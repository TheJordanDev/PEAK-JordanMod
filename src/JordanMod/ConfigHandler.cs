using BepInEx.Configuration;

namespace JordanMod;

public static class ConfigHandler
{
    public static ConfigFile Config { get; private set; } = null!;

    public static void Initialize(ConfigFile configFile)
    {
		Config = configFile;
	}

}