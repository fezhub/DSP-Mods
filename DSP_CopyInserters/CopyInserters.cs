using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace DSP_Mods.CopyInserters
{
    [BepInPlugin("org.fezeral.plugins.copyinserters", "Copy Inserters Plug-In", "1.1.0.0")]
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
                        for(int i = pendingInserters.Count - 1; i >= 0; i--) // Reverse loop for removing found elements
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

                                // Get inserter rotation relative to the building's
                                pbdata.rot = entityBuilt.rot * pi.ci.rot;
                                pbdata.rot2 = entityBuilt.rot * pi.ci.rot2;
                                
                                pbdata.insertOffset = pi.ci.insertOffset;
                                pbdata.pickOffset = pi.ci.pickOffset;
                                pbdata.filterId = pi.ci.filterId;

                                // Calculate inserter start and end positions from stored deltas and the building's rotation
                                pbdata.pos = ___planetAux.Snap(entityBuilt.pos + entityBuilt.rot * pi.ci.posDelta, true, false);
                                pbdata.pos2 = ___planetAux.Snap(entityBuilt.pos + entityBuilt.rot * pi.ci.pos2Delta, true, false);

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
                if (objectId < 0) // Copied item is a ghost, no inserters to cache
                    return;

                var sourceEntity = objectId;
                var sourcePos = ___factory.entityPool[objectId].pos;
                var sourceRot = ___factory.entityPool[objectId].rot;
                // Find connected inserters
                var inserterPool = ___factory.factorySystem.inserterPool;
                var entityPool = ___factory.entityPool;
                for (int i = 1; i < ___factory.factorySystem.inserterCursor; i++)
                {
                    if (inserterPool[i].id == i)
                    {
                        var inserter = inserterPool[i];
                        var inserterEntity = entityPool[inserter.entityId];

                        var pickTarget = inserter.pickTarget;
                        var insertTarget = inserter.insertTarget;

                        if (pickTarget == sourceEntity || insertTarget == sourceEntity)
                        {
                            bool incoming = insertTarget == sourceEntity;
                            var otherId = incoming ? pickTarget : insertTarget; // The belt or other building this inserter is attached to
                            var otherPos = entityPool[otherId].pos;

                            // Store the Grid-Snapped moves from assembler to belt/other
                            int path = 0;
                            Vector3[] snaps = new Vector3[6];
                            var snappedPointCount = ___planetAux.SnapLineNonAlloc(sourcePos, otherPos, ref path, snaps);
                            Vector3 lastSnap = sourcePos;
                            Vector3[] snapMoves = new Vector3[snappedPointCount];
                            for (int s = 0; s < snappedPointCount; s++)
                            {
                                // note: reverse rotation of the delta so that rotation works
                                Vector3 snapMove = Quaternion.Inverse(sourceRot) * (snaps[s] - lastSnap);
                                snapMoves[s] = snapMove;
                                lastSnap = snaps[s];
                            }

                            // Cache info for this inserter
                            var ci = new CachedInserter();
                            ci.incoming = incoming;
                            ci.protoId = inserterEntity.protoId;

                            // rotations + deltas relative to the source building's rotation
                            ci.rot = Quaternion.Inverse(sourceRot) * inserterEntity.rot;
                            ci.rot2 = Quaternion.Inverse(sourceRot) * inserter.rot2;
                            ci.posDelta = Quaternion.Inverse(sourceRot) * (inserterEntity.pos - sourcePos); // Delta from copied building to inserter pos
                            ci.pos2Delta = Quaternion.Inverse(sourceRot) * (inserter.pos2 - sourcePos); // Delta from copied building to inserter pos2

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
                public Vector3 pos2Delta;
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
                    foreach (var buildPreview in __instance.buildPreviews)
                    {
                        var targetPos = __instance.previewPose.position + __instance.previewPose.rotation * buildPreview.lpos;
                        var targetRot = __instance.previewPose.rotation;

                        var entityPool = ___factory.entityPool;
                        foreach (var inserter in ci)
                        {
                            // Find the desired belt/building position
                            // As delta doesn't work over distance, re-trace the Grid Snapped steps from the original
                            // to find the target belt/building for this inserters other connection
                            var testPos = targetPos;
                            // Note: rotate's each move relative to the rotation of the new building
                            for (int u = 0; u < inserter.snapCount; u++)
                                testPos = ___planetAux.Snap(testPos + targetRot * inserter.snapMoves[u], true, false);

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
