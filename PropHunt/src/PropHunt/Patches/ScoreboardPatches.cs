using HarmonyLib;

namespace PropHunt.Patches;

public class ScoreboardPatches
{
    [HarmonyPatch(typeof(Scoreboard), nameof(Scoreboard.Show))]
    [HarmonyPostfix]
    public static void Show_Postfix(Scoreboard __instance)
    {
        PropHuntUI.HideAllCurrentRoundUi();
    }
    
    [HarmonyPatch(typeof(Scoreboard), nameof(Scoreboard.Hide))]
    [HarmonyPostfix]
    public static void Hide_Postfix(Scoreboard __instance)
    {
        PropHuntUI.ShowAllCurrentRoundUi();
    }
    
    [HarmonyPatch(typeof(Scoreboard), nameof(Scoreboard.OnCourseManagerForceDisplayScoreboardChanged))]
    [HarmonyPostfix]
    public static void OnCourseManagerForceDisplayScoreboardChanged_Postfix(Scoreboard __instance)
    {
        if (__instance.isVisible)
        {
            PropHuntUI.HideAllCurrentRoundUi();
        }
    }
}
