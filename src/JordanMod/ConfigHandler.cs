using BepInEx.Configuration;
using UnityEngine;

namespace JordanMod;

public static class ConfigHandler
{
    public static ConfigFile Config { get; private set; } = null!;

    // Easy Backpack
    public static ConfigEntry<KeyCode> OpenBackpack { get; private set; } = null!;

    // Better Airport
    public static ConfigEntry<float> ConveyorSpeedModifier { get; private set; } = null!;

    // Stashed Bugle settings
    public static ConfigEntry<KeyCode> ToggleBugle { get; private set; } = null!;

    // Better Bugle settings
    public static ConfigEntry<KeyCode> SyncAudioRepository { get; private set; } = null!;
    public static ConfigEntry<float> BugleVolume { get; private set; } = null!;
    public static ConfigEntry<string> BugleSoundAPIURL { get; private set; } = null!;
    public static ConfigEntry<bool> AutoSyncAudioRepository { get; private set; } = null!;
    public static ConfigEntry<string> AudioRepositorySubdirectory { get; private set; } = null!;

    public static ConfigEntry<string> FavoriteSongsList { get; private set; } = null!;
    public static ConfigEntry<KeyCode> FavoriteSongToggleKey { get; private set; } = null!;

    public static void Initialize(ConfigFile configFile)
    {
		Config = configFile;

		// Easy Backpack settings
        OpenBackpack = Config.Bind(
            "Key Bindings",
            "OpenBackpack",
            KeyCode.B,
            new ConfigDescription("Open Easy Backpack UI")
        );

		// Better Airport settings
        ConveyorSpeedModifier = Config.Bind(
            "Conveyor",
            "ConveyorSpeedModifier",
            1.0f,
            new ConfigDescription(
                "Conveyor Speed Modifier",
                new AcceptableValueRange<float>(0.1f, 100f)
            )
        );

        // Stashed Bugle settings
        ToggleBugle = Config.Bind(
            "Control",
            "ToggleBugle",
            KeyCode.V,
            new ConfigDescription("Give / destroy Bugle")
        );
		
        // Better Bugle settings
        BugleVolume = Config.Bind(
            "Better Bugle",
            "BugleVolume",
            0.5f,
            new ConfigDescription(
                "Bugle Sound Volume",
                new AcceptableValueRange<float>(0f, 1f)
            )
        );

        BugleSoundAPIURL = Config.Bind(
            "Better Bugle",
            "BugleSoundAPIURL",
            "",
            new ConfigDescription("Bugle Sound API URL")
        );

        SyncAudioRepository = Config.Bind(
            "Better Bugle",
            "SyncAudioRepository",
            KeyCode.L,
            new ConfigDescription("Manually sync audio repository from git")
        );

        FavoriteSongsList = Config.Bind(
            "Better Bugle",
            "FavoriteSongsList",
            "",
            new ConfigDescription("Comma-separated list of favorite song names")
        );

        FavoriteSongToggleKey = Config.Bind(
            "Better Bugle",
            "FavoriteSongToggleKey",
            KeyCode.Asterisk,
            new ConfigDescription("Key to toggle favorite status of current song")
        );

	}

}