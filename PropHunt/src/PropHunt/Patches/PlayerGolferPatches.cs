using HarmonyLib;

namespace PropHunt.Patches;

public class PlayerGolferPatches
{
    //TODO: Cancel the player scoring

    [HarmonyPatch(typeof(PlayerGolfer), nameof(PlayerGolfer.InformScored))]
    [HarmonyPrefix]
    public static void InformScored_Prefix(PlayerGolfer __instance)
    {
    }
}
