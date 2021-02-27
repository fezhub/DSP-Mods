using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DSP_Mods.CopyInserters
{
    [BepInPlugin("org.fezeral.plugins.copyinserters", "Copy Inserters Plug-In", "1.5.0.0")]
    class CopyInserters : BaseUnityPlugin
    {
        Harmony harmony;
        public static bool copyEnabled = true;
        public static List<UIKeyTipNode> allTips;
        public static UIKeyTipNode tip;

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

        void Update()
        {
            if (Input.GetKeyUp(KeyCode.Tab) && IsCopyAvailable())
            {
                copyEnabled = !copyEnabled;
            }
        }


        internal void Awake()
        {
            harmony = new Harmony("org.fezeral.plugins.copyinserters");
            try
            {
                harmony.PatchAll(typeof(PlayerAction_Build_Patch));
                harmony.PatchAll(typeof(CopyInserters));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            PlayerAction_Build_Patch.cachedInserters = new List<PlayerAction_Build_Patch.CachedInserter>();
            PlayerAction_Build_Patch.currentPositionCache = new Queue<PlayerAction_Build_Patch.InserterPosition>();
            PlayerAction_Build_Patch.nextPositionCache = new Queue<PlayerAction_Build_Patch.InserterPosition>();
        }
        internal void OnDestroy()
        {
            harmony.UnpatchSelf();  // For ScriptEngine hot-reloading
            if(allTips!=null && tip != null)
            {
                allTips.Remove(tip);
            }

        }

        public static bool IsCopyAvailable()
        {
            return UIGame.viewMode == EViewMode.Build && pc.cmd.mode == 1 && PlayerAction_Build_Patch.cachedInserters.Count > 0;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIKeyTips), "UpdateTipDesiredState")]
        public static void UIKeyTips_UpdateTipDesiredState_Prefix(UIKeyTips __instance, ref List<UIKeyTipNode> ___allTips)
        {
            if (!tip)
            {
                allTips = ___allTips;
                tip = __instance.RegisterTip("TAB", "Toggle inserters copy");
            }
            tip.desired = IsCopyAvailable();
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(UIGeneralTips), "_OnUpdate")]
        public static void UIGeneralTips__OnUpdate_Postfix(ref Text ___modeText)
        {
            if (IsCopyAvailable() && copyEnabled)
            {
                ___modeText.text += " - Copy Inserters";
            }
        }

        public static int lastCmdMode;
        [HarmonyPrefix, HarmonyPatch(typeof(PlayerController), "UpdateCommandState")]
        public static void UpdateCommandState_Prefix(PlayerController __instance)
        {
            // Fixes https://github.com/fezhub/DSP-Mods/issues/24
            // When player cancels a build action with cached inserters, (mode 1->0)
            // Clear the inserters, so that selecting the same building from the toolbar
            // without exiting build mode does not include the inserters again
            if (PlayerAction_Build_Patch.cachedInserters.Count > 0
                && lastCmdMode == 1 && __instance.cmd.type == ECommand.Build && __instance.cmd.mode == 0)
                PlayerAction_Build_Patch.cachedInserters.Clear();

            if (__instance.cmd.mode != lastCmdMode) // store the last mode, as type->Build before mode->1
                lastCmdMode = __instance.cmd.mode;
        }
    }
}
