using Gamemode_Lib;
using Gamemode_Lib.Teams;
using HarmonyLib;
using UnityEngine;

namespace PropHunt.Patches;

public class NameTagUiPatches
{
    [HarmonyPatch(typeof(NameTagUi), nameof(NameTagUi.LateUpdate))]
    [HarmonyPostfix]
    public static void LateUpdate_Postfix(NameTagUi __instance)
    {
        if (CourseManager.Instance != null && CourseManager.Instance.currentHoleCourseIndex == -1)
        {
            __instance.tag.color = Color.white;
        };

        if (!__instance.tag.enabled)
        {
            __instance.tag.enabled = true;
        }
        
        if (TeamManager.Instance == null) return;
        if (GameModeUtilities.CurrentGamemodeId != null && !GameModeUtilities.Modes[GameModeUtilities.CurrentGamemodeId].IsTeamBased) return;
        if (TeamManager.Instance.SavedTeamIdByGuid == null || TeamManager.Instance.SavedTeamIdByGuid.Count == 0) return;
        if (TeamManager.Instance.Teams == null || TeamManager.Instance.Teams.Count == 0) return;
        
        PlayerInfo playerInfo = __instance.playerInfo;
        if (playerInfo == null) return;
        if (playerInfo.isLocalPlayer) return;
        
        int teamId = TeamManager.Instance.SavedTeamIdByGuid[playerInfo.PlayerId.guid];

        if (TeamManager.Instance.LocalPlayerTeam.teamId != PropHuntGamemode.HUNTER_TEAM) return;
        if (teamId != PropHuntGamemode.PROP_TEAM) return;

        __instance.tag.enabled = false;
    }
}