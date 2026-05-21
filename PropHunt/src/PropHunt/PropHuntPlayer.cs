using Gamemode_Lib;
using Gamemode_Lib.Teams;
using PropHunt.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PropHunt;

public class PropHuntPlayer : MonoBehaviour
{
    private Camera? _camera;
    private GameObject _currentDisguise;
    public int numberOfDecoysPlaced { get; private set; } = 0;
    public int maxNumberOfDecoysCanPlace { get; private set; } = 3;
    public int numberOfDisguisesTaken { get; private set; } = 0;
    public int maxNumberOfDisguisesTaken { get; private set; } = 3;
    public int hunterHealth { get; private set; } = MaxHunterHealth;

    private bool isDisguised = false;
    private bool hasTriggeredHunterZeroHealth;

    public PlayerInfo playerInfo;
    public PlayerTeam playerTeam;

    private GameObject bones;
    private GameObject geometry;

    private const int MaxHunterHealth = 100;

    private void Start()
    {
        playerInfo = transform.GetComponent<PlayerInfo>();
        playerTeam = TeamManager.Instance.EnsurePlayerTeam(playerInfo);
        if (playerInfo.isLocalPlayer) _camera = Camera.main;
        ApplyPropConfiguration();

        if (ConfigurationHandler.Instance != null)
            ConfigurationHandler.Instance.ConfigChanged += OnConfigurationChanged;

        foreach (Transform child in transform)
        {
            if (child.name == "Bones") bones = child.gameObject;
            if (child.name == "Geometry") geometry = child.gameObject;
            if (bones && geometry) break;
        }

        gameObject.AddComponent<PropHuntUI>();
    }

    private void Update()
    {
        CommonUpdate();
        LocalCommonUpdate();
        if (playerTeam.teamId == PropHuntGamemode.HUNTER_TEAM)
        {
            HunterUpdate();
            LocalHunterUpdate();
        }
        else if (playerTeam.teamId == PropHuntGamemode.PROP_TEAM)
        {
            PropUpdate();
            LocalPropUpdate();
        }
    }

    public void SetBodyVisible(bool visible)
    {
        bones.SetActive(visible);
        geometry.SetActive(visible);
        if (visible)
        {
            Destroy(_currentDisguise);
        }
    }

    private void UpdatePlayerTeam()
    {
        playerTeam = TeamManager.Instance.EnsurePlayerTeam(playerInfo);
        if (playerTeam.teamId == PropHuntGamemode.PROP_TEAM && isDisguised) SetBodyVisible(false);
        if (playerTeam.teamId == PropHuntGamemode.HUNTER_TEAM) SetBodyVisible(true);
    }

    private void LocalPropUpdate()
    {
        if (!playerInfo.isLocalPlayer) return;
        if (!_camera)
        {
            Plugin.Log.LogError("[PropHunt] Cannot locate local main camera!");
            return;
        }

        if (Keyboard.current == null)
            return;

        bool disguisePressed = false;
        bool decoyPressed = false;
        if (Gamepad.current != null)
        {
            decoyPressed = Keyboard.current.fKey.wasPressedThisFrame ||
                           Gamepad.current.leftStickButton.wasPressedThisFrame;
            disguisePressed = Keyboard.current.eKey.wasPressedThisFrame ||
                              Gamepad.current.rightStickButton.wasPressedThisFrame;
        }
        else
        {
            decoyPressed = Keyboard.current.fKey.wasPressedThisFrame;
            disguisePressed = Keyboard.current.eKey.wasPressedThisFrame;
        }

        if (decoyPressed)
        {
            PlaceDecoy();
        }

        if (disguisePressed)
        {
            DisguiseSelf();
        }
    }

