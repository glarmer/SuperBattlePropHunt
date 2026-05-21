using System;

namespace PropHunt;

internal static class Events
{
    public static event Action<PlayerInfo, PlayerInfo> OnShotByHunter;

    public static void InvokeOnShotByHunter(PlayerInfo victim, PlayerInfo hitter)
    {
        OnShotByHunter?.Invoke(victim, hitter);
    }
}