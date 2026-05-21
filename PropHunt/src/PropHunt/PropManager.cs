using System.Collections.Generic;
using Gamemode_Lib.Teams;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PropHunt;

public class PropManager : MonoBehaviour
{
    private readonly HashSet<ulong> _originalPropRoster = new();
    private readonly HashSet<ulong> _propsThatHaveBeenTagged = new();
    private readonly HashSet<ulong> _huntersThatHaveDied = new();
    private bool _hasEndedHole;

    public static PropManager Instance;

    public void Awake()
    {
        Plugin.Log.LogInfo("[PropManager] Awake() called");
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(Instance);
            Plugin.Log.LogInfo("[PropManager] Instance set + DontDestroyOnLoad");
        }
        else
        {
            Plugin.Log.LogInfo("[PropManager] Duplicate instance detected; destroying the old component");
            Destroy(Instance);
            Instance = this;
            return;
        }

        Gamemode_Lib.Events.SceneEvents.OnNextHole += OnNextHole;
        Gamemode_Lib.Events.SceneEvents.OnReturnToLobby += OnReturnToLobby;
        Events.OnShotByHunter += OnPlayerShot; 
        if (TeamManager.Instance != null)
        {
            TeamManager.Instance.AllPlayersOnOneTeam += OnAllPlayersOnOneTeam;
            Plugin.Log.LogInfo("[PropManager] Subscribed: TeamManager.AllPlayersOnOneTeam");
        }
        else
        {
            Plugin.Log.LogInfo(
                "[PropManager] TeamManager.Instance was null in Awake(); AllPlayersOnOneTeam not subscribed (yet?)");
        }
    }

    public int GetNumberOfOriginalProps()
    {
        return  _originalPropRoster.Count;
    }

    public int GetNumberOfCapturedProps()
    {
        return _propsThatHaveBeenTagged.Count;
    }

    public int GetNumberOfRemainingProps()
    {
        return GetNumberOfOriginalProps() - GetNumberOfCapturedProps();
    }

    public int GetNumberOfHunters()
    {
        if (TeamManager.Instance == null)
            return 0;

        var totalHunters = 0;
        foreach (var player in TeamManager.Instance.SavedTeamIdByGuid)
        {
            if (player.Value == PropHuntGamemode.HUNTER_TEAM)
            {
                totalHunters++;
            }
        }
        return totalHunters;
    }

    public HashSet<PlayerInfo> GetRemainingProps()
    {
        HashSet<PlayerInfo> props = new HashSet<PlayerInfo>();
        foreach (ulong prop in _originalPropRoster)
        {
            if (!_propsThatHaveBeenTagged.Contains(prop))
            {
                GameManager.TryFindPlayerByGuid(prop,  out PlayerInfo playerInfo);
                props.Add(playerInfo);
            }
        }
        return  props;
    }
    
    public bool HasPropBeenCaptured(ulong prop)
    {
        return _propsThatHaveBeenTagged.Contains(prop);
    }

    public int GetNumberOfDeadHunters()
    {
        return _huntersThatHaveDied.Count;
    }

    public int GetNumberOfLivingHunters()
    {
        return Mathf.Max(0, GetNumberOfHunters() - GetNumberOfDeadHunters());
    }

    public bool HasHunterDied(ulong hunter)
    {
        return _huntersThatHaveDied.Contains(hunter);
    }

    public void Start()
    {
        Plugin.Log.LogInfo("[PropManager] Start() called; initializing prop roster");
        SetInitialPropRoster();
        Plugin.Log.LogInfo($"[PropManager] Initial roster loaded. originalPropRoster={_originalPropRoster.Count}");
    }

    public void OnDestroy()
    {
        Plugin.Log.LogInfo("[PropManager] OnDestroy() called; unsubscribing events");
        Gamemode_Lib.Events.SceneEvents.OnNextHole -= OnNextHole;
        Gamemode_Lib.Events.SceneEvents.OnReturnToLobby -= OnReturnToLobby;
        Events.OnShotByHunter -= OnPlayerShot; 
        if (TeamManager.Instance != null)
        {
            TeamManager.Instance.AllPlayersOnOneTeam -= OnAllPlayersOnOneTeam;
            Plugin.Log.LogInfo("[PropManager] Unsubscribed: TeamManager.AllPlayersOnOneTeam");
        }
        else
        {
            Plugin.Log.LogInfo(
                "[PropManager] TeamManager.Instance was null in OnDestroy(); skipping AllPlayersOnOneTeam unsubscribe");
        }

        Plugin.Log.LogInfo("[PropManager] Unsubscribed: PlayerTag.PlayerTagged");
    }

    private void OnAllPlayersOnOneTeam(TeamData teamData)
    {
        Plugin.Log.LogInfo($"[PropManager] OnAllPlayersOnOneTeam(teamId={teamData?.ID}) called");
        if (teamData == null) return;
        if (teamData.ID == PropHuntGamemode.HUNTER_TEAM)
        {
            Plugin.Log.LogInfo(
                $"[PropManager] AllPlayersOnOneTeam -> Hunters. originalPropRoster={_originalPropRoster.Count} tagged={_propsThatHaveBeenTagged.Count} deadHunters={_huntersThatHaveDied.Count}. Ending game");
            EndHole(true);
        }
    }

    private void OnNextHole(Scene scenePrev, Scene sceneNew)
    {
        _hasEndedHole = false;
        ClearHuntersThatHaveDied();
    }

    public void Update()
    {
        //TODO: Okay, this is gross, find a better way
        if (_originalPropRoster.Count == 0)
        {
            SetInitialPropRoster();
        }

        TryEndHole();
    }

    private void TryEndHole()
    {
        if (_hasEndedHole)
            return;

        if (_originalPropRoster.Count > 0 && _propsThatHaveBeenTagged.Count >= _originalPropRoster.Count)
        {
            Plugin.Log.LogInfo(
                $"[PropManager] All props caught. originalPropRoster={_originalPropRoster.Count} tagged={_propsThatHaveBeenTagged.Count}. Ending hole");
            EndHole(true);
            return;
        }

        int hunterCount = GetNumberOfHunters();
        if (hunterCount > 0 && _huntersThatHaveDied.Count >= hunterCount)
        {
            Plugin.Log.LogInfo(
                $"[PropManager] All hunters died. hunters={hunterCount} deadHunters={_huntersThatHaveDied.Count}. Ending hole");
            EndHole(false);
        }
    }

    private void EndHole(bool isEndGame)
    {
        _hasEndedHole = true;
        PropHuntGamemode.EndCurrentHole(isEndGame);
    }

    public void SetInitialPropRoster()
    {
        if (TeamManager.Instance == null)
        {
            Plugin.Log.LogInfo("[PropManager] TeamManager.Instance is null; cannot build initial roster");
            return;
        }

        var totalPlayers = 0;
        var totalProps = 0;
        foreach (var player in TeamManager.Instance.SavedTeamIdByGuid)
        {
            totalPlayers++;
            if (player.Value == PropHuntGamemode.PROP_TEAM)
            {
                _originalPropRoster.Add(player.Key);
                totalProps++;
                Plugin.Log.LogInfo($"[PropManager] Added initial prop guid={player.Key}");
            }
        }
    }

    private void OnReturnToLobby(Scene scene, Scene scene1)
    {
        Plugin.Log.LogInfo("[PropManager] OnReturnToLobby() called");
        ClearAllProps();
    }

    private void ClearAllProps()
    {
        _propsThatHaveBeenTagged.Clear();
        _huntersThatHaveDied.Clear();
        _originalPropRoster.Clear();
        _hasEndedHole = false;
    }


    //TODO: Add an end conditions module to gamemode lib so this code can be reused
    public void ClearHuntersThatHaveDied()
    {
        Plugin.Log.LogInfo(
            $"[PropManager] ClearHuntersThatHaveDied() clearing {_huntersThatHaveDied.Count} dead hunters (tagged={_propsThatHaveBeenTagged.Count} roster={_originalPropRoster.Count})");
        _huntersThatHaveDied.Clear();
    }

    public void ClearPropsThatHaveBeenTagged()
    {
        Plugin.Log.LogInfo(
            $"[PropManager] ClearPropsThatHaveBeenTagged() clearing {_propsThatHaveBeenTagged.Count} tagged props (deadHunters={_huntersThatHaveDied.Count} roster={_originalPropRoster.Count})");
        _propsThatHaveBeenTagged.Clear();
    }

    public void ClearOriginalPropRoster()
    {
        _originalPropRoster.Clear();
        Plugin.Log.LogInfo($"[PropManager] ClearOriginalPropRoster() cleared roster");
    }

    public void AddPropTagged(ulong prop)
    {
        var added = _propsThatHaveBeenTagged.Add(prop);
        Plugin.Log.LogInfo(
            $"[PropManager] AddPropTagged(guid={prop}) added={added} tagged={_propsThatHaveBeenTagged.Count} deadHunters={_huntersThatHaveDied.Count} roster={_originalPropRoster.Count}");
        TryEndHole();
    }

    public void AddHunterDied(ulong hunter)
    {
        var added = _huntersThatHaveDied.Add(hunter);
        Plugin.Log.LogInfo(
            $"[PropManager] AddHunterDied(guid={hunter}) added={added} deadHunters={_huntersThatHaveDied.Count} hunters={GetNumberOfHunters()} tagged={_propsThatHaveBeenTagged.Count} roster={_originalPropRoster.Count}");
        TryEndHole();
    }

    public void RemoveHunterDied(ulong hunter)
    {
        var removed = _huntersThatHaveDied.Remove(hunter);
        Plugin.Log.LogInfo(
            $"[PropManager] RemoveHunterDied(guid={hunter}) removed={removed} deadHunters={_huntersThatHaveDied.Count} hunters={GetNumberOfHunters()}");
    }

    public void SetHunterDied(ulong hunter, bool isDead)
    {
        if (isDead)
            AddHunterDied(hunter);
        else
            RemoveHunterDied(hunter);
    }

    public static void RegisterNetworkHandlers()
    {
        HunterDeathStateMessageSerializer.Register();
        NetworkServer.RegisterHandler<HunterDeathStateMessage>(OnHunterDeathStateMessage);
    }

    private static void OnHunterDeathStateMessage(NetworkConnectionToClient sender, HunterDeathStateMessage message)
    {
        if (!NetworkServer.active)
            return;

        if (Instance == null)
            return;

        if (TeamManager.Instance == null ||
            !TeamManager.Instance.SavedTeamIdByGuid.TryGetValue(message.HunterGuid, out var teamId) ||
            teamId != PropHuntGamemode.HUNTER_TEAM)
        {
            Plugin.Log.LogInfo(
                $"[PropManager] Ignoring hunter death update for non-hunter guid={message.HunterGuid}");
            return;
        }

        Plugin.Log.LogInfo(
            $"[PropManager] Server received hunter death update. guid={message.HunterGuid} isDead={message.IsDead}");
        Instance.SetHunterDied(message.HunterGuid, message.IsDead);
    }

    private void OnPlayerShot(PlayerInfo victim, PlayerInfo hitter)
    {
        if (victim == null || hitter == null) return;

        Plugin.Log.LogInfo(
            $"[PropManager] OnPlayerTagged(victim={victim.PlayerId.guid}, hitter={hitter.PlayerId.guid}, serverActive={NetworkServer.active})"
        );

        var teamManager = TeamManager.Instance;

        if (teamManager == null)
        {
            Plugin.Log.LogInfo("[PropManager] TeamManager.Instance is null; aborting tag handling");
            return;
        }

        var victimTeam = teamManager.EnsurePlayerTeam(victim);
        var hitterTeam = teamManager.EnsurePlayerTeam(hitter);

        if (victimTeam == null || hitterTeam == null)
        {
            Plugin.Log.LogInfo(
                $"[PropManager] EnsurePlayerTeam failed. victimTeamNull={victimTeam == null} hitterTeamNull={hitterTeam == null}"
            );
            return;
        }

        bool hunterTaggedProp =
            hitterTeam.teamId == PropHuntGamemode.HUNTER_TEAM &&
            victimTeam.teamId == PropHuntGamemode.PROP_TEAM;

        if (!hunterTaggedProp)
        {
            Plugin.Log.LogInfo(
                $"[PropManager] Tag ignored. hitterTeam={hitterTeam.teamId}, victimTeam={victimTeam.teamId}"
            );
            return;
        }

        if (victim.isLocalPlayer)
        {
            Plugin.Log.LogInfo(
                $"[PropManager] Victim is local player; applying hunter movement. victimGuid={victim.PlayerId.guid}"
            );
        }
        
        StartCoroutine(RestorePropPlayersBody(victim));
        
        if (!NetworkServer.active)
            return;

        Plugin.Log.LogInfo(
            $"[PropManager] Valid tag: prop->hunter conversion. victimGuid={victim.PlayerId.guid} hitterGuid={hitter.PlayerId.guid}"
        );

        StartCoroutine(ConvertPropToHunterAfterDelay(victim, victimTeam, teamManager));
    }

    private System.Collections.IEnumerator ConvertPropToHunterAfterDelay(PlayerInfo victim, PlayerTeam victimTeam,
        TeamManager teamManager)
    {
        yield return new WaitForSeconds(0.25f);

        if (victim == null || victimTeam == null || teamManager == null)
            yield break;

        teamManager.SetTeam(
            victimTeam,
            PropHuntGamemode.HUNTER_TEAM,
            broadcastToClients: true
        );

        AddPropTagged(victim.PlayerId.guid);

        victim.Movement.TryBeginRespawn(false, RespawnTarget.TeeOrCheckpoint);

        Plugin.Log.LogInfo(
            $"[PropManager] Conversion complete. victimGuid={victim.PlayerId.guid} tagged={_propsThatHaveBeenTagged.Count}/{_originalPropRoster.Count} deadHunters={_huntersThatHaveDied.Count}/{GetNumberOfHunters()}"
        );

        PropHuntGamemode.GrantHunterInfiniteGun(victim);
    }

    private System.Collections.IEnumerator RestorePropPlayersBody(PlayerInfo victim)
    {
        yield return new WaitForSeconds(0.3f);
        PropHuntPlayer victimProp = victim.gameObject.GetComponent<PropHuntPlayer>();
        victimProp.SetBodyVisible(true);
    }
}
