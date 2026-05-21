using Gamemode_Lib;
using Gamemode_Lib.Events;
using Gamemode_Lib.Patches.Features;
using Gamemode_Lib.Teams;
using HarmonyLib;
using Mirror;
using PropHunt.Configuration;
using PropHunt.Patches;
using UnityEngine;
using UnityEngine.SceneManagement;
using static UnityEngine.Object;

namespace PropHunt;

public class PropHuntGamemode : IGamemode
{
    public Harmony GamemodeHarmony { get; init; }
    public string Name { get; } = "PropHunt";
    public string ModId { get; } = "com.github.glarmer.prophunt";
    public int MinPlayers { get; } = 2;
    public int MaxPlayers { get; } = 200;
    public bool IsTeamBased { get; } = true;
    public bool IsNormalStartProcedure { get; } = true;
    public int TeamCount { get; } = 2;

    public string Description { get; } =
        "Can the Props disguise themselves long enough to evade the Hunters? Only time will tell!";

    public bool IsTaggingEnabled { get; } = true;

    public static int HUNTER_TEAM = 0;
    public static int PROP_TEAM = 1;

    private static bool _cached = false;

    private static float _defaultMoveSpeed;
    private static float _walkMoveSpeed;
    private static float _wadingSpeed;
    private static float _speedBoostFactor;
    private static float _diveHorizontalSpeed;
    private static float _diveUpwardsSpeed;
    private static float _diveGetUpDuration;
    private static float _jumpUpwardsSpeed;
    private static float _swingChargingSpeed;
    private static float _swingAimingSpeed;

    public PropHuntGamemode()
    {
        GamemodeHarmony = new(((IGamemode)this).GameModeId);
    }

    public void OnGameStart()
    {
        Plugin.Log.LogInfo($"[{Name}] OnGameStart");
        StopAutoNextHole.END_GAME = false;
        GamemodeHarmony.PatchAll(typeof(HoleProgressBarUIPatches));
        GamemodeHarmony.PatchAll(typeof(StopCountdownToMatchEnd));
        GamemodeHarmony.PatchAll(typeof(StopAutoNextHole));
        GamemodeHarmony.PatchAll(typeof(HideAheadOfBallMessage));
        GamemodeHarmony.PatchAll(typeof(PlayerGolferPatches));
        GamemodeHarmony.PatchAll(typeof(InfoFeedPatches));
        GamemodeHarmony.PatchAll(typeof(TeeOffCountdownPatches));
        GamemodeHarmony.PatchAll(typeof(PlayerInventoryPatches));
        GamemodeHarmony.PatchAll(typeof(DisableLevelBounds));
        GamemodeHarmony.PatchAll(typeof(CustomTimerPatches));
        GamemodeHarmony.PatchAll(typeof(NameTagUiPatches));
        GamemodeHarmony.PatchAll(typeof(HittablePatches));
        GamemodeHarmony.PatchAll(typeof(VFXManagerPatches));

        ConfigurationHandler.Instance.SyncConfiguration();

        var holeProgressBar = GameObject.Find("Hole progress bar");
        if (holeProgressBar != null) holeProgressBar.SetActive(false);

        GameManager.Instance.gameObject.AddComponent<RaycastListener>();
        GameManager.LocalPlayerInfo.gameObject.AddComponent<PropHuntPlayer>();
        GameManager.Instance.gameObject.AddComponent<PropManager>();

        PlayerEvents.OnLocalPlayerLoaded += OnLocalPlayerRegistered;
        PlayerEvents.OnRemotePlayerLoaded += OnRemotePlayerRegistered;
        MatchEvents.OnTeeOffFinished += OnTeeOffFinished;

        if (!NetworkServer.active) return;
        PropManager.RegisterNetworkHandlers();
        GrantAllHuntersInfiniteGuns();
    }

    private void OnTeeOffFinished()
    {
        if (!NetworkServer.active) return;
        GrantAllHuntersInfiniteGuns();
    }

    private void GrantAllHuntersInfiniteGuns()
    {
        foreach (PlayerInfo player in GameManager.RemotePlayers)
        {
            GrantHunterInfiniteGun(player);
        }
        GrantHunterInfiniteGun(GameManager.LocalPlayerInfo);
    }

    private void OnLocalPlayerRegistered()
    {
        PlayerInfo player = GameManager.LocalPlayerInfo;
        Plugin.Log.LogInfo($"[{Name}] OnLocalPlayerRegistered for player {player.name}");
        GameManager.LocalPlayerInfo.gameObject.AddComponent<PropHuntPlayer>();
    }

    private void OnRemotePlayerRegistered(PlayerInfo player)
    {
        Plugin.Log.LogInfo($"[{Name}] OnRemotePlayerRegistered  for player {player.name}");
        player.gameObject.AddComponent<PropHuntPlayer>();
    }

    public static void GrantHunterInfiniteGun(PlayerInfo player)
    {
        if (!NetworkServer.active) return;
        if (TeamManager.Instance.EnsurePlayerTeam(player).teamId == HUNTER_TEAM)
        {
            PlayerInventory playerInventory = player.Inventory;
            playerInventory.ServerTryAddItem(ItemType.DuelingPistol, 1);
            CourseManager.InformPlayerPickedUpItem(playerInventory.PlayerInfo);
        }
    }

    public void OnGameEnd()
    {
        Plugin.Log.LogInfo($"[{Name}] OnGameEnd");

        if (PropManager.Instance)
            Destroy(PropManager.Instance);

        if (RaycastListener.Instance)
        {
            Destroy(RaycastListener.Instance);
        }

        PlayerEvents.OnLocalPlayerLoaded -= OnLocalPlayerRegistered;
        PlayerEvents.OnRemotePlayerLoaded -= OnRemotePlayerRegistered;
        MatchEvents.OnTeeOffFinished -= OnTeeOffFinished;
        
        GamemodeHarmony.UnpatchSelf();
    }

    public bool CanStart(int playerCount)
    {
        return true;
    }

    public static void EndCurrentHole(bool isEndGame)
    {
        if (!NetworkServer.active) return;
        if (!CourseManager.Instance) return;
        Plugin.Log.LogInfo($"[PropHunt] Ending this hole");
        StopAutoNextHole.END_GAME = isEndGame;
        if (CourseManager.Instance.matchState == MatchState.Ended) return;
        MatchState prevState = CourseManager.Instance.matchState;
        CourseManager.Instance.matchState = MatchState.Ended;
        CourseManager.Instance.OnMatchStateChanged(prevState, MatchState.Ended);
        Plugin.Log.LogInfo($"[PropHunt] Ending this hole");
    }
}
