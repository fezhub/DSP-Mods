using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DSP_Mods.SphereProgress
{
    [BepInPlugin("org.fezeral.plugins.sphereprogress", "Sphere Progress Plug-In", "1.0.0.0")]
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

        class PatchSphereProgress
        {
            internal static GameObject cellValue, cellLabel, structValue, structLabel;
            [HarmonyPostfix, HarmonyPatch(typeof(UIDysonPanel), "_OnUpdate")]
            public static void UIDysonPanel_OnUpdate_Postfix(UIDysonPanel __instance)
            {
                if (cellValue != null)
                {
                    var dysonSphere = __instance.viewDysonSphere;
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
