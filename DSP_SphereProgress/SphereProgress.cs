using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System;
using System.Collections.Generic;
using FullSerializer;

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
        public static IEnumerable<DysonSphereLayer> GetLayers(DysonSphere sphere)
        {
            for (int i = 0; i < sphere.layersIdBased.Length; i ++)
            {
                var layer = sphere.layersIdBased[i];
                if (layer == null || layer.id != i)
                {
                    continue;
                }
                yield return layer;
            }
        }
        public static IEnumerable<DysonNode> GetNodes(DysonSphereLayer layer)
        {
            for (int i = 0; i < layer.nodeCursor; i++)
            {
                var node = layer.nodePool[i];
                if (node == null || node.id != i)
                {
                    continue;
                }
                yield return node;
            }
        }
        public static IEnumerable<DysonFrame> GetFrames(DysonSphereLayer layer)
        {
            for (int i = 0; i < layer.frameCursor; i++)
            {
                var frame = layer.framePool[i];
                if (frame == null || frame.id != i)
                {
                    continue;
                }
                yield return frame;
            }
        }
        public static IEnumerable<DysonShell> GetShells(DysonSphereLayer layer)
        {
            for (int i = 0; i < layer.shellCursor; i++)
            {
                var shell = layer.shellPool[i];
                if (shell == null || shell.id != i)
                {
                    continue;
                }
                yield return shell;
            }
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
            public static string ExportStructure(DysonSphere dysonSphere)
            {
                var structure = DysonSphereStructure.Create(dysonSphere);
                var fs = new fsSerializer();
                fs.TrySerialize(structure, out fsData data).AssertSuccessWithoutWarnings();
                string json = fsJsonPrinter.CompressedJson(data);
                System.Console.WriteLine("Layers: " + structure.layers.Count);
                foreach (var layer in structure.layers)
                {
                    System.Console.WriteLine("Node count: " + layer.nodes.Count);
                    System.Console.WriteLine("Frame count: " + layer.frames.Count);
                    System.Console.WriteLine("Shells count: " + layer.shells.Count);
                }
                System.Console.WriteLine("JSON size: " + json.Length);
                return json;
            }
            public static void RemoveAllLayers(DysonSphere dysonSphere)
            {
                foreach (var layer in DysonSphereUtils.GetLayers(dysonSphere))
                {
                    foreach(var shell in DysonSphereUtils.GetShells(layer))
                    {
                        layer.RemoveDysonShell(shell.id);
                    }
                    foreach(var frame in DysonSphereUtils.GetFrames(layer))
                    {
                        layer.RemoveDysonFrame(frame.id);
                    }
                    foreach (var node in DysonSphereUtils.GetNodes(layer))
                    {
                        layer.RemoveDysonNode(node.id);
                    }
                    dysonSphere.RemoveLayer(layer);
                }
            }
            public static void ImportStructure(DysonSphere dysonSphere, string dysonSphereData)
            {
                var deserializer = new fsSerializer();
                fsData data = fsJsonParser.Parse(dysonSphereData);
                var structure = new DysonSphereStructure();
                deserializer.TryDeserialize(data, ref structure).AssertSuccessWithoutWarnings();
                RemoveAllLayers(dysonSphere);

                int nodeCount = 0;
                foreach (var layer in structure.layers)
                {
                    System.Console.Write("Importing layer " + layer.id);
                    int maxId = 0;
                    foreach (var node in layer.nodes)
                    {
                        maxId = Math.Max(maxId, node.id);
                    }
                    int[] nodeIdMap = new int[maxId + 1];
                    var newLayer = dysonSphere.AddLayer(layer.orbitRadius, layer.orbitRotation, layer.orbitAngularSpeed);
                    newLayer.gridMode = layer.gridMode;
                    foreach (var dysonNode in layer.nodes)
                    {
                        nodeIdMap[dysonNode.id] = newLayer.NewDysonNode(0, dysonNode.pos);
                        nodeCount++;
                        if (nodeIdMap[dysonNode.id] <= 0)
                        {
                            System.Console.WriteLine("Failed to Generate node " + dysonNode.id + " pos: " + dysonNode.pos);
                        }
                    }
                    System.Console.WriteLine("Created " + nodeCount + " nodes");
                    foreach (var dysonFrame in layer.frames)
                    {
                        int node1 = nodeIdMap[dysonFrame.nodeAId];
                        int node2 = nodeIdMap[dysonFrame.nodeBId];
                        if (node1 <= 0 || node2 <= 0)
                        {
                            System.Console.WriteLine("Missing node for frame " + dysonFrame.id + " old node: " + dysonFrame.nodeAId + ", " + dysonFrame.nodeBId);
                        }
                        int frameId = newLayer.NewDysonFrame(0, node1, node2, dysonFrame.euler);
                        if (frameId != dysonFrame.id)
                        {
                            System.Console.WriteLine($"Frame id mismatch! Expected {frameId} actual {dysonFrame.id}");
                        }
                    }
                    System.Console.WriteLine("Created " + layer.frames.Count + " frames");


                    foreach (var shell in layer.shells)
                    {
                        List<int> nodeIdList = new List<int>();
                        foreach (var nodeId in shell.nodeIds)
                        {
                            int id = nodeIdMap[nodeId];
                            if (id <= 0)
                            {
                                System.Console.WriteLine("Missing node id " + nodeId);
                            }
                            nodeIdList.Add(id);
                        }
                        int shellId = newLayer.NewDysonShell(shell.protoId, nodeIdList);
                        if (shellId != shell.id)
                        {
                            System.Console.WriteLine($"Shell id mismatch! expected {shell.id} actual: {shellId}");
                        }
                    }
                    System.Console.WriteLine("Created " + layer.shells.Count + " shells");
                }
            }
            internal static GameObject cellValue, cellLabel, structValue, structLabel;
            [HarmonyPostfix, HarmonyPatch(typeof(UIDysonPanel), "_OnUpdate")]
            public static void UIDysonPanel_OnUpdate_Postfix(UIDysonPanel __instance)
            {
                if (Input.GetKeyDown(KeyCode.L))
                {
                    ImportStructure(__instance.viewDysonSphere, GUIUtility.systemCopyBuffer);
                    UIRealtimeTip.Popup("Imported dyson sphere data from clipboard!");
                    System.Console.WriteLine("Imported dyson sphere data from clipboard!");
                }
                var dysonSphere = __instance.viewDysonSphere;
                if (Input.GetKeyDown(KeyCode.S))
                {
                    string data = ExportStructure(dysonSphere);
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
                    if (dysonSphere != null)
                    {
                        for (int i = 0; i < dysonSphere.layersIdBased.Length; i ++)
                        {
                            var layer = dysonSphere.layersIdBased[i];
                            if (layer == null || layer.id != i)
                            {
                                continue;
                            }
                            System.Console.WriteLine($"Completing layer {i}");
                            CompleteSphere.CompleteLayer(layer);
                        }
                    }
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
                CreateTextObject(ref structLabel, srcLabel, "progress-struct-label", new Vector3(8f, -231f - 24f, 0f), "Structure Progress:");
                CreateTextObject(ref structValue, srcValue, "progress-struct-value", new Vector3(8f, -231f - 24f, 0f));
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
