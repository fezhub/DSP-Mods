using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System;
using System.Collections.Generic;
using System.IO.Compression;

namespace DSP_Mods.SphereProgress
{
    public static class DysonSphereUtils
    {
        public static void Clear<T>(T[] arr)
        {
            if (arr == null)
            {
                return;
            }
            Array.Clear(arr, 0, arr.Length);
        }
        public static void ResetSphereProgress(this DysonSphere dysonSphere)
        {
            if (dysonSphere == null)
            {
                return;
            }
            // reset cell point progress
            for (int index1 = 1; index1 < dysonSphere.layersIdBased.Length; ++index1)
            {
                if (dysonSphere.layersIdBased[index1] != null && dysonSphere.layersIdBased[index1].id == index1)
                {
                    DysonShell[] shellPool = dysonSphere.layersIdBased[index1].shellPool;
                    for (int index2 = 1; index2 < dysonSphere.layersIdBased[index1].shellCursor; ++index2)
                    {
                        if (shellPool[index2] != null && shellPool[index2].id == index2)
                        {
                            DysonShell shell = shellPool[index2];
                            shell.cellPoint = 0;
                            Clear(shell.nodecps);
                            Clear(shell.vertcps);
                            shell.vertRecycleCursor = 0;
                            shell.buffer.SetData(shell.vertcps);
                        }
                    }
                }
            }
            // reset structure point progress
            for (int index1 = 1; index1 < dysonSphere.layersIdBased.Length; ++index1)
            {
                DysonSphereLayer dysonSphereLayer = dysonSphere.layersIdBased[index1];
                if (dysonSphereLayer != null && dysonSphereLayer.id == index1)
                {
                    for (int index2 = 1; index2 < dysonSphereLayer.frameCursor; ++index2)
                    {
                        DysonFrame dysonFrame = dysonSphereLayer.framePool[index2];
                        if (dysonFrame != null && dysonFrame.id == index2)
                        {
                            dysonFrame.spA = 0;
                            dysonFrame.spB = 0;
                        }
                        dysonFrame.GetSegments().Clear();
                    }
                    for (int index2 = 1; index2 < dysonSphereLayer.nodeCursor; ++index2)
                    {
                        DysonNode dysonNode = dysonSphereLayer.nodePool[index2];
                        if (dysonNode != null && dysonNode.id == index2)
                        {
                            dysonNode.sp = 0;
                            dysonNode.cpOrdered = 0;
                            dysonNode.spOrdered = 0;
                        }
                    }
                }
            }
            //sync cell buffer
            for (int index1 = 1; index1 < dysonSphere.layersIdBased.Length; ++index1)
            {
                if (dysonSphere.layersIdBased[index1] != null && dysonSphere.layersIdBased[index1].id == index1)
                {
                    DysonShell[] shellPool = dysonSphere.layersIdBased[index1].shellPool;
                    for (int index2 = 1; index2 < dysonSphere.layersIdBased[index1].shellCursor; ++index2)
                    {
                        if (shellPool[index2] != null && shellPool[index2].id == index2)
                        {
                            DysonShell shell = shellPool[index2];
                            shell.SyncCellBuffer();
                        }
                    }
                }
            }
            /*
            dysonSphere.CheckAutoNodes();
            dysonSphere.ArrangeAutoNodes();
            for(int i = 0; i < dysonSphere.autoNodes.Length; i++)
            {
                dysonSphere.PickAutoNode();
            }
            */
        }
    }

