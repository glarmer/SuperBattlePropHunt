using BepInEx;
using BepInEx.Logging;
using Gamemode_Lib;
using PropHunt.Configuration;

namespace PropHunt;

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    public static ConfigurationHandler ConfigurationHandler;

    private PropHuntGamemode gamemode;

    private void Awake()
    {
        Log = Logger;
        ConfigurationHandler = new ConfigurationHandler(Config);

        gamemode = new PropHuntGamemode();
        GameModeUtilities.RegisterGameMode(gamemode);

        Log.LogInfo($"Plugin {Name} is loaded!");
    }
    
}