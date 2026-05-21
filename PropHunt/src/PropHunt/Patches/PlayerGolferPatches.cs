using Gamemode_Lib.Teams;
using HarmonyLib;

namespace PropHunt.Patches;

public class PlayerGolferPatches
{
    //TODO: Cancel the player scoring

    /// <summary>
    /// Alert the prop manager to this player scoring
    /// </summary>
    [HarmonyPatch(typeof(PlayerGolfer), nameof(PlayerGolfer.InformScored))]
    [HarmonyPrefix]
    public static void InformScored_Prefix(PlayerGolfer __instance)
    {
        var manager = TeamManager.Instance;
        if (manager == null) return;

        if (PropManager.Instance == null) return;
        if (PropManager.Instance.HasPropBeenCaptured(__instance.PlayerInfo.PlayerId.guid)) return;

        var info = __instance != null ? __instance.PlayerInfo : null;
        if (info == null) return;

        var scorerTeam = manager.EnsurePlayerTeam(info);
        if (scorerTeam == null || scorerTeam.teamId != PropHuntGamemode.PROP_TEAM) return;

        PropManager.Instance.AddPropScored(info.PlayerId.guid);
    }
}