using Gamemode_Lib.Teams;
using HarmonyLib;

namespace PropHunt.Patches;

public class PlayerInventoryPatches
{

    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.DecrementUseFromSlotAt))]
    [HarmonyPrefix]
    public static bool DecrementUseFromSlotAt_Prefix(PlayerInventory __instance, int index)
    {
        if (TeamManager.Instance.LocalPlayerTeam.teamId != PropHuntGamemode.HUNTER_TEAM) return true;
        InventorySlot effectiveSlot = __instance.GetEffectiveSlot(index);
        if (effectiveSlot.itemType.Equals(ItemType.DuelingPistol)) return false;
        return true;
    }
}