using System.Collections.Generic;
using Gamemode_Lib.Teams;
using Mirror;
using PropHunt.Patches;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PropHunt;

public class PropManager : MonoBehaviour
{
    private readonly HashSet<ulong> _originalPropRoster = new();
    private readonly HashSet<ulong> _propsThatScored = new();
    private readonly HashSet<ulong> _propsThatHaveBeenTagged = new();

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

    public bool HasPropScored(ulong prop)
    {
        return _propsThatScored.Contains(prop);
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
        // Plugin.Log.LogInfo($"[PropManager] OnAllPlayersOnOneTeam(teamId={teamData?.ID}) called");
        // if (teamData == null) return;
        // if (teamData.ID == PropHuntGamemode.HUNTER_TEAM)
        // {
        //     Plugin.Log.LogInfo(
        //         $"[PropManager] AllPlayersOnOneTeam -> Hunters. originalPropRoster={_originalPropRoster.Count} tagged={_propsThatHaveBeenTagged.Count} scored={_propsThatScored.Count}. Ending game");
        //     PropHuntGamemode.EndCurrentHole(true);
        // }
    }

    private void OnNextHole(Scene scenePrev, Scene sceneNew)
    {
        ClearPropsThatHaveScored();
    }

    public void Update()
    {
        //TODO: Okay, this is gross, find a better way
        if (_originalPropRoster.Count == 0)
        {
            SetInitialPropRoster();
        }

        if (_propsThatScored.Count == 0) return;
        if (_propsThatScored.Count + _propsThatHaveBeenTagged.Count >= _originalPropRoster.Count)
        {
            Plugin.Log.LogInfo(
                $"[PropManager] All Props accounted for (scored/tagged). originalPropRoster={_originalPropRoster.Count} tagged={_propsThatHaveBeenTagged.Count} scored={_propsThatScored.Count}. Ending hole");
            PropHuntGamemode.EndCurrentHole(false);
            ClearPropsThatHaveScored();
        }
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
        _propsThatScored.Clear();
        _originalPropRoster.Clear();
    }


    //TODO: Add an end conditions module to gamemode lib so this code can be reused
    public void ClearPropsThatHaveScored()
    {
        Plugin.Log.LogInfo(
            $"[PropManager] ClearPropsThatHaveScored() clearing {_propsThatScored.Count} scored props (tagged={_propsThatHaveBeenTagged.Count} roster={_originalPropRoster.Count})");
        _propsThatScored.Clear();
    }

    public void ClearPropsThatHaveBeenTagged()
    {
        Plugin.Log.LogInfo(
            $"[PropManager] ClearPropsThatHaveBeenTagged() clearing {_propsThatHaveBeenTagged.Count} tagged props (scored={_propsThatScored.Count} roster={_originalPropRoster.Count})");
        _propsThatHaveBeenTagged.Clear();
    }

    public void ClearOriginalPropRoster()
    {
        _originalPropRoster.Clear();
        Plugin.Log.LogInfo($"[PropManager] ClearOriginalPropRoster() cleared roster");
    }

    public void AddPropTagged(ulong prop)
    {
        if (_propsThatScored.Contains(prop))
        {
            Plugin.Log.LogInfo($"[PropManager] AddPropTagged(guid={prop}) already scored; skipping");
            return;
        }

        var added = _propsThatHaveBeenTagged.Add(prop);
        Plugin.Log.LogInfo(
            $"[PropManager] AddPropTagged(guid={prop}) added={added} tagged={_propsThatHaveBeenTagged.Count} scored={_propsThatScored.Count} roster={_originalPropRoster.Count}");
    }

    public void AddPropScored(ulong prop)
    {
        if (_propsThatHaveBeenTagged.Contains(prop))
        {
            Plugin.Log.LogInfo($"[PropManager] AddPropScored(guid={prop}) already tagged; skipping");
            return;
        }

        var added = _propsThatScored.Add(prop);
        Plugin.Log.LogInfo(
            $"[PropManager] AddPropScored(guid={prop}) added={added} scored={_propsThatScored.Count} tagged={_propsThatHaveBeenTagged.Count} roster={_originalPropRoster.Count}");
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
            $"[PropManager] Conversion complete. victimGuid={victim.PlayerId.guid} tagged={_propsThatHaveBeenTagged.Count}/{_originalPropRoster.Count} scored={_propsThatScored.Count}/{_originalPropRoster.Count}"
        );
    }

    private System.Collections.IEnumerator RestorePropPlayersBody(PlayerInfo victim)
    {
        yield return new WaitForSeconds(0.3f);
        PropHuntPlayer victimProp = victim.gameObject.GetComponent<PropHuntPlayer>();
        victimProp.SetBodyVisible(true);
    }
}
