using System;
using BepInEx.Configuration;
using Gamemode_Lib.ConfigSync;
using Mirror;
using UnityEngine;

namespace PropHunt.Configuration;

public class ConfigurationHandler
{
    public static ConfigurationHandler Instance { get; private set; }
    private ConfigFile _config;
    private BepInExConfigSync _bepInExConfigSync;

    private ConfigEntry<bool> ConfigRoundTimerActivated;
    private ConfigEntry<int> ConfigRoundTimerLengthInSeconds;
    private ConfigEntry<int> ConfigPropHeatUpdateIntervalInSeconds;
    private ConfigEntry<int> ConfigPropHeatActivationPercent;
    private ConfigEntry<int> ConfigIncorrectShotHealthPenalty;

    public event Action ConfigChanged;

    public bool IsRoundTimerActivated => ConfigRoundTimerActivated.Value;
    public int RoundTimerLengthInSeconds  => ConfigRoundTimerLengthInSeconds.Value;
    public int PropHeatUpdateIntervalInSeconds => ConfigPropHeatUpdateIntervalInSeconds.Value;
    public int PropHeatActivationPercent => ConfigPropHeatActivationPercent.Value;
    public int IncorrectShotHealthPenalty => ConfigIncorrectShotHealthPenalty.Value;

    public ConfigurationHandler(ConfigFile configFile)
    {
        Instance = this;

        _config = configFile;

        Plugin.Log.LogInfo($"ConfigurationHandler initialising {configFile}");

        BindCustomTimerConfigurations();
        BindHunterHintConfigurations();
        BindHunterHealthConfigurations();

        _bepInExConfigSync = new BepInExConfigSync(
            _config,
            "com.github.glarmer.PropHunt:PropHunt"
        );
        _bepInExConfigSync.SyncedValueChanged += (_, _) => { ConfigChanged?.Invoke(); };

        Plugin.Log.LogInfo("ConfigurationHandler initialised");
    }

    private void BindCustomTimerConfigurations()
    {
        const string Section = "Round Timer";

        ConfigRoundTimerActivated = Bind(
            Section,
            "RoundTimerEnabled",
            true,
            "Whether or not the round timer is activated",
            () => ConfigChanged?.Invoke()
        );
        
        ConfigRoundTimerLengthInSeconds = Bind(
            Section,
            "RoundTimerLength",
            820,
            "The length in seconds of the round timer",
            () => ConfigChanged?.Invoke(),
            v => Math.Clamp(v, 120, 3600)
        );
    }

    private void BindHunterHintConfigurations()
    {
        const string Section = "Minimap Hunter Hints";
        
        ConfigPropHeatUpdateIntervalInSeconds = Bind(
            Section,
            "PropHeatUpdateInterval",
            5,
            "How often, in seconds, the hunter minimap prop heat direction updates",
            () => ConfigChanged?.Invoke(),
            v => Math.Clamp(v, 1, 300)
        );

        ConfigPropHeatActivationPercent = Bind(
            Section,
            "PropHeatActivationPercent",
            60,
            "How far through the round timer, as a percentage, before hunter minimap prop heat appears",
            () => ConfigChanged?.Invoke(),
            v => Math.Clamp(v, 0, 100)
        );
    }

    private void BindHunterHealthConfigurations()
    {
        const string Section = "Hunter Health";

        ConfigIncorrectShotHealthPenalty = Bind(
            Section,
            "IncorrectShotHealthPenalty",
            10,
            "How much health a hunter loses when shooting something other than a player",
            () => ConfigChanged?.Invoke(),
            v => Math.Clamp(v, 0, 100)
        );
    }

    public void SyncConfiguration()
    {
        if (NetworkServer.active)
        {
            _bepInExConfigSync.PushHostConfigToScope();
        }
        else if (NetworkClient.active)
        {
            _bepInExConfigSync.RequestFromHost();
        }
    }

    private ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description,
        Action onChanged = null, Func<T, T> clamp = null)
    {
        var entry = _config.Bind(section, key, defaultValue, description);

        if (clamp != null)
            entry.Value = clamp(entry.Value);

        Plugin.Log.LogInfo($"{key} set to: {entry.Value}");

        if (onChanged != null)
        {
            entry.SettingChanged += (_, _) =>
            {
                if (clamp != null)
                {
                    var clamped = clamp(entry.Value);
                    if (!Equals(entry.Value, clamped))
                    {
                        entry.Value = clamped;
                        return;
                    }
                }

                onChanged();
            };
        }

        return entry;
    }
}
