using Gamemode_Lib.Teams;
using HarmonyLib;
using UnityEngine;

namespace SharksAndMinnows.Patches;

public class ItemSpawnerPatches
{
    /// <summary>
    /// Does nothing rn
    /// </summary>
    [HarmonyPatch(typeof(ItemSpawner), nameof(ItemSpawner.OnTriggerEnter))]
    [HarmonyPrefix]
    public static bool OnTriggerEnter_Prefix(ItemSpawner __instance, Collider other)
    {
        other.TryGetComponentInParent<PlayerTeam>(out PlayerTeam playerTeam, true);
        if (playerTeam == null) return true;

        // if (playerTeam.teamId == SharksAndMinnows.MINNOWS_TEAM)
        // {
        //     return false;
        // }
        return true;
    }
}