    private void LocalHunterUpdate()
    {
        if (!playerInfo.isLocalPlayer) return;
        bool isDead = hunterHealth <= 0;
        if (isDead & !playerInfo.AsSpectator.isSpectating)
        {
            hunterHealth = 0;
            CourseManager.SetPlayerSpectator(playerInfo.AsGolfer, isDead);
        }
        else if (!isDead & playerInfo.AsSpectator.isSpectating)
        {
            CourseManager.SetPlayerSpectator(playerInfo.AsGolfer, isDead);
        }
    }

    private void LocalCommonUpdate()
    {
        if (!playerInfo.isLocalPlayer) return;
    }

    private void PropUpdate()
    {
    }

    private void HunterUpdate()
    {
    }

    private void CommonUpdate()
    {
    }

    private void ApplyPropConfiguration()
    {
        ConfigurationHandler configuration = ConfigurationHandler.Instance;
        if (configuration == null)
            return;

        maxNumberOfDecoysCanPlace = configuration.MaxNumberOfDecoysCanPlace;
        maxNumberOfDisguisesTaken = configuration.MaxNumberOfDisguisesTaken;
    }

    private void OnConfigurationChanged()
    {
        ApplyPropConfiguration();
    }

    private void DisguiseSelf()
    {
        if (numberOfDisguisesTaken < maxNumberOfDisguisesTaken)
        {
            RaycastUtility.RequestRaycastOnHost(
                "disguise",
                GameManager.LocalPlayerId.guid,
                _camera.transform.position,
                _camera.transform.forward,
                100f
            );
        }
    }

    //TODO: Potentially do it so it copies your disguise?
    private void PlaceDecoy()
    {
        if (numberOfDecoysPlaced < maxNumberOfDecoysCanPlace)
        {
            RaycastUtility.RequestRaycastOnHost(
                "decoy",
                GameManager.LocalPlayerId.guid,
                _camera.transform.position,
                _camera.transform.forward,
                100f
            );
        }
    }

    internal void SetCurrentDisguise(GameObject disguise)
    {
        isDisguised = true;
        SetBodyVisible(false);
        _currentDisguise = disguise;
        Plugin.Log.LogInfo($"Set {playerInfo.name}'s disguise to {_currentDisguise.name}");
        numberOfDisguisesTaken++;
    }

    public void IncrementDecoyCount()
    {
        numberOfDecoysPlaced++;
    }

    public void ApplyIncorrectShotHealthPenalty()
    {
        int penalty = ConfigurationHandler.Instance != null
            ? ConfigurationHandler.Instance.IncorrectShotHealthPenalty
            : 10;

        DamageHunter(penalty);
    }

    public void DamageHunter(int damage)
    {
        if (damage <= 0)
            return;

        if (TeamManager.Instance != null)
            playerTeam = TeamManager.Instance.EnsurePlayerTeam(playerInfo);

        if (playerTeam == null || playerTeam.teamId != PropHuntGamemode.HUNTER_TEAM)
            return;

        if (hunterHealth <= 0)
            return;

        hunterHealth = Mathf.Max(0, hunterHealth - damage);
        Plugin.Log.LogInfo($"[PropHuntPlayer] Hunter health changed. player={playerInfo?.name} health={hunterHealth}");

        if (hunterHealth == 0 && !hasTriggeredHunterZeroHealth)
        {
            hasTriggeredHunterZeroHealth = true;
            OnHunterHealthDepleted();
        }
    }

    public void ResetHunterHealth()
    {
        hunterHealth = MaxHunterHealth;
        hasTriggeredHunterZeroHealth = false;
        PropManager.Instance?.RemoveHunterDied(playerInfo.PlayerId.guid);
    }

    public void OnHunterHealthDepleted()
    {
        Plugin.Log.LogInfo($"[PropHuntPlayer] Hunter health depleted. player={playerInfo?.name}");
        PropManager.Instance?.AddHunterDied(playerInfo.PlayerId.guid);
    }

    private void OnDestroy()
    {
        if (ConfigurationHandler.Instance != null)
            ConfigurationHandler.Instance.ConfigChanged -= OnConfigurationChanged;
    }
}
