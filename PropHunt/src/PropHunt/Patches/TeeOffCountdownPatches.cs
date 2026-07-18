using System;
using System.Collections.Generic;
using System.Linq;
using Gamemode_Lib.Teams;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace PropHunt.Patches;

public class TeeOffCountdownPatches
{
    private static int _lastHoleIndex = -1;

    [HarmonyPatch(typeof(TeeOffCountdown), nameof(TeeOffCountdown.ApplyVisibility))]
    [HarmonyPrefix]
    public static void Show_Prefix(TeeOffCountdown __instance)
    {
        if (!NetworkServer.active) return;
        if (CourseManager.MatchState != MatchState.TeeOff) return;

        Plugin.Log.LogInfo("Shark spawn server");
        if (TeamManager.Instance == null) return;
        if (CheckpointManager.Instance == null) return;
        if (CheckpointManager.Instance.allCheckpoints == null) return;
        if (CheckpointManager.Instance.allCheckpoints.Count == 0) return;
        if (!GolfHoleManager.HasMaxReferenceDistance) return;

        var sortedCheckpoints = CheckpointManager.Instance.allCheckpoints
            .Where(checkpoint => checkpoint != null)
            .Select(checkpoint =>
            {
                Vector3 checkpointPosition = checkpoint.transform.position;
                float progress = 1f - BMath.Clamp01(
                    (checkpointPosition - GolfHoleManager.MainHole.transform.position).magnitude /
                    GolfHoleManager.MaxReferenceDistance
                );

                return new
                {
                    Checkpoint = checkpoint,
                    Progress = progress
                };
            })
            .OrderByDescending(x => x.Progress)
            .ToList();

        int cutOffIndex = Math.Max(1, (int)Math.Floor(sortedCheckpoints.Count / 2f));

        Gamemode_Lib.Teams.TeamData teamData = TeamManager.Instance.Teams[PropHuntGamemode.HUNTER_TEAM];
        HashSet<PlayerTeam> sharks = teamData.Members;

        List<PlayerTeam> shuffledSharks = sharks
            .Where(shark => shark != null && shark.playerInfo != null)
            .OrderBy(shark => shark.playerInfo.PlayerId.guid)
            .ToList();

        System.Random rng = new System.Random();

        for (int i = shuffledSharks.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shuffledSharks[i], shuffledSharks[j]) =
                (shuffledSharks[j], shuffledSharks[i]);
        }

        int usableCheckpointCount = Math.Min(cutOffIndex, shuffledSharks.Count);
        usableCheckpointCount = Math.Max(1, usableCheckpointCount);

        for (int i = 0; i < shuffledSharks.Count; i++)
        {
            PlayerTeam shark = shuffledSharks[i];

            int checkpointIndex = i % usableCheckpointCount;
            Checkpoint checkpoint = sortedCheckpoints[checkpointIndex].Checkpoint;

            CheckpointManager.TryActivate(checkpoint, shark.playerInfo);

            Plugin.Log.LogInfo(
                $"Shark spawn server assigned {shark.playerInfo.PlayerId.guid} to checkpoint {checkpointIndex}");
        }

        Plugin.Log.LogInfo("Shark spawn server done");
    }

    //Todo: would be nice to find an earlier entry point
    [HarmonyPatch(typeof(TeeOffCountdown), nameof(TeeOffCountdown.ApplyVisibility))]
    [HarmonyPostfix]
    public static void Show_Postfix(TeeOffCountdown __instance)
    {
        //Todo: swap with an event or something, but for now just to make sure
        if (PropManager.Instance != null)
        {
            PropManager.Instance.SetInitialPropRoster();
        }

        if (!NetworkClient.active) return;

        int currentIndex = CourseManager.CurrentHoleCourseIndex;
        if (currentIndex == _lastHoleIndex) return;

        if (TeamManager.Instance == null) return;
        if (TeamManager.Instance.LocalPlayerTeam == null) return;
        if (GameManager.LocalPlayerInfo == null) return;

        if (TeamManager.Instance.LocalPlayerTeam.teamId == PropHuntGamemode.HUNTER_TEAM)
        {
            GameManager.LocalPlayerInfo.Movement.TryBeginRespawn(
                false,
                RespawnTarget.TeeOrCheckpoint,
                true
            );
            _lastHoleIndex = currentIndex;
            Plugin.Log.LogInfo("Shark spawn client respawned local shark");
        }
    }
}