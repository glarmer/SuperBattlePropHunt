using HarmonyLib;
using PropHunt.Configuration;

namespace PropHunt.Patches;

public class CustomTimerPatches
{
    [HarmonyPatch(typeof(TeeOffCountdown), nameof(TeeOffCountdown.Hide))]
    [HarmonyPostfix]
    public static void Hide_Postfix(TeeOffCountdown __instance)
    {
        if (MatchSetupRules.GetValueAsBool(MatchSetupRules.Rule.MaxTimeBasedOnPar)) return;
        if (ConfigurationHandler.Instance.IsRoundTimerActivated)
        {
            CustomRoundTimer roundTimer = GameManager.Instance.gameObject.AddComponent<CustomRoundTimer>();
        }
    }
}