using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DSP_Mods.SphereProgress
{
    [Serializable]
    class DysonSphereStructure
    {
        public IList<DysonLayerStructure> layers { get; set; }
        public static DysonSphereStructure Create(DysonSphere obj)
        {
            var structure = new DysonSphereStructure();
            structure.layers = new List<DysonLayerStructure>();
            for (int i = 0; i < obj.layersIdBased.Length; i ++)
            {
                var layer = obj.layersIdBased[i];
                if (layer == null || layer.id != i)
                {
                    System.Console.WriteLine("layer = " + layer);
                    if (layer != null)
                    {
                        System.Console.WriteLine("layer.id = " + layer.id);
                    }
                    continue;
                }
                structure.layers.Add(DysonLayerStructure.Create(layer));
            }
            return structure;
        }

    }
    [Serializable]
    class DysonLayerStructure
    {
        public float orbitRadius { get; set; }
        public Quaternion orbitRotation { get; set; }
        public float orbitAngularSpeed { get; set; }
        public int gridMode { get; set; }

        public int id { get; set; }
        public IList<DysonNodeStructure> nodes;
        public IList<DysonFrameStructure> frames;
        public IList<DysonShellStructure> shells;

        public static DysonLayerStructure Create(DysonSphereLayer obj)
        {
            DysonLayerStructure structure = new DysonLayerStructure();
            structure.orbitRadius = obj.orbitRadius;
            structure.orbitAngularSpeed = obj.orbitAngularSpeed;
            structure.orbitRotation = obj.orbitRotation;
            structure.gridMode = obj.gridMode;
            structure.nodes = new List<DysonNodeStructure>();
            for (int i = 0; i < obj.nodeCursor; i ++)
            {
                var node = obj.nodePool[i];
                if (node == null || node.id != i)
                {
                    continue;
                }
                structure.nodes.Add(DysonNodeStructure.Create(node));
            }
            structure.frames = new List<DysonFrameStructure>();
            for (int i = 0; i < obj.frameCursor; i++)
            {
                var frame = obj.framePool[i];
                if (frame == null || frame.id != i)
                {
                    continue;
                }
                structure.frames.Add(DysonFrameStructure.Create(frame));
            }
            structure.shells = new List<DysonShellStructure>();
            for (int i = 0; i < obj.shellCursor; i++)
            {
                var shell = obj.shellPool[i];
                if (shell == null || shell.id != i)
                {
                    continue;
                }
                structure.shells.Add(DysonShellStructure.Create(shell));
            }

            structure.id = obj.id;
            return structure;
        }
    }
    [Serializable]
    class DysonNodeStructure
    {
        public Vector3 pos { get; set; }
        public int id { get; set; }
        public int protoId { get; set; }
        public static DysonNodeStructure Create(DysonNode node)
        {
            DysonNodeStructure structure = new DysonNodeStructure();
            structure.pos = node.pos;
            structure.id = node.id;
            structure.protoId = node.protoId;
            return structure;
        }
    }
    [Serializable]
    class DysonFrameStructure
    {
        public int nodeAId { get; set; }
        public int nodeBId { get; set; }
        public bool euler { get; set; }

        public int id { get; set; }
        public int protoId { get; set; }
        public static DysonFrameStructure Create(DysonFrame obj)
        {
            DysonFrameStructure structure = new DysonFrameStructure();
            structure.nodeAId = obj.nodeA.id;
            structure.nodeBId = obj.nodeB.id;
            structure.euler = obj.euler;

            structure.id = obj.id;
            structure.protoId = obj.protoId;
            return structure;
        }
    }
    [Serializable]
    class DysonShellStructure
    {
        public int id { get; set; }
        public int protoId { get; set; }

        public IList<int> nodeIds { get; set; }
        public static DysonShellStructure Create(DysonShell obj)
        {
            DysonShellStructure structure = new DysonShellStructure();

            structure.nodeIds = obj.nodes.ConvertAll(n => n.id);

            structure.id = obj.id;
            structure.protoId = obj.protoId;
            return structure;
        }
    }
}
