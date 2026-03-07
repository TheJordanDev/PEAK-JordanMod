using BepInEx.Configuration;
using UnityEngine;

namespace JordanMod;

public static class ConfigHandler
{
    public static ConfigFile Config { get; private set; } = null!;

    // Easy Backpack
    public static ConfigEntry<KeyCode> OpenBackpack { get; private set; } = null!;

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
	}

}