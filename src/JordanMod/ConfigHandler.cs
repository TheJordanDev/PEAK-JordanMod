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
	}

}