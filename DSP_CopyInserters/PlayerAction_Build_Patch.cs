using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;

namespace DSP_Mods.CopyInserters
{
    class PlayerAction_Build_Patch
    {
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
            public int refCount;
            public bool otherIsBelt;
        }

        internal class InserterPosition
        {
            public CachedInserter cachedInserter;
            public Vector3 absoluteBuildingPos;
            public Quaternion absoluteBuildingRot;

            public Vector3 absoluteInserterPos;
            public Vector3 absoluteInserterPos2;
            public Quaternion absoluteInserterRot;
            public Quaternion absoluteInserterRot2;

            public Vector3 posDelta;
            public Vector3 pos2Delta;

            public int startSlot;
            public int endSlot;

            public short pickOffset;
            public short insertOffset;

            public int otherId;

            public EBuildCondition? condition;
        }

        internal static List<CachedInserter> cachedInserters; // During copy mode, cached info on inserters attached to the copied building

        private static int[] _nearObjectIds = new int[4096];
        private static Collider[] _tmp_cols = new Collider[256];

        private static bool overridePoseMethods = false;
        private static ItemProto overriddenProto;
        private static Pose overriddenPose;

        public static Queue<InserterPosition> currentPositionCache;
        public static Queue<InserterPosition> nextPositionCache;

        private static void SwapPositionCache()
        {
            currentPositionCache = nextPositionCache;
            nextPositionCache = new Queue<InserterPosition>();
        }

