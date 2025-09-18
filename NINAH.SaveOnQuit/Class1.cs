using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SaveOnQuit
{
    [BepInPlugin("com.vainstar.saveonquit", "Save On Quit", "2.0.0")]
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource Log;
        private Harmony _harmony;

        public override void Load()
        {
            Log = base.Log;
            _harmony = new Harmony("com.yourname.saveonquit");
            _harmony.PatchAll();
            Log.LogInfo("SaveOnQuit loaded.");
        }
    }

    internal static class SaveCache
    {
        internal const string SavePath = "Game view/Locations/HouseInterior/Actionable objects/Save";
        internal static Component Instance;
        internal static MethodInfo Lambda;

        internal static void Clear()
        {
            Instance = null;
            Lambda = null;
        }

        internal static string BuildPath(Transform t)
        {
            if (t == null) return "";
            string p = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                p = t.name + "/" + p;
            }
            return p;
        }
    }

    [HarmonyPatch]
    internal static class SaveInteractable_Start_Patch
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("_Code.Infrastructure.SaveInteractable");
            if (t == null) return null;
            return AccessTools.Method(t, "Start");
        }

        static void Postfix(object __instance)
        {
            try
            {
                var comp = __instance as Component;
                if (comp == null) return;
                string path = SaveCache.BuildPath(comp.transform);
                if (!string.Equals(path, SaveCache.SavePath, StringComparison.Ordinal)) return;
                SaveCache.Instance = comp;
                var type = comp.GetType();
                var m = AccessTools.Method(type, "_Interact_b__23_0") ?? AccessTools.Method(type, "<Interact>b__23_0");
                if (m == null)
                {
                    Plugin.Log.LogWarning("[SaveOnQuit] SaveInteractable found, but lambda not found on type: " + type.FullName);
                    return;
                }
                SaveCache.Lambda = m;
                Plugin.Log.LogInfo("[SaveOnQuit] Cached SaveInteractable at: " + path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("[SaveOnQuit] Failed to cache SaveInteractable: " + e.Message);
            }
        }
    }

    [HarmonyPatch]
    internal static class SaveInteractable_OnDestroy_Patch
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("_Code.Infrastructure.SaveInteractable");
            if (t == null) return null;
            return AccessTools.Method(t, "OnDestroy");
        }

        static void Prefix(object __instance)
        {
            try
            {
                if (SaveCache.Instance != null && ReferenceEquals(__instance, SaveCache.Instance))
                {
                    Plugin.Log.LogInfo("[SaveOnQuit] SaveInteractable destroyed — cache cleared.");
                    SaveCache.Clear();
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Button), nameof(Button.Press))]
    public static class ButtonPressPatch
    {
        static void Prefix(Button __instance)
        {
            if (__instance == null) return;
            if (__instance.name == "Button (1)" &&
                __instance.transform?.parent?.name == "Buttons" &&
                __instance.transform.parent?.parent?.name == "PopupBg" &&
                __instance.transform.parent?.parent?.parent?.name == "PopupCanvas")
            {
                TrySaveNow();
            }
        }

        private static void TrySaveNow()
        {
            try
            {
                if (SaveCache.Instance == null || SaveCache.Lambda == null)
                {
                    Plugin.Log.LogWarning("[SaveOnQuit] Save not available: SaveInteractable not cached yet (scene/path mismatch or not initialized).");
                    return;
                }
                var t = SaveCache.Instance.transform;
                string currentPath = SaveCache.BuildPath(t);
                if (!string.Equals(currentPath, SaveCache.SavePath, StringComparison.Ordinal))
                {
                    Plugin.Log.LogWarning("[SaveOnQuit] Cached SaveInteractable moved or different scene. Path now: " + currentPath);
                    return;
                }
                SaveCache.Lambda.Invoke(SaveCache.Instance, null);
                Plugin.Log.LogInfo("[SaveOnQuit] Save invoked via SaveInteractable._Interact_b__23_0().");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("[SaveOnQuit] Save invoke failed: " + e.Message);
            }
        }
    }
}
