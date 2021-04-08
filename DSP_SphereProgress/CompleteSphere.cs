using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DSP_Mods.SphereProgress
{
    class CompleteSphere
    {
        public static void CompleteLayer(DysonSphereLayer layer)
        {
            for (int i = 1; i < layer.nodeCursor; i++)
            {
                var node = layer.nodePool[i];
                if (node == null || node.id != i)
                {
                    continue;
                }
                int cpRequired = node.cpReqOrder;
                for (int j = 0; j < cpRequired; j++)
                {
                    node.ConstructCp();
                }
                int spRequired = node.spReqOrder;
                for (int j = 0; j < spRequired; j++)
                {
                    node.OrderConstructSp();
                }
            }
            /*
            for (int i = 1; i < layer.shellCursor; i++)
            {
                var shell = layer.shellPool[i];
                if (shell == null || shell.id != i)
                {
                    continue;
                }
                for (int j = 0; j < shell.nodecps.Length; j++)
                {
                    shell.nodecps[j] = 2;
                }
            }
            */
        }
    }
}