        private static InserterPosition GetPositions(PlayerAction_Build __instance, PlanetFactory ___factory, PlanetAuxData ___planetAux, NearColliderLogic ___nearcdLogic, BuildPreview buildPreview, CachedInserter cachedInserter, bool useCache = true)
        {

            Vector3 absoluteBuildingPos;
            Quaternion absoluteBuildingRot;

            // When using AdvancedBuildDestruct mod, all buildPreviews are positioned 'absolutely' on the planet surface.
            // In 'normal' mode the buildPreviews are relative to __instance.previewPose.
            // This means that in 'normal' mode the (only) buildPreview is always positioned at {0,0,0}

            if (buildPreview.lpos == Vector3.zero)
            {
                absoluteBuildingPos = __instance.previewPose.position;
                absoluteBuildingRot = __instance.previewPose.rotation;
            }
            else
            {
                absoluteBuildingPos = buildPreview.lpos;
                absoluteBuildingRot = buildPreview.lrot;
            }

            InserterPosition position = null;
            if (useCache && currentPositionCache.Count > 0)
            {
                position = currentPositionCache.Dequeue();
            }

            bool isCacheValid = position != null &&
                position.cachedInserter == cachedInserter &&
                position.absoluteBuildingPos == absoluteBuildingPos &&
                position.absoluteBuildingRot == absoluteBuildingRot;

            if (isCacheValid)
            {
                nextPositionCache.Enqueue(position);
                return position;
            }


            var posDelta = cachedInserter.posDelta;
            var pos2Delta = cachedInserter.pos2Delta;

            Vector3 absoluteInserterPos = absoluteBuildingPos + absoluteBuildingRot * cachedInserter.posDelta;
            Vector3 absoluteInserterPos2 = absoluteBuildingPos + absoluteBuildingRot * cachedInserter.pos2Delta;

            Quaternion absoluteInserterRot = absoluteBuildingRot * cachedInserter.rot;
            Quaternion absoluteInserterRot2 = absoluteBuildingRot * cachedInserter.rot2;

            int startSlot = cachedInserter.startSlot;
            int endSlot = cachedInserter.endSlot;

            short pickOffset = cachedInserter.pickOffset;
            short insertOffset = cachedInserter.insertOffset;

            // Find the desired belt/building position
            // As delta doesn't work over distance, re-trace the Grid Snapped steps from the original
            // to find the target belt/building for this inserters other connection
            var testPos = absoluteBuildingPos;
            // Note: rotates each move relative to the rotation of the new building
            for (int u = 0; u < cachedInserter.snapCount; u++)
                testPos = ___planetAux.Snap(testPos + absoluteBuildingRot * cachedInserter.snapMoves[u], true, false);

            // Find the other entity at the target location
            int otherId = 0;

            // find building nearby
            int found = ___nearcdLogic.GetBuildingsInAreaNonAlloc(testPos, 0.2f, _nearObjectIds, false);

            // find nearest building
            float maxDistance = 0.2f;
            for (int x = 0; x < found; x++)
            {
                var id = _nearObjectIds[x];
                float distance;
                ItemProto proto;
                if (id == 0 || id == buildPreview.objId)
                {
                    continue;
                }
                else if (id > 0)
                {
                    EntityData entityData = ___factory.entityPool[id];
                    proto = LDB.items.Select((int)entityData.protoId);
                    distance = Vector3.Distance(entityData.pos, testPos);
                }
                else
                {
                    PrebuildData prebuildData = ___factory.prebuildPool[-id];
                    proto = LDB.items.Select((int)prebuildData.protoId);
                    if (proto.prefabDesc.isBelt)
                    {
                        // ignore unbuilt belts
                        continue;
                    }
                    distance = Vector3.Distance(prebuildData.pos, testPos);
                }

                // ignore entitites that ore not (built) belts or don't have inserterPoses
                if ((proto.prefabDesc.isBelt == cachedInserter.otherIsBelt || proto.prefabDesc.insertPoses.Length > 0) && distance < maxDistance)
                {
                    otherId = id;
                    maxDistance = distance;
                }

            }

            if (otherId != 0)
            {
                var buildingId = buildPreview.objId;

                if (buildingId == 0)
                {
                    // the original calculatePose code doesn't correctly handle calculation where one of the entity is a BuildPreview
                    // as it has objId 0 and is not registered in either the entityPool or prebuildPool
                    // To address that we override the 4 critical methods GetObjectPose/GetObjectProtoId/ObjectIsBelt/GetLocalInserts
                    // only for this specific execution of CalculatePose, by returning the expected value for the current buildPreview
                    overridePoseMethods = true;
                    overriddenProto = buildPreview.item;
                    overriddenPose = new Pose(absoluteBuildingPos, absoluteBuildingRot);
                }

                if (cachedInserter.incoming)
                {
                    CalculatePose(__instance, otherId, buildingId);
                }
                else
                {
                    CalculatePose(__instance, buildingId, otherId);
                }


                overridePoseMethods = false;

                bool hasNearbyPose = false;
                if (__instance.posePairs.Count > 0)
                {
                    float minDistance = 1000f;
                    PlayerAction_Build.PosePair bestFit = new PlayerAction_Build.PosePair();

                    for (int j = 0; j < __instance.posePairs.Count; ++j)
                    {
                        var posePair = __instance.posePairs[j];
                        if (
                            (cachedInserter.incoming && cachedInserter.endSlot != posePair.endSlot) ||
                            (!cachedInserter.incoming && cachedInserter.startSlot != posePair.startSlot)
                            )
                        {
                            continue;
                        }
                        float startDistance = Vector3.Distance(posePair.startPose.position, absoluteInserterPos);
                        float endDistance = Vector3.Distance(posePair.endPose.position, absoluteInserterPos2);
                        float poseDistance = startDistance + endDistance;

                        if (poseDistance < minDistance)
                        {
                            minDistance = poseDistance;
                            bestFit = posePair;
                            hasNearbyPose = true;
                        }
                    }
                    if (hasNearbyPose)
                    {
                        // if we were able to calculate a close enough sensible pose
                        // use that instead of the (visually) imprecise default

                        absoluteInserterPos = bestFit.startPose.position;
                        absoluteInserterPos2 = bestFit.endPose.position;

                        absoluteInserterRot = bestFit.startPose.rotation;
                        absoluteInserterRot2 = bestFit.endPose.rotation * Quaternion.Euler(0.0f, 180f, 0.0f);

                        pickOffset = (short)bestFit.startOffset;
                        insertOffset = (short)bestFit.endOffset;

                        startSlot = bestFit.startSlot;
                        endSlot = bestFit.endSlot;


                        posDelta = Quaternion.Inverse(absoluteBuildingRot) * (absoluteInserterPos - absoluteBuildingPos);
                        pos2Delta = Quaternion.Inverse(absoluteBuildingRot) * (absoluteInserterPos2 - absoluteBuildingPos);
                    }
                }
            }

            position = new InserterPosition()
            {
                cachedInserter = cachedInserter,
                absoluteBuildingPos = absoluteBuildingPos,
                absoluteBuildingRot = absoluteBuildingRot,

                otherId = otherId,

                posDelta = posDelta,
                pos2Delta = pos2Delta,
                absoluteInserterPos = absoluteInserterPos,
                absoluteInserterPos2 = absoluteInserterPos2,

                absoluteInserterRot = absoluteInserterRot,
                absoluteInserterRot2 = absoluteInserterRot2,

                pickOffset = pickOffset,
                insertOffset = insertOffset,

                startSlot = startSlot,
                endSlot = endSlot,
            };

            if (useCache)
            {
                nextPositionCache.Enqueue(position);
            }
            return position;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "GetObjectPose")]
        public static bool GetObjectPose_Prefix(int objId, ref Pose __result)
        {
            if (overridePoseMethods && objId == 0)
            {
                __result = overriddenPose;
                return false;
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "GetObjectProtoId")]
        public static bool GetObjectProtoId_Prefix(int objId, ref int __result)
        {
            if (overridePoseMethods && objId == 0)
            {
                __result = overriddenProto.ID;
                return false;
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "ObjectIsBelt")]
        public static bool ObjectIsBelt_Prefix(int objId, ref bool __result)
        {
            if (overridePoseMethods && objId == 0)
            {
                __result = false;
                return false;
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "GetLocalInserts")]
        public static bool GetLocalInserts_Prefix(int objId, ref Pose[] __result)
        {
            if (overridePoseMethods && objId == 0)
            {
                __result = overriddenProto.prefabDesc.insertPoses;
                return false;
            }

            return true;
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(PlayerAction_Build), "DetermineBuildPreviews")]
        public static void DetermineBuildPreviews_Postfix(PlayerAction_Build __instance, PlanetFactory ___factory, PlanetAuxData ___planetAux, NearColliderLogic ___nearcdLogic, Player ___player)
        {
            // Do we have cached inserters?
            var ci = cachedInserters;
            if (CopyInserters.copyEnabled && ci.Count > 0 && !__instance.multiLevelCovering)
            {
                var bpCount = __instance.buildPreviews.Count;

                for (int i = 0; i < bpCount; i++)
                {
                    BuildPreview buildPreview = __instance.buildPreviews[i];

                    if (!buildPreview.item.prefabDesc.isInserter)
                    {
                        foreach (var cachedInserter in ci)
                        {

                            var positionData = GetPositions(__instance, ___factory, ___planetAux, ___nearcdLogic, buildPreview, cachedInserter);

                            var bp = BuildPreview.CreateSingle(LDB.items.Select(cachedInserter.protoId), LDB.items.Select(cachedInserter.protoId).prefabDesc, true);
                            bp.ResetInfos();


                            bp.lrot = buildPreview.lrot * cachedInserter.rot;
                            bp.lrot2 = buildPreview.lrot * cachedInserter.rot2;


                            if (buildPreview.lpos == Vector3.zero)
                            {
                                bp.lpos = buildPreview.lpos + buildPreview.lrot * positionData.posDelta;
                                bp.lpos2 = buildPreview.lpos + buildPreview.lrot * positionData.pos2Delta;
                            }
                            else
                            {
                                bp.lpos = positionData.absoluteInserterPos;
                                bp.lpos2 = positionData.absoluteInserterPos2;
                            }



                            if (positionData.condition == null)
                            {
                                positionData.condition = EBuildCondition.Ok;

                                Vector3 lpos = positionData.absoluteInserterPos;
                                Vector3 lpos2 = positionData.absoluteInserterPos2;
                                Vector3 forward = lpos2 - lpos;

                                Pose pose;
                                pose.position = Vector3.Lerp(lpos, lpos2, 0.5f);
                                pose.rotation = Quaternion.LookRotation(forward, lpos.normalized);


                                var colliderData = bp.desc.buildColliders[0];
                                colliderData.ext = new Vector3(colliderData.ext.x, colliderData.ext.y, Vector3.Distance(lpos2, lpos) * 0.5f + colliderData.ext.z - 0.5f);

                                if (cachedInserter.otherIsBelt)
                                {
                                    if (cachedInserter.incoming)
                                    {
                                        colliderData.pos.z -= 0.4f;
                                        colliderData.ext.z += 0.4f;
                                    }
                                    else
                                    {
                                        colliderData.pos.z += 0.4f;
                                        colliderData.ext.z += 0.4f;
                                    }
                                }

                                if (colliderData.ext.z < 0.1f)
                                {
                                    colliderData.ext.z = 0.1f;
                                }
                                colliderData.pos = pose.position + pose.rotation * colliderData.pos;
                                colliderData.q = pose.rotation * colliderData.q;


                                int mask = 165888;
                                int collisionsFound = Physics.OverlapBoxNonAlloc(colliderData.pos, colliderData.ext, _tmp_cols, colliderData.q, mask, QueryTriggerInteraction.Collide);

                                int collisionLimit = cachedInserter.otherIsBelt ? 0 : 1;

                                if (collisionsFound > collisionLimit)
                                {
                                    PlanetPhysics physics2 = ___player.planetData.physics;
                                    for (int j = 0; j < collisionsFound; j++)
                                    {
                                        physics2.GetColliderData(_tmp_cols[j], out ColliderData colliderData2);
                                        if (colliderData2.objId != 0)
                                        {
                                            if (colliderData2.usage == EColliderUsage.Build)
                                            {
                                                positionData.condition = EBuildCondition.Collide;
                                            }
                                        }
                                    }
                                }
                            }

                            bp.condition = (EBuildCondition)positionData.condition;

                            if(bp.condition == EBuildCondition.Ok)
                            {
                                if (cachedInserter.incoming)
                                {
                                    bp.inputObjId = positionData.otherId;
                                }
                                else
                                {
                                    bp.outputObjId = positionData.otherId;
                                }
                            }

                            __instance.AddBuildPreview(bp);
                        }
                    }
                }

                SwapPositionCache();
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "CheckBuildConditions")]
        public static void CheckBuildConditions_Postfix(PlayerAction_Build __instance, ref bool __result)
        {
            var ci = cachedInserters;

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
                    bool isConnected = buildPreview.inputObjId != 0 || buildPreview.outputObjId != 0;
                    if (isInserter && (
                        buildPreview.condition == EBuildCondition.TooFar ||
                        buildPreview.condition == EBuildCondition.TooClose ||
                        buildPreview.condition == EBuildCondition.OutOfReach ||
                        // the following fix an incompatibility with AdvanceBuildDestruct where inserter are tagged ascolliding even if they are not
                        (buildPreview.condition == EBuildCondition.Collide && isConnected)
                        ))
                    {
                        buildPreview.condition = EBuildCondition.Ok;
                    }

                    if (buildPreview.condition != EBuildCondition.Ok)
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

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(PlayerAction_Build), "UpdatePreviews")]
        public static void UpdatePreviews_Postfix(PlayerAction_Build __instance, List<Renderer> ___previewRenderers)
        {
            if (CopyInserters.IsCopyAvailable() && CopyInserters.copyEnabled)
            {
                var bpCount = __instance.buildPreviews.Count;
                for (int i = 0; i < bpCount; i++)
                {
                    BuildPreview buildPreview = __instance.buildPreviews[i];

                    if (buildPreview.item.prefabDesc.isInserter &&
                        buildPreview.condition == EBuildCondition.Ok &&
                        buildPreview.outputObjId == 0 && buildPreview.inputObjId == 0)
                    {
                        var originalMaterial = ___previewRenderers[buildPreview.previewIndex].sharedMaterial;

                        // this makes the inserter preview white-ish if not connected
                        var newMaterial = UnityEngine.Object.Instantiate<Material>(Configs.builtin.previewGizmoMat_Inserter);
                        newMaterial.SetVector("_Position1", originalMaterial.GetVector("_Position1"));
                        newMaterial.SetVector("_Position2", originalMaterial.GetVector("_Position2"));
                        newMaterial.SetVector("_Rotation1", originalMaterial.GetVector("_Rotation1"));
                        newMaterial.SetVector("_Rotation2", originalMaterial.GetVector("_Rotation2"));
                        ___previewRenderers[buildPreview.previewIndex].sharedMaterial = newMaterial;
                    }

                }
            }
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "CreatePrebuilds")]
        public static void CreatePrebuilds_Prefix(PlayerAction_Build __instance)
        {
            var ci = cachedInserters;
            if (CopyInserters.copyEnabled && ci.Count > 0)
            {
                if (__instance.waitConfirm && VFInput._buildConfirm.onDown && __instance.buildPreviews.Count > 0)
                {

                    // for now we remove the inserters prebuilds. we recreate them after the 'real' buildpreviews have been transformed into prebuilds
                    for (int i = __instance.buildPreviews.Count - 1; i >= 0; i--) // Reverse loop for removing found elements
                    {
                        BuildPreview buildPreview = __instance.buildPreviews[i];
                        bool isInserter = buildPreview.desc.isInserter;

                        if (isInserter)
                        {
                            __instance.buildPreviews.RemoveAt(i);
                            __instance.FreePreviewModel(buildPreview);
                        }
                    }
                }
            }
        }

        [HarmonyReversePatch, HarmonyPatch(typeof(PlayerAction_Build), "DetermineBuildPreviews")]
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
        /// When entering Copy mode, cache info for all inserters attached to the copied building
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "SetCopyInfo")]
        public static void SetCopyInfo_Postfix(PlayerAction_Build __instance, PlanetFactory ___factory, PlanetAuxData ___planetAux, int objectId, int protoId)
        {
            cachedInserters.Clear(); // Remove previous copy info
            if (objectId < 0) // Copied item is a ghost, no inserters to cache
                return;
            var sourceEntityProto = LDB.items.Select(protoId);
            var sourceEntityId = objectId;
            var sourceEntity = ___factory.entityPool[sourceEntityId];
            var sourcePos = sourceEntity.pos;
            var sourceRot = sourceEntity.rot;

            // Set the current build rotation to the copied building rotation
            Quaternion zeroRot = Maths.SphericalRotation(sourcePos, 0f);
            float yaw = Vector3.SignedAngle(zeroRot.Forward(), sourceRot.Forward(), zeroRot.Up());
            if (sourceEntityProto.prefabDesc.minerType != EMinerType.Vein)
            {
                yaw = Mathf.Round(yaw / 90f) * 90f;
            }
            __instance.yaw = yaw;

            // Ignore building without inserter slots
            if (sourceEntityProto.prefabDesc.insertPoses.Length == 0)
                return;

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

                    if (pickTarget == sourceEntityId || insertTarget == sourceEntityId)
                    {
                        ItemProto itemProto = LDB.items.Select(inserterEntity.protoId);

                        bool incoming = insertTarget == sourceEntityId;
                        var otherId = incoming ? pickTarget : insertTarget; // The belt or other building this inserter is attached to
                        Vector3 otherPos;
                        ItemProto otherProto;

                        if (otherId > 0)
                        {
                            otherPos = entityPool[otherId].pos;
                            otherProto = LDB.items.Select((int)___factory.entityPool[otherId].protoId);
                        }
                        else
                        {
                            otherPos = ___factory.prebuildPool[-otherId].pos;
                            otherProto = LDB.items.Select((int)___factory.prebuildPool[-otherId].protoId);
                        }

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

                        bool otherIsBelt = otherProto != null && otherProto.prefabDesc.isBelt;

                        // Cache info for this inserter
                        CachedInserter ci = new CachedInserter
                        {
                            incoming = incoming,
                            protoId = inserterEntity.protoId,

                            // rotations + deltas relative to the source building's rotation
                            rot = Quaternion.Inverse(sourceRot) * inserterEntity.rot,
                            rot2 = Quaternion.Inverse(sourceRot) * inserter.rot2,
                            posDelta = Quaternion.Inverse(sourceRot) * (inserterEntity.pos - sourcePos), // Delta from copied building to inserter pos
                            pos2Delta = Quaternion.Inverse(sourceRot) * (inserter.pos2 - sourcePos), // Delta from copied building to inserter pos2

                            // store to restore inserter speed
                            refCount = Mathf.RoundToInt((float)(inserter.stt - 0.499f) / itemProto.prefabDesc.inserterSTT),

                            // not important?
                            pickOffset = inserter.pickOffset,
                            insertOffset = inserter.insertOffset,

                            // needed for pose?
                            t1 = inserter.t1,
                            t2 = inserter.t2,

                            filterId = inserter.filter,
                            snapMoves = snapMoves,
                            snapCount = snappedPointCount,

                            startSlot = -1,
                            endSlot = -1,

                            otherIsBelt = otherIsBelt
                        };


                        // compute the start and end slot that the cached inserter uses
                        CalculatePose(__instance, pickTarget, insertTarget);

                        if (__instance.posePairs.Count > 0)
                        {
                            float minDistance = 1000f;
                            for (int j = 0; j < __instance.posePairs.Count; ++j)
                            {
                                var posePair = __instance.posePairs[j];
                                float startDistance = Vector3.Distance(posePair.startPose.position, inserterEntity.pos);
                                float endDistance = Vector3.Distance(posePair.endPose.position, inserter.pos2);
                                float poseDistance = startDistance + endDistance;

                                if (poseDistance < minDistance)
                                {
                                    minDistance = poseDistance;
                                    ci.startSlot = posePair.startSlot;
                                    ci.endSlot = posePair.endSlot;
                                }
                            }
                        }

                        cachedInserters.Add(ci);
                    }
                }
            }
        }

        /// <summary>
        /// After player designates a PreBuild, check if it is an assembler that has Cached Inserters
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "AfterPrebuild")]
        public static void AfterPrebuild_Postfix(PlayerAction_Build __instance, PlanetFactory ___factory, PlanetAuxData ___planetAux, NearColliderLogic ___nearcdLogic)
        {
            // Do we have cached inserters?
            var ci = cachedInserters;
            if (CopyInserters.copyEnabled && ci.Count > 0 && !__instance.multiLevelCovering)
            {
                foreach (var cachedInserter in ci)
                {
                    var protoId = cachedInserter.protoId;
                    var modelIndex = (short)LDB.items.Select(cachedInserter.protoId).ModelIndex;

                    foreach (BuildPreview buildPreview in __instance.buildPreviews)
                    {

                        var positionData = GetPositions(__instance, ___factory, ___planetAux, ___nearcdLogic, buildPreview, cachedInserter, false);

                        if (positionData.otherId != 0)
                        {

                            // Create inserter Prebuild data
                            var pbdata = new PrebuildData
                            {
                                protoId = (short)protoId,
                                modelIndex = modelIndex,

                                insertOffset = positionData.insertOffset,
                                pickOffset = positionData.pickOffset,
                                filterId = cachedInserter.filterId,

                                refCount = cachedInserter.refCount,

                                pos = positionData.absoluteInserterPos,
                                pos2 = positionData.absoluteInserterPos2,

                                rot = positionData.absoluteInserterRot,
                                rot2 = positionData.absoluteInserterRot2
                            };


                            // Check the player has the item in inventory, no cheating here
                            var pc = CopyInserters.pc;
                            var itemcount = pc.player.package.GetItemCount(protoId);
                            // If player has none; skip this request, as we dont create prebuild ghosts, must avoid confusion
                            if (itemcount > 0)
                            {
                                var qty = 1;
                                pc.player.package.TakeTailItems(ref protoId, ref qty);
                                int pbCursor = ___factory.AddPrebuildDataWithComponents(pbdata); // Add the inserter request to Prebuild pool

                                if (cachedInserter.incoming)
                                {
                                    ___factory.WriteObjectConn(-pbCursor, 0, true, buildPreview.objId, positionData.endSlot); // assembler connection
                                    ___factory.WriteObjectConn(-pbCursor, 1, false, positionData.otherId, positionData.startSlot); // other connection
                                }
                                else
                                {
                                    ___factory.WriteObjectConn(-pbCursor, 0, false, buildPreview.objId, positionData.startSlot); // assembler connection
                                    ___factory.WriteObjectConn(-pbCursor, 1, true, positionData.otherId, positionData.endSlot); // other connection
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Clear the cached inserters when the player exits out of copy mode
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "ResetCopyInfo")]
        public static void ResetCopyInfo_Postfix()
        {
            cachedInserters.Clear();
            CopyInserters.copyEnabled = true;
        }
    }
}
