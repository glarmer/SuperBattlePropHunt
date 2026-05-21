using Gamemode_Lib.Teams;
using HarmonyLib;
using UnityEngine;

namespace PropHunt.Patches;

//TODO: This should be a module of the gamemode lib
public class InfoFeedPatches
{
    [HarmonyPatch(typeof(InfoFeed), nameof(InfoFeed.ShowKnockoutMessage))]
    [HarmonyPrefix]
    public static bool StartOrCancelMatch_Prefix(
        PlayerInfo responsiblePlayer,
        PlayerInfo knockedOutPlayer,
        KnockoutType knockoutType)
    {
        if (responsiblePlayer == null || knockedOutPlayer == null) return true;

        var hitterTeam = TeamManager.Instance.EnsurePlayerTeam(responsiblePlayer);
        var victimTeam = TeamManager.Instance.EnsurePlayerTeam(knockedOutPlayer);

        if (hitterTeam == null || victimTeam == null) return true;

        if (hitterTeam.teamId == PropHuntGamemode.HUNTER_TEAM &&
            victimTeam.teamId == PropHuntGamemode.PROP_TEAM)
        {
            return knockoutType != KnockoutType.Swing;
        }

        return true;
    }

    [HarmonyPatch(typeof(InfoFeed), nameof(InfoFeed.ColorizePlayerName))]
    [HarmonyPrefix]
    public static bool ColorizePlayerName_Prefix(
        string playerName,
        Color playerColor,
        ref string __result)
    {
        if (TryGetTeamColorForPlayerName(playerName, out var teamColor))
        {
            __result = $"<color=#{ColorUtility.ToHtmlStringRGB(teamColor)}>{playerName}</color>";
            return false;
        }

        __result = $"<color=#{ColorUtility.ToHtmlStringRGB(playerColor)}>{playerName}</color>";
        return false;
    }

    private static bool TryGetTeamColorForPlayerName(string playerName, out Color color)
    {
        color = default;

        if (string.IsNullOrEmpty(playerName)) return false;
        if (TeamManager.Instance == null) return false;

        foreach (var player in TeamManager.Instance.Players)
        {
            if (player == null) continue;
            var guid = player.playerInfo.PlayerId.Guid;
            if (CourseManager.GetPlayerName(guid) != playerName) continue;

            var team = TeamManager.Instance.EnsurePlayerTeam(player.playerInfo);
            if (team == null) return false;

            color = TeamManager.Instance.Teams[team.teamId].Color;
            return true;
        }

        return false;
    }
}