    [BepInPlugin("org.fezeral.plugins.sphereprogress", "Sphere Progress Plug-In", "1.0.0.1")]
    class SphereProgress : BaseUnityPlugin
    {
        Harmony harmony;
        internal void Awake()
        {
            harmony = new Harmony("org.fezeral.plugins.sphereprogress");
            harmony.PatchAll(typeof(PatchSphereProgress));
        }
        internal void OnDestroy()
        {
            harmony.UnpatchSelf();  // For ScriptEngine hot-reloading
            Destroy(PatchSphereProgress.cellLabel);
            Destroy(PatchSphereProgress.cellValue);
            Destroy(PatchSphereProgress.structLabel);
            Destroy(PatchSphereProgress.structValue);
        }
        public static readonly string DSP_SAVE_PATH = $"{Paths.GameRootPath}/dsp_save.txt";
        class PatchSphereProgress
        {
            public static string Export(DysonSphere dysonSphere)
            {
                System.Console.WriteLine("Auto node count: " + dysonSphere.autoNodeCount);
                foreach (var autoNode in dysonSphere.autoNodes)
                {
                    System.Console.WriteLine(autoNode);
                }
                var memoryStream = new MemoryStream();
                // var deflateStream = new GZipStream(memoryStream, CompressionMode.Compress);
                dysonSphere.Export(new System.IO.BinaryWriter(memoryStream));
                System.Console.WriteLine("Export: raw compressed bits length: " + memoryStream.Length);
                return System.Convert.ToBase64String(memoryStream.ToArray());
            }

            public static void Import(DysonSphere dysonSphere, string dysonSphereData)
            {
                byte[] data = System.Convert.FromBase64String(dysonSphereData);
                System.Console.WriteLine("Import: raw compressed bits length: " + data.Length);
                var memoryStream = new MemoryStream(data);
                // var deflateStream = new GZipStream(memoryStream, CompressionMode.Decompress);
                dysonSphere.Import(new BinaryReader(memoryStream));
            }

