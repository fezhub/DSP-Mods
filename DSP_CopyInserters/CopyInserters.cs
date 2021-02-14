using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DSP_Mods.CopyInserters
{
    [BepInPlugin("org.fezeral.plugins.copyinserters", "Copy Inserters Plug-In", "1.1.0.0")]
    class CopyInserters : BaseUnityPlugin
    {
        public static bool copyEnabled = true;
        public static List<UIKeyTipNode> allTips;
        public static UIKeyTipNode tip;
        void Update()
        {
  
            if (Input.GetKeyUp(KeyCode.LeftControl))
            {
                copyEnabled = !copyEnabled;
            }
        }
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
            try
            {
                harmony.PatchAll(typeof(PatchCopyInserters));
                harmony.PatchAll(typeof(CopyInserters));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            PatchCopyInserters.cachedInserters = new List<PatchCopyInserters.CachedInserter>();
            PatchCopyInserters.pendingInserters = new List<PatchCopyInserters.PendingInserter>();
        }
        internal void OnDestroy()
        {
            harmony.UnpatchSelf();  // For ScriptEngine hot-reloading
            allTips.Remove(tip);
        }

        public static bool IsCopyAvailable()
        {
            return UIGame.viewMode == EViewMode.Build && pc.cmd.mode == 1 && PatchCopyInserters.cachedInserters.Count > 0;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIKeyTips), "UpdateTipDesiredState")]
        public static void UpdateTipDesiredStatePatch(UIKeyTips __instance, ref List<UIKeyTipNode> ___allTips)
        {
            if (!tip)
            {
                allTips = ___allTips;
                tip = __instance.RegisterTip("L-CTRL", "Toggle inserters copy");
            }
            tip.desired = IsCopyAvailable();
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(UIGeneralTips), "_OnUpdate")]
        public static void TipsPatch(ref Text ___modeText)
        {
        if (IsCopyAvailable() && copyEnabled)
            {
                ___modeText.text += " - Copy inserters";
            }
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
            [HarmonyPostfix]
            [HarmonyPatch(typeof(PlayerAction_Build), "DetermineBuildPreviews")]
            public static void PlayerAction_BuildDetermineBuildPreviewsPostfix(PlayerAction_Build __instance)
            {
                // Do we have cached inserters?

                var ci = PatchCopyInserters.cachedInserters;
                if (CopyInserters.copyEnabled && ci.Count > 0)
                {
                    var bpCount = __instance.buildPreviews.Count;
                    for (int i = 0; i < bpCount; i++)
                    {
                        var buildingPreview = __instance.buildPreviews[i];
                        if (!buildingPreview.item.prefabDesc.isInserter)
                        {
                            foreach (var inserter in ci)
                            {
                                var bp = BuildPreview.CreateSingle(LDB.items.Select(inserter.protoId), LDB.items.Select(inserter.protoId).prefabDesc, true);
                                bp.ResetInfos();
                                
                                bp.lpos = buildingPreview.lpos + buildingPreview.lrot * inserter.posDelta;
                                bp.lrot = buildingPreview.lrot * inserter.rot;
                                bp.lpos2 = buildingPreview.lpos + buildingPreview.lrot * inserter.pos2Delta;
                                bp.lrot2 = buildingPreview.lrot * inserter.rot2;
                                bp.ignoreCollider = true;
                                __instance.AddBuildPreview(bp);
                            }
                        }
                        
                    }
                    
                }

            }



            [HarmonyPostfix]
            [HarmonyPatch(typeof(PlayerAction_Build), "CheckBuildConditions")]
            public static void PlayerAction_BuildCheckBuildConditionsPostfix(PlayerAction_Build __instance, ref bool __result)
            {
                var ci = PatchCopyInserters.cachedInserters;

                if (CopyInserters.copyEnabled && ci.Count > 0)
                {
                    __instance.cursorText = __instance.prepareCursorText;
                    __instance.prepareCursorText = string.Empty;
                    __instance.cursorWarning = false;
                    UICursor.SetCursor(ECursor.Default);
                    var flag = true;
                    for (int i = 0; i < __instance.buildPreviews.Count; i++)
                    {
                        BuildPreview buildPreview = __instance.buildPreviews[i];
                        bool isInserter = buildPreview.desc.isInserter;

                        if (isInserter && buildPreview.ignoreCollider && (
                            buildPreview.condition == EBuildCondition.TooFar ||
                            buildPreview.condition == EBuildCondition.TooClose ||
                            buildPreview.condition == EBuildCondition.OutOfReach))
                        {
                            buildPreview.condition = EBuildCondition.Ok;
                        } 
                        
                        if(buildPreview.condition != EBuildCondition.Ok)
                        {
                            flag = false;
                            if (!__instance.cursorWarning)
                            {
                                __instance.cursorWarning = true;
                                __instance.cursorText = buildPreview.conditionText;
                            }
                        }
                    }

                    if (!flag && !VFInput.onGUI)
                    {
                        UICursor.SetCursor(ECursor.Ban);
                    }

                    __result = flag;
                }
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(PlayerAction_Build), "CreatePrebuilds")]
            public static void PlayerAction_BuildCreatePrebuildsPrefix(PlayerAction_Build __instance)
            {
                var ci = PatchCopyInserters.cachedInserters;
                if (CopyInserters.copyEnabled && ci.Count > 0) 
                {
                    if (__instance.waitConfirm && VFInput._buildConfirm.onDown && __instance.buildPreviews.Count > 0)
                    {
                        // for now we remove the inserters prebuilds
                        for (int i = __instance.buildPreviews.Count - 1; i >= 0; i--) // Reverse loop for removing found elements
                            {
                                var buildPreview = __instance.buildPreviews[i];
                                bool isInserter = buildPreview.desc.isInserter;

                                if (isInserter && buildPreview.ignoreCollider)
                                {
                                    __instance.buildPreviews.RemoveAt(i);
                                    __instance.FreePreviewModel(buildPreview);
                                }
                            }
                    }
                }
            }


            [HarmonyReversePatch]
            [HarmonyPatch(typeof(PlayerAction_Build), "DetermineBuildPreviews")]
            public static void CalculatePose(PlayerAction_Build __instance, int startObjId, int castObjId)
            {
                IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
                {
                    List<CodeInstruction> instructionsList = instructions.ToList();

                    // Find the idx at which the "cargoTraffic" field of the PlanetFactory
                    // Is first accessed since this is the start of the instructions that compute posing

                    /* ex of the code in dotpeek:
                     * ```
                     * if (this.cursorValid && this.startObjId != this.castObjId && (this.startObjId > 0 && this.castObjId > 0))
                     * {
                     *   CargoTraffic cargoTraffic = this.factory.cargoTraffic; <- WE WANT TO START WITH THIS LINE (INCLUSIVE)
                     *   EntityData[] entityPool = this.factory.entityPool;
                     *   BeltComponent[] beltPool = cargoTraffic.beltPool;
                     *   this.posePairs.Clear();
                     *   this.startSlots.Clear();
                     * ```
                     */

                    int startIdx = -1;
                    for (int i = 0; i < instructionsList.Count; i++)
                    {
                        if (instructionsList[i].LoadsField(typeof(PlanetFactory).GetField("cargoTraffic")))
                        {
                            startIdx = i - 2; // need the two proceeding lines that are ldarg.0 and ldfld PlayerAction_Build::factory
                            break;
                        }
                    }
                    if (startIdx == -1)
                    {
                        throw new InvalidOperationException("Cannot patch sorter posing code b/c the start indicator isn't present");
                    }

                    // Find the idx at which the "posePairs" field of the PlayerAction_Build
                    // Is first accessed and followed by a call to get_Count

                    /*
                     * ex of the code in dotpeek:
                     * ```
                     *          else
                     *              flag6 = true;
                     *      }
                     *      else
                     *        flag6 = true;
                     *    }
                     *  }
                     *  if (this.posePairs.Count > 0) <- WE WANT TO END ON THIS LINE (EXCLUSIVE)
                     *  {
                     *    float num1 = 1000f;
                     *    float num2 = Vector3.Distance(this.currMouseRay.origin, this.cursorTarget) + 10f;
                     *    PlayerAction_Build.PosePair posePair2 = new PlayerAction_Build.PosePair();
                     * ```
                     */

                    int endIdx = -1;
                    for (int i = startIdx; i < instructionsList.Count - 1; i++) // go to the end - 1 b/c we need to check two instructions to find valid loc
                    {
                        if (instructionsList[i].LoadsField(typeof(PlayerAction_Build).GetField("posePairs")))
                        {
                            if (instructionsList[i + 1].Calls(typeof(List<PlayerAction_Build.PosePair>).GetMethod("get_Count")))
                            {
                                endIdx = i - 1; // need the proceeding line that is ldarg.0
                                break;
                            }
                        }
                    }
                    if (endIdx == -1)
                    {
                        throw new InvalidOperationException("Cannot patch sorter posing code b/c the end indicator isn't present");
                    }

                    // The first argument to an instance method (arg 0) is the instance itself
                    // Since this is a static method, the instance will still need to be passed
                    // For the IL instructions to work properly so manually pass the instance as
                    // The first argument to the method.
                    List<CodeInstruction> code = new List<CodeInstruction>()
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldarg_1),
                        CodeInstruction.StoreField(typeof(PlayerAction_Build), "startObjId"),
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldarg_2),
                        CodeInstruction.StoreField(typeof(PlayerAction_Build), "castObjId"),
                    };

                    for (int i = startIdx; i < endIdx; i++)
                    {
                        code.Add(instructionsList[i]);
                    }
                    return code.AsEnumerable();
                }

                // make compiler happy
                _ = Transpiler(null);
                return;
            }

            /// <summary>
            /// After any item has completed building, check if there are pendingInserters to request
            /// </summary>
            /// <param name="postObjId">The built entities object ID</param>
            [HarmonyPrefix]
            [HarmonyPatch(typeof(PlayerAction_Build), "NotifyBuilt")]
            public static void PlayerAction_BuildNotifyBuiltPrefix(PlayerAction_Build __instance, int postObjId, PlanetAuxData ___planetAux)
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
                        for (int i = pendingInserters.Count - 1; i >= 0; i--) // Reverse loop for removing found elements
                        {
                            var pi = pendingInserters[i];
                            // Is the NotifyBuilt assembler in the expected position for this pending inserter?
                            var distance = Vector3.Distance(entityBuilt.pos, pi.AssemblerPos);
                            if (distance < 0.2)
                            {
                                var assemblerId = entityBuilt.id;

                                // Create inserter Prebuild data
                                var pbdata = new PrebuildData();
                                pbdata.protoId = (short)pi.ci.protoId;
                                pbdata.modelIndex = (short)LDB.items.Select(pi.ci.protoId).ModelIndex;

                                pbdata.insertOffset = pi.ci.insertOffset;
                                pbdata.pickOffset = pi.ci.pickOffset;
                                pbdata.filterId = pi.ci.filterId;

                                // Calculate inserter start and end positions from stored deltas and the building's rotation
                                pbdata.pos = ___planetAux.Snap(entityBuilt.pos + entityBuilt.rot * pi.ci.posDelta, true, false);
                                pbdata.pos2 = ___planetAux.Snap(entityBuilt.pos + entityBuilt.rot * pi.ci.pos2Delta, true, false);
                                // Get inserter rotation relative to the building's
                                pbdata.rot = entityBuilt.rot * pi.ci.rot;
                                pbdata.rot2 = entityBuilt.rot * pi.ci.rot2;

                                if (!pi.ci.incoming)
                                {
                                    CalculatePose(__instance, assemblerId, pi.otherId);
                                }
                                else
                                {
                                    CalculatePose(__instance, pi.otherId, assemblerId);
                                }
                                if (__instance.posePairs.Count > 0)
                                {
                                    float minDistance = 1000f;
                                    PlayerAction_Build.PosePair bestFit = new PlayerAction_Build.PosePair();
                                    bool hasNearbyPose = false;
                                    for (int j = 0; j < __instance.posePairs.Count; ++j)
                                    {
                                        if (__instance.posePairs[j].startSlot != pi.ci.startSlot || __instance.posePairs[j].endSlot != pi.ci.endSlot)
                                        {
                                            continue;
                                        }
                                        float startDistance = Vector3.Distance(__instance.posePairs[j].startPose.position, pbdata.pos);
                                        float endDistance = Vector3.Distance(__instance.posePairs[j].endPose.position, pbdata.pos2);
                                        float poseDistance = startDistance + endDistance;

                                        if (poseDistance < minDistance)
                                        {
                                            minDistance = poseDistance;
                                            bestFit = __instance.posePairs[j];
                                            hasNearbyPose = true;
                                        }
                                    }
                                    if (hasNearbyPose)
                                    {
                                        // if we were able to calculate a close enough sensible pose
                                        // use that instead of the (visually) ugly default

                                        pbdata.pos = bestFit.startPose.position;
                                        pbdata.pos2 = bestFit.endPose.position;

                                        pbdata.rot = bestFit.startPose.rotation;
                                        pbdata.rot2 = bestFit.endPose.rotation * Quaternion.Euler(0.0f, 180f, 0.0f);
                                    }
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
            public static void PlayerAction_BuildSetCopyInfoPostfix(PlayerAction_Build __instance, ref PlanetFactory ___factory, int objectId, PlanetAuxData ___planetAux)
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


                            // compute the start and end slot that the cached inserter uses
                            if (!incoming)
                            {
                                CalculatePose(__instance, sourceEntity, otherId);
                            }
                            else
                            {
                                CalculatePose(__instance, otherId, sourceEntity);
                            }

                            if (__instance.posePairs.Count > 0)
                            {
                                float minDistance = 1000f;
                                PlayerAction_Build.PosePair bestFit = new PlayerAction_Build.PosePair();
                                bool hasNearbyPose = false;
                                for (int j = 0; j < __instance.posePairs.Count; ++j)
                                {
                                    float startDistance = Vector3.Distance(__instance.posePairs[j].startPose.position, inserterEntity.pos);
                                    float endDistance = Vector3.Distance(__instance.posePairs[j].endPose.position, inserter.pos2);
                                    float poseDistance = startDistance + endDistance;

                                    if (poseDistance < minDistance)
                                    {
                                        minDistance = poseDistance;
                                        bestFit = __instance.posePairs[j];
                                        hasNearbyPose = true;
                                    }
                                }
                                if (hasNearbyPose)
                                {
                                    ci.startSlot = bestFit.startSlot;
                                    ci.endSlot = bestFit.endSlot;
                                }
                            }

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
                public int startSlot;
                public int endSlot;
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
                if (CopyInserters.copyEnabled && ci.Count > 0)
                {
                    foreach (var buildPreview in __instance.buildPreviews)
                    {
                        Vector3 targetPos;
                        Quaternion targetRot;
                        if (__instance.buildPreviews.Count > 1)
                        {
                            targetPos = buildPreview.lpos;
                            targetRot = buildPreview.lrot;
                        }
                        else
                        {
                            targetPos = __instance.previewPose.position + __instance.previewPose.rotation * buildPreview.lpos;
                            targetRot = __instance.previewPose.rotation;
                        }
                        var entityPool = ___factory.entityPool;
                        foreach (var inserter in ci)
                        {
                            // Find the desired belt/building position
                            // As delta doesn't work over distance, re-trace the Grid Snapped steps from the original
                            // to find the target belt/building for this inserters other connection
                            var testPos = targetPos;
                            // Note: rotates each move relative to the rotation of the new building
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
                CopyInserters.copyEnabled = true;
            }
        }
    }
}
