using HarmonyLib;

namespace PropHunt.Patches;

public class HoleProgressBarUIPatches
{
    /// <summary>
    /// Disables the wind and progress bar UI
    /// </summary>
    [HarmonyPatch(typeof(HoleProgressBarUi), nameof(HoleProgressBarUi.Awake))]
    [HarmonyPrefix]
    public static bool Awake_Prefix(HoleProgressBarUi __instance)
    {
        __instance.transform.gameObject.SetActive(false);
        return false;
    }

    /// <summary>
    /// Disables the wind and progress bar UI
    /// </summary>
    [HarmonyPatch(typeof(HoleProgressBarUi), nameof(HoleProgressBarUi.Start))]
    [HarmonyPrefix]
    public static bool Start_Prefix(HoleProgressBarUi __instance)
    {
        __instance.transform.gameObject.SetActive(false);
        return false;
    }
}