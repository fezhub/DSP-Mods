using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace DSP_Mods.CopyInserters
{
    [BepInPlugin("org.fezeral.plugins.copyinserters", "Copy Inserters Plug-In", "1.0.0.0")]
    class CopyInserters : BaseUnityPlugin
    {
        private static PlayerController _pc;
        internal static PlayerController pc
        {
            get
            {
                if (_pc == null)
                {
                    var go = GameObject.Find("Player (Icarus)");
                    _pc = go.GetComponent<PlayerController>();
                }
                return _pc;
            }
        }
        Harmony harmony;
        internal void Awake()
        {
            harmony = new Harmony("org.fezeral.plugins.copyinserters");
            harmony.PatchAll(typeof(PatchCopyInserters));

            PatchCopyInserters.cachedInserters = new List<PatchCopyInserters.CachedInserter>();
            PatchCopyInserters.pendingInserters = new List<PatchCopyInserters.PendingInserter>();
        }
        internal void OnDestroy()
        {
            harmony.UnpatchSelf();  // For ScriptEngine hot-reloading
        }

        [HarmonyPatch]
        public class PatchCopyInserters
        {
            internal static List<CachedInserter> cachedInserters; // During copy mode, cached info on inserters attached to the copied building
            internal static List<PendingInserter> pendingInserters; // Info on inserters for every unbuilt pasted building

            /// <summary>
            /// After any item has completed building, check if there are pendingInserters to request
            /// </summary>
            /// <param name="postObjId">The built entities object ID</param>
            [HarmonyPrefix]
            [HarmonyPatch(typeof(PlayerAction_Build), "NotifyBuilt")]
            public static void PlayerAction_BuildNotifyBuiltPrefix(int postObjId, PlanetAuxData ___planetAux)
            {
                var entityBuilt = pc.player.factory.entityPool[postObjId];
                                
                ModelProto modelProto = LDB.models.Select(entityBuilt.modelIndex);
                var prefabDesc = modelProto.prefabDesc;
                if (!prefabDesc.isInserter)
                {
                    // Check for pending inserter requests
                    if (PatchCopyInserters.pendingInserters.Count > 0)
                    {
                        var factory = pc.player.factory;
                        for(int i = pendingInserters.Count -1; i >= 0; i--) // Reverse loop for removing found elements
                        {
                            var pi = pendingInserters[i];
                            // Is the NotifyBuilt assembler in the expected position for this pending inserter?
                            var distance = Vector3.Distance(entityBuilt.pos, pi.AssemblerPos);
                            if (distance < 0.2)
                            {
                                var assemblerId = entityBuilt.id;
                                //Debug.Log($"!!! found assembler id={assemblerId} at Pos={entityBuilt.pos} expected {pi.AssemblerPos} distance={distance}");
                                
                                // Create inserter Prebuild data
                                var pbdata = new PrebuildData();
                                pbdata.protoId = (short)pi.ci.protoId;
                                pbdata.modelIndex = (short)LDB.items.Select(pi.ci.protoId).ModelIndex;
                                pbdata.modelId = factory.entityPool[pi.otherId].modelId;
                                pbdata.rot = pi.ci.rot;
                                pbdata.rot2 = pi.ci.rot2;

                                // Copy rot from building, smelters have problems here
                                pbdata.rot = entityBuilt.rot;
                                pbdata.rot2 = entityBuilt.rot;
                                
                                pbdata.insertOffset = pi.ci.insertOffset;
                                pbdata.pickOffset = pi.ci.pickOffset;
                                pbdata.filterId = pi.ci.filterId;
                                
                                // Calculate inserter start and end positions from stored delta's
                                pbdata.pos = entityBuilt.pos + pi.ci.posDelta;
                                pbdata.pos = ___planetAux.Snap(pbdata.pos, true, false);
                                pbdata.pos2 = ___planetAux.Snap(pbdata.pos + pi.ci.pos2delta, true, false);

                                // Reverse positions for inserters that unload the copied building
                                if (!pi.ci.incoming)
                                {
                                    pbdata.pos = ___planetAux.Snap(entityBuilt.pos - pi.ci.posDelta, true, false);
                                    pbdata.pos2 = ___planetAux.Snap(pbdata.pos + pi.ci.pos2delta, true, false);
                                }

                                // Check the player has the item in inventory, no cheating here
                                var itemcount = pc.player.package.GetItemCount(pi.ci.protoId);
                                // If player has none; skip this request, as we dont create prebuild ghosts, must avoid confusion
                                if (itemcount > 0)
                                {
                                    var qty = 1;
                                    pc.player.package.TakeTailItems(ref pi.ci.protoId, ref qty);
                                    int pbCursor = factory.AddPrebuildData(pbdata); // Add the inserter request to Prebuild pool

                                    // Otherslot -1 will try to find one, otherwise could cache this from original assembler if it causes problems
                                    if (pi.ci.incoming)
                                    {                                        
                                        factory.WriteObjectConn(-pbCursor, 0, true, assemblerId, -1); // assembler connection                                        
                                        factory.WriteObjectConn(-pbCursor, 1, false, pi.otherId, -1); // other connection
                                    }
                                    else
                                    {                                        
                                        factory.WriteObjectConn(-pbCursor, 0, false, assemblerId, -1); // assembler connection                                        
                                        factory.WriteObjectConn(-pbCursor, 1, true, pi.otherId, -1); // other connection
                                    }
                                }
                                pendingInserters.RemoveAt(i);
                            }
                        }
                    }
                }                
            }

            /// <summary>
            /// When entering Copy mode, cache info for all inserters attached to the copied building
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch(typeof(PlayerAction_Build), "SetCopyInfo")]
            public static void PlayerAction_BuildSetCopyInfoPostfix(ref PlanetFactory ___factory, int objectId, PlanetAuxData ___planetAux)
            {
                cachedInserters.Clear(); // Remove previous copy info

                var sourceEntity = objectId;
                var sourcePos = ___factory.entityPool[objectId].pos;
                // Find connected inserters                
                int matches = 0;
                var inserterPool = ___factory.factorySystem.inserterPool;
                var entityPool = ___factory.entityPool;
                for (int i = 1; i < ___factory.factorySystem.inserterCursor; i++)
                {
                    if (inserterPool[i].id == i)
                    {
                        var inserter = inserterPool[i];
                        var pickTarget = inserter.pickTarget;
                        var insertTarget = inserter.insertTarget;
                        if (pickTarget == sourceEntity || insertTarget == sourceEntity)
                        {
                            matches++;
                            var inserterType = ___factory.entityPool[inserter.entityId].protoId;
                            bool incoming = insertTarget == sourceEntity;
                            var otherId = incoming ? pickTarget : insertTarget; // The belt or other building this inserter is attached to
                            var otherPos = ___factory.entityPool[otherId].pos;

                            // Store the Grid-Snapped moves from assembler to belt/other                            
                            Vector3 begin = sourcePos;
                            Vector3 end = otherPos;
                            int path = 0;
                            Vector3[] snaps = new Vector3[6];
                            var snappedPointCount = ___planetAux.SnapLineNonAlloc(begin, end, ref path, snaps);
                            Vector3 lastSnap = begin;
                            Vector3[] snapMoves = new Vector3[snappedPointCount];
                            for (int s = 0; s < snappedPointCount; s++)
                            {
                                Vector3 snapMove = snaps[s] - lastSnap;
                                snapMoves[s] = snapMove;
                                lastSnap = snaps[s];
                            }

                            // Cache info for this inserter
                            var ci = new CachedInserter();
                            ci.otherDelta = (otherPos - sourcePos); // Delta from copied building to other belt/building
                            ci.incoming = incoming;
                            ci.protoId = inserterType;
                            ci.rot = ___factory.entityPool[inserter.entityId].rot;
                            ci.rot2 = inserter.rot2;
                            ci.pos2delta = (inserter.pos2 - entityPool[inserter.entityId].pos); // Delta from inserter pos2 to copied building
                            var posDelta = entityPool[inserter.entityId].pos - sourcePos; // Delta from copied building to inserter pos
                            if (!incoming) posDelta = sourcePos - entityPool[inserter.entityId].pos; // Reverse for outgoing inserters
                            ci.posDelta = posDelta;

                            // not important?
                            ci.pickOffset = inserter.pickOffset;
                            ci.insertOffset = inserter.insertOffset;
                            // needed for pose?
                            ci.t1 = inserter.t1;
                            ci.t2 = inserter.t2;
                            

                            ci.filterId = inserter.filter;
                            ci.snapMoves = snapMoves;
                            ci.snapCount = snappedPointCount;

                            cachedInserters.Add(ci);
                        }
                    }
                }
            }


            // Store copied assemblers inserter information for finding targets on paste
            internal class CachedInserter
            {
                public int protoId;
                public bool incoming;
                public Vector3 posDelta;
                public Vector3 pos2delta;
                public Vector3 otherDelta;
                public Quaternion rot;
                public Quaternion rot2;
                public short pickOffset;
                public short insertOffset;
                public short t1;
                public short t2;
                public int filterId;
                public Vector3[] snapMoves;
                public int snapCount;
            }

            //For inserters that need to be built when assembler is ready
            internal class PendingInserter
            {
                public CachedInserter ci;
                public int otherId;
                public Vector3 AssemblerPos;
            }


            /// <summary>
            /// After player designates a PreBuild, check if it is an assembler that has Cached Inserters
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch(typeof(PlayerAction_Build), "AfterPrebuild")]
            public static void PlayerAction_BuildAfterPrebuildPostfix(PlayerAction_Build __instance, ref PlanetFactory ___factory, PlanetAuxData ___planetAux)
            {
                // Do we have cached inserters?
                var ci = PatchCopyInserters.cachedInserters;
                if (ci.Count > 0)
                {
                    var targetPos = __instance.buildTargetPositionWanted;
                    var entityPool = ___factory.entityPool;
                    foreach (var inserter in ci)
                    {
                        // Find the desired belt/building position
                        // As delta doesn't work over distance, re-trace the Grid Snapped steps from the original
                        // to find the target belt/building for this inserters other connection
                        var currentPos = targetPos;
                        for (int u = 0; u < inserter.snapCount; u++)
                            currentPos = ___planetAux.Snap(currentPos + inserter.snapMoves[u], true, false);

                        var testPos = currentPos;

                        // Find the other entity at the target location
                        int otherId = 0;
                        for (int x = 1; x < ___factory.entityCursor; x++)
                        {
                            if (entityPool[x].id == x)
                            {
                                var distance = Vector3.Distance(entityPool[x].pos, testPos);
                                if (distance < 0.2)
                                {
                                    otherId = entityPool[x].id;
                                    break;
                                }
                            }
                        }
                        
                        if (otherId != 0)
                        {
                            // Order an inserter
                            var pi = new PatchCopyInserters.PendingInserter();
                            pi.otherId = otherId;
                            pi.ci = inserter;
                            pi.AssemblerPos = targetPos;

                            PatchCopyInserters.pendingInserters.Add(pi);
                        }
                    }
                }
            }

            /// <summary>
            /// Clear the cached inserters when the player exits out of copy mode
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch(typeof(PlayerAction_Build), "ResetCopyInfo")]
            public static void PlayerAction_BuildResetCopyInfoPostfix()
            {
                PatchCopyInserters.cachedInserters.Clear();
            }
        }
    }
}