            public static void ImportStructure(DysonSphere dysonSphere, string dysonSphereData)
            {
                DysonSphere loadedSphere = new DysonSphere();
                loadedSphere.Init(dysonSphere.gameData, dysonSphere.starData);
                Import(loadedSphere, dysonSphereData);
                for (int i = 1; i < dysonSphere.layerCount; i++)
                {
                    dysonSphere.RemoveLayer(i);
                }
                int nodeCount = 0;
                for (int index1 = 1; index1 < loadedSphere.layersIdBased.Length; ++index1)
                {
                    if (loadedSphere.layersIdBased[index1] != null && loadedSphere.layersIdBased[index1].id == index1)
                    {
                        var layer = loadedSphere.layersIdBased[index1];
                        int[] nodeIdMap = new int[layer.nodeCapacity];
                        var newLayer = dysonSphere.AddLayer(layer.orbitRadius, layer.orbitRotation, layer.orbitAngularSpeed);
                        newLayer.gridMode = layer.gridMode;
                        for (int index2 = 1; index2 < layer.nodeCursor; ++index2)
                        {
                            DysonNode dysonNode = layer.nodePool[index2];
                            if (dysonNode == null || dysonNode.id != index2)
                            {
                                continue;
                            }
                            nodeIdMap[dysonNode.id] = newLayer.NewDysonNode(0, dysonNode.pos);
                            GameMain.gameScenario.NotifyOnPlanDysonNode();
                            nodeCount++;
                            if (nodeIdMap[dysonNode.id] <= 0)
                            {
                                System.Console.WriteLine("Failed to Generate node " + index2 + " pos: " + dysonNode.pos);
                            }
                        }
                        System.Console.WriteLine("Created " + nodeCount + " nodes");
                        for (int index2 = 1; index2 < layer.frameCursor; ++index2)
                        {
                            DysonFrame dysonFrame = layer.framePool[index2];
                            if (dysonFrame != null && dysonFrame.id == index2)
                            {
                                int node1 = nodeIdMap[dysonFrame.nodeA.id];
                                int node2 = nodeIdMap[dysonFrame.nodeB.id];
                                if (node1 <= 0 || node2 <= 0)
                                {
                                    System.Console.WriteLine("Missing node for frame " + index2 + " old node: " + dysonFrame.nodeA.id + ", " + dysonFrame.nodeB.id);
                                }
                                if (newLayer.NewDysonFrame(0, node1, node2, dysonFrame.euler) != dysonFrame.id)
                                {
                                    System.Console.WriteLine("Frame id mismatch!");
                                }
                                GameMain.gameScenario.NotifyOnPlanDysonFrame();
                            }
                        }
                        
                        
                        DysonShell[] shellPool = layer.shellPool;
                        for (int index2 = 1; index2 < layer.shellCursor; ++index2)
                        {
                            if (shellPool[index2] != null && shellPool[index2].id == index2)
                            {
                                var shell = shellPool[index2];
                                List<int> nodeIdList = new List<int>();
                                shell.nodes.ForEach(node =>
                                {
                                    if(node!= null && node.id > 0)
                                    {
                                        int id = nodeIdMap[node.id];
                                        if (id <= 0)
                                        {
                                            System.Console.WriteLine("Missing node id " + node.id);
                                        }
                                        nodeIdList.Add(id);
                                    }
                                });
                                if(newLayer.NewDysonShell(shell.protoId, nodeIdList) != shell.id)
                                {
                                    System.Console.WriteLine("Shell id mismatch!");
                                }
                                newLayer.shellPool[shell.id].GenerateGeometry();
                            }
                        }
                        
                        
                    }
                }
            }
            /*
            [HarmonyPrefix, HarmonyPatch(typeof(DysonSphere), "UpdateStates", new Type[] { typeof(DysonFrame), typeof(System.UInt32), typeof(System.Boolean), typeof(System.Boolean) })]
            public static bool DysonSphere_UpdateStates_Prefix(DysonSphere __instance, DysonFrame frame, System.UInt32 state, System.Boolean add, System.Boolean remove)
            {
                if (frame == null)
                {
                    System.Console.WriteLine("Something is not right, frame == null, state = " + state + " add = " + add + " remove = " + remove);
                } else if (__instance == null)
                {
                    System.Console.WriteLine("__instance == null");
                }
                else if (__instance.modelRenderer == null)
                {
                    System.Console.WriteLine("modelRenderer == null");
                } else if (__instance.modelRenderer.batches == null)
                {
                    System.Console.WriteLine("batches == null");
                }
                else if (__instance.modelRenderer.batches[frame.protoId] == null)
                {
                    System.Console.WriteLine("DysonSphereSegmentRenderer.protoMeshes[index] = " + DysonSphereSegmentRenderer.protoMeshes[frame.protoId]);
                    System.Console.WriteLine("DysonSphereSegmentRenderer.protoMeshes[index] = " + DysonSphereSegmentRenderer.protoMats[frame.protoId]);
                    System.Console.WriteLine("batches[protoId] == null " + frame.protoId);
                }
                else if (__instance.modelRenderer.batches[frame.protoId].segs == null)
                {
                    System.Console.WriteLine("segs == null");
                }
                return true;
            }
            */
            internal static GameObject cellValue, cellLabel, structValue, structLabel;
            [HarmonyPostfix, HarmonyPatch(typeof(UIDysonPanel), "_OnUpdate")]
            public static void UIDysonPanel_OnUpdate_Postfix(UIDysonPanel __instance)
            {
                if (Input.GetKeyDown(KeyCode.L))
                {
                    ImportStructure(__instance.viewDysonSphere, GUIUtility.systemCopyBuffer);
                    /*
                    Import(__instance.viewDysonSphere, GUIUtility.systemCopyBuffer);
                    System.Console.WriteLine("Resetting sphere progress...");
                    __instance.viewDysonSphere.ResetSphereProgress();
                    string data = Export(__instance.viewDysonSphere);
                    Import(__instance.viewDysonSphere, data);
                    */
                    UIRealtimeTip.Popup("Imported dyson sphere data from clipboard!");
                    System.Console.WriteLine("Imported dyson sphere data from clipboard!");
                }
                var dysonSphere = __instance.viewDysonSphere;
                if (Input.GetKeyDown(KeyCode.S))
                {
                    string data = Export(dysonSphere);
                    GUIUtility.systemCopyBuffer = data;
                    UIRealtimeTip.Popup("Exported dyson sphere data to clipboard!");
                    System.Console.WriteLine("Exported dyson sphere data to clipboard!");
                    using (StreamWriter outputFile = new StreamWriter(DSP_SAVE_PATH))
                    {
                        outputFile.WriteLine(data);
                    }
                }
                if (Input.GetKeyDown(KeyCode.P))
                {
                    Import(__instance.viewDysonSphere, GUIUtility.systemCopyBuffer);
                }
                if (cellValue != null)
                {
                    if (dysonSphere != null)
                    {                    
                        // Structure Progress
                        structValue.GetComponentInChildren<Text>().text = $"{dysonSphere.totalConstructedStructurePoint} / {dysonSphere.totalStructurePoint}";

                        // Cell Progress
                        int cpOrdered = 0;
                        for (int i = 1; i < dysonSphere.layersIdBased.Length; i++)
                        {
                            if (dysonSphere.layersIdBased[i] != null && dysonSphere.layersIdBased[i].id == i)
                            {
                                for (int j = 1; j < dysonSphere.layersIdBased[i].nodeCursor; j++)
                                {
                                    DysonNode dysonNode = dysonSphere.layersIdBased[i].nodePool[j];
                                    if (dysonNode != null && dysonNode.id == j)
                                    {
                                        cpOrdered += dysonNode.cpOrdered;
                                    }
                                }
                            }
                        }
                        cellValue.GetComponentInChildren<Text>().text = $"{dysonSphere.totalConstructedCellPoint}/{cpOrdered}/{dysonSphere.totalCellPoint}";
                    }
                    else
                    {
                        structValue.GetComponentInChildren<Text>().text = "-";
                        cellValue.GetComponentInChildren<Text>().text = "-";
                    }
                }
            }
            [HarmonyPostfix, HarmonyPatch(typeof(UIDysonPanel), "_OnOpen")]
            public static void UIDysonPanel_OnOpen_Postfix()
            {
                if (cellValue != null) return;

                // Get the donor objects to copy settings from
                var srcLabel = GameObject.Find("UI Root/Always on Top/Overlay Canvas - Top/Dyson Editor Top/info-group/shell/prop-label-0");
                var srcValue = GameObject.Find("UI Root/Always on Top/Overlay Canvas - Top/Dyson Editor Top/info-group/shell/prop-value-0");
                
                CreateTextObject(ref cellLabel, srcLabel, "progress-cell-label", new Vector3(8f, -231f, 0f), "Cell Progress:");
                CreateTextObject(ref cellValue, srcValue, "progress-cell-value", new Vector3(8f, -231f, 0f));
                CreateTextObject(ref structLabel, srcLabel, "progress-struct-label", new Vector3(8f, -231f-24f, 0f), "Structure Progress:");
                CreateTextObject(ref structValue, srcValue, "progress-struct-value", new Vector3(8f, -231f-24f, 0f));
            }

            private static void CreateTextObject(ref GameObject newObject, GameObject sourceObj, string objName, Vector3 lPos, string labelText = "")
            {
                newObject = Instantiate(sourceObj, sourceObj.transform.position, Quaternion.identity);
                newObject.name = objName;
                newObject.transform.SetParent(sourceObj.transform.parent);
                if (labelText != "")
                {
                    newObject.GetComponentInChildren<Text>().text = labelText;
                    Destroy(newObject.GetComponentInChildren<Localizer>()); // TODO: Translations
                }
                newObject.transform.localScale = new Vector3(1f, 1f, 1f);
                newObject.transform.localPosition = lPos;
            }
        }
    }
}
