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

    // Open Passport settings
    public static ConfigEntry<KeyCode> OpenPassport { get; private set; } = null!;

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

    // BingBong Voice settings
    public static ConfigEntry<bool> BingBongVoiceEnabled { get; private set; } = null!;
    public static ConfigEntry<string> BingBongVoiceRelayURL { get; private set; } = null!;
    public static ConfigEntry<float> BingBongVoiceVolume { get; private set; } = null!;
    public static ConfigEntry<float> BingBongVoiceDuckVolume { get; private set; } = null!;
    public static ConfigEntry<float> BingBongVoiceDuckLead { get; private set; } = null!;
    public static ConfigEntry<float> BingBongVoiceJitterMs { get; private set; } = null!;
    public static ConfigEntry<float> BingBongVoiceMaxDistance { get; private set; } = null!;
    public static ConfigEntry<bool> BingBongVoiceAutoConnect { get; private set; } = null!;
    public static ConfigEntry<bool> BingBongVoiceAnnounceSpeaker { get; private set; } = null!;
    public static ConfigEntry<bool> BingBongVoiceUplinkEnabled { get; private set; } = null!;
    public static ConfigEntry<KeyCode> BingBongVoiceUplinkMuteKey { get; private set; } = null!;
    public static ConfigEntry<float> BingBongVoiceUplinkGain { get; private set; } = null!;
    public static ConfigEntry<float> BingBongVoiceUplinkMinGain { get; private set; } = null!;
    public static ConfigEntry<float> BingBongVoiceUplinkGateSensitivity { get; private set; } = null!;
    public static ConfigEntry<float> BingBongVoiceUplinkNoiseGateDb { get; private set; } = null!;
    public static ConfigEntry<bool> BingBongVoiceUplinkLoopback { get; private set; } = null!;
    public static ConfigEntry<KeyCode> BingBongVoiceDebugKey { get; private set; } = null!;
    public static ConfigEntry<bool> BingBongVoiceDebugSource { get; private set; } = null!;

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

        // Open Passport settings
        OpenPassport = Config.Bind(
            "Key Bindings",
            "OpenPassport",
            KeyCode.P,
            new ConfigDescription("Open Passport UI")
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

        // BingBong Voice settings
        BingBongVoiceEnabled = Config.Bind(
            "BingBong Voice",
            "Enabled",
            true,
            new ConfigDescription("Let BingBong speak with a live voice streamed from the relay. When off, no patches are applied at all.")
        );

        BingBongVoiceRelayURL = Config.Bind(
            "BingBong Voice",
            "RelayURL",
            "",
            new ConfigDescription("Override for the voice relay WebSocket URL. Normally leave this EMPTY: it is then derived from BugleSoundAPIURL, keeping that URL's path, so https://host/api becomes wss://host/api/voice/ws. Only set it to point at a different relay, and include the full path - aiming at the website instead of the API gives an HTML page and a 1002 close.")
        );


        BingBongVoiceVolume = Config.Bind(
            "BingBong Voice",
            "Volume",
            1.0f,
            new ConfigDescription("Live voice volume", new AcceptableValueRange<float>(0f, 1f))
        );

        BingBongVoiceDuckVolume = Config.Bind(
            "BingBong Voice",
            "DuckVolume",
            0.0f,
            new ConfigDescription("Live voice volume while one of BingBong's own voice lines is playing", new AcceptableValueRange<float>(0f, 1f))
        );

        BingBongVoiceDuckLead = Config.Bind(
            "BingBong Voice",
            "DuckLeadSeconds",
            1.0f,
            new ConfigDescription("How long before a voice line's audio actually starts to begin ducking. AskRoutine waits about a second before it plays anything.", new AcceptableValueRange<float>(0f, 2f))
        );

        BingBongVoiceJitterMs = Config.Bind(
            "BingBong Voice",
            "JitterBufferMs",
            80f,
            new ConfigDescription("Audio buffered before playback starts. Lower is more responsive, higher survives worse connections.", new AcceptableValueRange<float>(40f, 400f))
        );

        BingBongVoiceMaxDistance = Config.Bind(
            "BingBong Voice",
            "MaxDistance",
            150f,
            new ConfigDescription("How far the live voice carries, in metres. BingBong's own voice lines only reach 30m, which is short for a conversation.", new AcceptableValueRange<float>(5f, 500f))
        );

        BingBongVoiceAutoConnect = Config.Bind(
            "BingBong Voice",
            "AutoConnect",
            true,
            new ConfigDescription("Connect to the voice relay automatically on joining a lobby, and disconnect on leaving. Turn off to connect only with the keybind.")
        );

        BingBongVoiceAnnounceSpeaker = Config.Bind(
            "BingBong Voice",
            "AnnounceSpeaker",
            true,
            new ConfigDescription("Show a message when someone starts speaking through BingBong. Being puppeted silently by someone who is not even in your lobby is a bit much without it.")
        );

        BingBongVoiceUplinkEnabled = Config.Bind(
            "BingBong Voice",
            "UplinkEnabled",
            true,
            new ConfigDescription("Send your own microphone to whoever is puppeting BingBong, when you are near him and only while someone is actually connected. Turning this off means no microphone patch is applied at all.")
        );

        BingBongVoiceUplinkMuteKey = Config.Bind(
            "BingBong Voice",
            "UplinkMuteKey",
            KeyCode.F8,
            new ConfigDescription("Mutes and unmutes your outgoing microphone for the rest of the session. Faster than the config when you need it to stop now.")
        );

        BingBongVoiceUplinkGain = Config.Bind(
            "BingBong Voice",
            "UplinkGain",
            3.0f,
            new ConfigDescription("Trim on your outgoing mic. The tap sits upstream of Photon's automatic gain control, so the raw signal is quieter than what teammates hear and usually needs lifting.", new AcceptableValueRange<float>(0f, 8f))
        );

        BingBongVoiceUplinkMinGain = Config.Bind(
            "BingBong Voice",
            "UplinkMinGain",
            0.02f,
            new ConfigDescription("Stop transmitting once distance has attenuated you below this. Keeps the edge of the range quiet rather than faintly open.", new AcceptableValueRange<float>(0f, 0.5f))
        );

        BingBongVoiceUplinkGateSensitivity = Config.Bind(
            "BingBong Voice",
            "UplinkGateSensitivity",
            3.0f,
            new ConfigDescription("How far above the measured background noise your voice must be before it is transmitted. The gate learns your room rather than using a fixed level, because a quiet voice can measure lower than a noisy room. Raise it if background noise gets through, lower it if quiet speech is cut off.", new AcceptableValueRange<float>(1.5f, 10f))
        );

        BingBongVoiceUplinkNoiseGateDb = Config.Bind(
            "BingBong Voice",
            "UplinkFloorDb",
            -60f,
            new ConfigDescription("Absolute floor: nothing quieter than this is ever transmitted, whatever the adaptive gate decides. A safety net, not the main control.", new AcceptableValueRange<float>(-80f, -20f))
        );

        BingBongVoiceUplinkLoopback = Config.Bind(
            "BingBong Voice",
            "UplinkLoopback",
            false,
            new ConfigDescription("Debug: play your own processed microphone back out of BingBong instead of sending it anywhere. Lets the capture path be heard with no relay and no second player. Deliberately closes a feedback loop, so use headphones.")
        );

        BingBongVoiceDebugKey = Config.Bind(
            "BingBong Voice",
            "DebugToggleKey",
            KeyCode.F7,
            new ConfigDescription("Connect to / disconnect from the voice relay")
        );

        BingBongVoiceDebugSource = Config.Bind(
            "BingBong Voice",
            "DebugSource",
            false,
            new ConfigDescription("Also play the stream through a non-positional source, for testing without a BingBong in the scene. Plays on top of any real BingBong, so leave this off normally.")
        );

	}

}