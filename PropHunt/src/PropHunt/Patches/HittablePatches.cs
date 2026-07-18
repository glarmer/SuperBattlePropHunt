using Gamemode_Lib.Teams;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace PropHunt.Patches;

public class HittablePatches
{
    [HarmonyPatch(typeof(Hittable), nameof(Hittable.HitWithItem))]
    [HarmonyPostfix]
    public static void HitWithItem_Postfix(
        Hittable __instance,
        ItemType itemType,
        ItemUseId itemUseId,
        Vector3 hitLocalPosition,
        Vector3 direction,
        Vector3 localOrigin,
        float distance,
        PlayerInventory itemUser,
        bool isReflected,
        bool isInSpecialState,
        bool canHitWithNoUser)
    {
        Plugin.Log.LogInfo("HitWithItem_Postfix");

        HandleHunterShot(
            __instance,
            itemType,
            itemUser,
            isReflected
        );
    }
    
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Hittable), 
        nameof(Hittable.UserCode_CmdHitWithItem__ItemType__ItemUseId__Vector3__Vector3__Vector3__Single__PlayerInventory__Boolean__Boolean__Boolean__Double__UInt64__NetworkConnectionToClient))]
    private static void UserCode_CmdHitWithItem_Postfix(
        Hittable __instance,
        ItemType itemType,
        ItemUseId itemUseId,
        Vector3 hitLocalPosition,
        Vector3 direction,
        Vector3 localOrigin,
        float distance,
        PlayerInventory itemUser,
        bool isReflected,
        bool isInSpecialState,
        bool canHitWithNoUser,
        double hitTimestamp,
        ulong itemUseHash,
        NetworkConnectionToClient sender)
    {
        Plugin.Log.LogInfo("UserCode_CmdHitWithItem_Postfix");

        if (!NetworkServer.active)
            return;

        HandleHunterShot(__instance, itemType, itemUser, isReflected);
    }

    private static void HandleHunterShot(
        Hittable hittable,
        ItemType itemType,
        PlayerInventory itemUser,
        bool isReflected)
    {
        if (hittable == null)
            return;

        if (itemUser == null || itemUser.PlayerInfo == null)
            return;

        PlayerInfo attacker = itemUser.PlayerInfo;

        if (TeamManager.Instance.EnsurePlayerTeam(attacker).teamId != PropHuntGamemode.HUNTER_TEAM)
            return;

        if (isReflected)
            return;

        if (!(itemType == ItemType.DuelingPistol ||
              itemType == ItemType.ElephantGun ||
              itemType == ItemType.RocketLauncher ||
              itemType == ItemType.OrbitalLaser))
            return;

        PlayerInfo player = hittable.gameObject.GetComponent<PlayerInfo>();

        if (player == null)
            player = hittable.gameObject.GetComponentInParent<PlayerInfo>();

        if (player == null)
            return;

        Plugin.Log.LogInfo(
            $"[PropHunt] Server saw hunter shot. target='{player.name}', attacker='{attacker.name}'"
        );

        Events.InvokeOnShotByHunter(player, attacker);
    }
}