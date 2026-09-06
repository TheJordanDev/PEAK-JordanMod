using System;
using System.Security.Cryptography;
using System.Text;
using JordanMod.Modules.BetterBugle;
using Newtonsoft.Json;
using Photon.Pun;
using Photon.Voice.Unity;
using UnityEngine;

namespace JordanMod.Modules.BingBongVoice;

[Module(Enabled = true)]
class BingBongVoiceModule : Module
{
	public static BingBongVoiceModule? Instance { get; private set; }

	public override string ModuleName => "BingBong Voice Module";

	public BingBongVoiceClient? Client { get; private set; }

	private GameObject? _debugObject;

	/// Captures the local microphone. Null when the uplink is off or we are not in a lobby, which
	/// is what makes the mic tap a no-op rather than something that needs its own guard.
	public MicUplinkSource? Uplink { get; private set; }

	/// Stage 1 debug: the captured mic played back through BingBong instead of being sent. Lets
	/// the whole capture path be heard with no relay and no second player.
	public VoiceStream? LoopbackStream { get; private set; }

	private MicUplinkSender? _sender;
	private bool _uplinkTransmitting;

	private const string ChannelSalt = "jordanmod-bingbong-voice-v1";

	public override Type[] GetPatches()
	{
		// Config is bound before modules load (Plugin.Awake: ConfigHandler.Initialize ->
		// SetupModules -> SetupPatches), so turning the feature off here means the Harmony
		// patches are never applied at all and the mod has zero footprint.
		if (!ConfigHandler.BingBongVoiceEnabled.Value) return [];

		// The mic tap sits on Photon's per-frame audio path, so an opted-out player should carry
		// no patch at all rather than one that early-returns.
		return ConfigHandler.BingBongVoiceUplinkEnabled.Value
			? [typeof(BingBongVoicePatch), typeof(BingBongMicTapPatch)]
			: [typeof(BingBongVoicePatch)];
	}

	public override void Initialize()
	{
		if (Instance != null) return;
		Instance = this;

		// Recorded here so the mic callback's thread can be compared against it.
		BingBongMicTapPatch.NoteMainThread();

		// The relay lives on the audio bank, so with no override configured every player derives
		// the same URL and connects on their own -- nobody has to press anything.
		PhotonNetworkEventListener listener = PhotonNetworkEventListener.Instance!;
		if (listener != null)
		{
			listener.RegisterOnJoinedRoom(OnJoinedRoom);
			listener.RegisterOnLeftRoom(Disconnect);
			listener.RegisterOnDisconnected(_ => Disconnect());
		}
		else
		{
			Debug.LogWarning("[BingBongVoice] No network event listener, auto-connect is unavailable.");
		}

		base.Initialize();
	}

	private void OnJoinedRoom()
	{
		if (!ConfigHandler.BingBongVoiceEnabled.Value) return;
		if (!ConfigHandler.BingBongVoiceAutoConnect.Value) return;
		Connect();
	}

	public override void Update()
	{
		if (!ConfigHandler.BingBongVoiceEnabled.Value) return;
		if (Input.GetKeyDown(ConfigHandler.BingBongVoiceDebugKey.Value)) ToggleStream();
		if (Input.GetKeyDown(ConfigHandler.BingBongVoiceUplinkMuteKey.Value)) ToggleUplinkMute();
		EnsureUplink();
		UpdateUplinkProximity();
	}

	// Hysteresis on the gate only. The gain is continuous so it never stutters; what stutters is
	// the open/closed decision, and each cycle costs the receiver a buffer reset.
	private bool _uplinkInRange;
	private float _uplinkStateChangedAt;
	private float _lastUplinkReport;
	private bool _uplinkMuted;

	/// Session-scoped mute for the outgoing microphone, on a key because reaching for the config is
	/// too slow to be the answer to "stop sending, now". Deliberately not persisted: a mute you
	/// forgot you set is worse than one that clears when you next launch.
	private void ToggleUplinkMute()
	{
		_uplinkMuted = !_uplinkMuted;
		Debug.Log($"[BingBongVoice] Uplink microphone {(_uplinkMuted ? "MUTED" : "unmuted")}.");
		BetterBugleUI.Instance?.ShowActionbar(_uplinkMuted
			? "🎙 BingBong microphone muted"
			: "🎙 BingBong microphone live");
	}

	/// <summary>
	/// Decides how loudly, if at all, the local microphone should be transmitted: nearest BingBong,
	/// attenuated by the same curve his own voice is heard through.
	/// </summary>
	private void UpdateUplinkProximity()
	{
		MicUplinkSource? uplink = Uplink;
		if (uplink == null) return;

		Character? local = Character.localCharacter;
		if (local == null)
		{
			uplink.SetGate(false);
			return;
		}

		float range = ConfigHandler.BingBongVoiceMaxDistance.Value;
		float nearest = float.MaxValue;
		foreach (BingBongVoiceSource source in BingBongVoiceSource.Active)
		{
			if (source == null || !source.IsPositional) continue;
			float d = Vector3.Distance(local.Head, source.transform.position);
			if (d < nearest) nearest = d;
		}

		// Open on entering the range, close only past a margin beyond it, so walking the boundary
		// cannot chatter the gate. The dwell is a minimum time in the CURRENT state before it may
		// flip again -- not a check against the change just made, which would undo it every frame.
		bool want = _uplinkInRange ? nearest <= range * 1.15f : nearest < range;
		if (want != _uplinkInRange)
		{
			float minDwell = _uplinkInRange ? 1.5f : 0.5f;
			if (Time.time - _uplinkStateChangedAt >= minDwell)
			{
				_uplinkInRange = want;
				_uplinkStateChangedAt = Time.time;
			}
		}

		float gain = 0f;
		if (_uplinkInRange && nearest < float.MaxValue)
		{
			// The curve, then a taper over the last fifth so the gate closes on real silence
			// instead of a cliff. Unity's own rolloff stops attenuating past maxDistance, which
			// copied literally would leave everyone in the level faintly transmitting forever.
			gain = BingBongVoiceSource.RangeGain(nearest, range)
			     * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(range * 0.8f, range, nearest))
			     * ConfigHandler.BingBongVoiceUplinkGain.Value;
		}

		bool audible = gain > ConfigHandler.BingBongVoiceUplinkMinGain.Value;

		// Only while someone is actually puppeting BingBong. An idle lobby sends nothing, and
		// "my mic goes out only while someone is on the other end" is a rule a player can hold
		// in their head. Loopback ignores it, having no speaker to wait for.
		bool speakerPresent = ConfigHandler.BingBongVoiceUplinkLoopback.Value || (Client?.SpeakerActive ?? false);
		bool allowed = !_uplinkMuted && !(Client?.UplinkDenied ?? false);
		bool transmit = _uplinkInRange && audible && speakerPresent && allowed;

		uplink.SetTargetGain(gain);
		uplink.SetGate(transmit);

		// The receiver's timeline freezes with ours while gated, so it needs telling to rejoin the
		// live edge rather than treating the pause as a gap to fill with silence.
		if (transmit != _uplinkTransmitting && !ConfigHandler.BingBongVoiceUplinkLoopback.Value)
		{
			_uplinkTransmitting = transmit;
			Client?.TrySendControl(transmit ? "{\"type\":\"uplink_start\"}" : "{\"type\":\"uplink_stop\"}");
			Debug.Log($"[BingBongVoice] Uplink {(transmit ? "started" : "stopped")} sending.");
		}

		if (Time.time - _lastUplinkReport < 5f) return;
		_lastUplinkReport = Time.time;
		Debug.Log($"[BingBongVoice] Uplink: nearest BingBong {(nearest < float.MaxValue ? nearest.ToString("F1") + "m" : "none")}, " +
			$"gain {gain:F2}, gate {(_uplinkInRange && audible ? "open" : "closed")}{(_uplinkMuted ? ", MUTED" : "")}, " +
			$"level {uplink.Level:F4} (floor {uplink.NoiseFloor:F5}), {uplink.FramesProduced} captured" +
			$"{(_sender != null ? $", {_sender.Sent} sent, {_sender.Dropped} dropped" : "")}. {DescribeMic(local)}");
	}

	/// <summary>
	/// Where the relay lives. The relay is served by the audio bank, so an unset override just
	/// derives the WebSocket URL from BugleSoundAPIURL; setting it points somewhere else
	/// entirely (a local stub, a separate host).
	/// </summary>
	public static string ResolveRelayUrl()
	{
		string overrideUrl = (ConfigHandler.BingBongVoiceRelayURL.Value ?? "").Trim();
		string source;
		string url;

		if (overrideUrl.Length > 0)
		{
			// An override names the endpoint. Only fill in /voice/ws when it has no path at all,
			// so "localhost:8787" works but "wss://host/custom/path" is left alone.
			source = overrideUrl;
			url = NormalizeOverride(overrideUrl);
		}
		else
		{
			// The audio bank URL names a service root, which may itself sit under a path
			// (https://audiobank.thejordan.dev/api), so the endpoint always goes on the end.
			source = (ConfigHandler.BugleSoundAPIURL.Value ?? "").Trim();
			if (source.Length == 0)
			{
				Debug.LogWarning("[BingBongVoice] Neither RelayURL nor BugleSoundAPIURL is set, nowhere to connect.");
				return "";
			}
			url = DeriveFromApiBase(source);
		}

		if (url.Length == 0)
		{
			Debug.LogError($"[BingBongVoice] Could not make a usable WebSocket URL out of '{source}'. Expected something like 'ws://localhost:8787/voice/ws' or just 'localhost:8787'.");
			return "";
		}

		// A URL that already carries a token is left exactly as given -- that is how you point at
		// a stub relay, or connect from outside a lobby.
		if (url.IndexOf("token=", StringComparison.OrdinalIgnoreCase) < 0)
		{
			string channel = ResolveChannel();
			if (channel.Length == 0)
			{
				Debug.LogWarning("[BingBongVoice] Not in a lobby, so there is no channel to join. Put a full URL including ?token= in RelayURL to connect from outside one.");
				return "";
			}
			// The relay's published contract is "listen:<channel>". It also accepts a bare channel
			// id, but following the documented form is what stays correct if it ever tightens.
			// The channel must be lowercase hex -- uppercase is rejected with a 1008.
			url += (url.IndexOf('?') >= 0 ? "&" : "?") + "token=" + Uri.EscapeDataString("listen:" + channel);
		}

		return url;
	}

	/// Announced to the relay on connect so admins can see which lobbies are live and who is in
	/// them. Deliberately carries the player name but NOT the room code: the name is what makes a
	/// session recognisable in the web app, whereas the code is what someone would need to join
	/// the game, so there is no reason to put it on the wire.
	private static string BuildHello()
	{
		string nickname = PhotonNetwork.LocalPlayer?.NickName ?? "";
		if (string.IsNullOrWhiteSpace(nickname)) nickname = "Unknown";

		// Whether we might ever send a microphone, so the relay can allocate a stream id and get the
		// roster right before the first audio frame rather than a beat after it.
		bool uplink = ConfigHandler.BingBongVoiceUplinkEnabled.Value
			&& !ConfigHandler.BingBongVoiceUplinkLoopback.Value;
		return JsonConvert.SerializeObject(new { type = "hello", player = nickname, uplink });
	}

	/// <summary>
	/// Scopes the voice channel to the Photon lobby, so each game session gets its own and
	/// players only hear whoever is speaking into theirs.
	///
	/// Hashed rather than sent as-is: the room code is what someone would need to join the game,
	/// and it would otherwise sit in the URL and in every server access log. The salt is baked
	/// into the mod so it is not a secret -- it only stops a channel id from being trivially
	/// reversible into a joinable room code. Once the relay's own join endpoint exists it should
	/// issue channel ids server-side instead, with a salt that really is private.
	/// </summary>
	public static string ResolveChannel()
	{
		string room = PhotonNetwork.CurrentRoom?.Name ?? "";
		if (string.IsNullOrWhiteSpace(room)) return "";

		using SHA256 sha = SHA256.Create();
		byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(ChannelSalt + room.Trim().ToUpperInvariant()));
		return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant().Substring(0, 32);
	}

	private static string NormalizeOverride(string raw)
	{
		string url = ToWebSocketScheme(raw);

		// Split the query/fragment off first so appending a path can't mangle them.
		int split = url.IndexOfAny(['?', '#']);
		string tail = split >= 0 ? url.Substring(split) : "";
		string head = split >= 0 ? url.Substring(0, split) : url;

		if (!IsWebSocketUrl(head, out Uri? parsed)) return "";

		// A bare host means the caller gave us the root, not the endpoint.
		if (parsed.AbsolutePath.Length <= 1) head = head.TrimEnd('/') + "/voice/ws";

		return head + tail;
	}

	private static string DeriveFromApiBase(string apiBase)
	{
		string url = ToWebSocketScheme(apiBase);

		// The base is a service root, so anything after it is not ours to keep.
		int split = url.IndexOfAny(['?', '#']);
		if (split >= 0) url = url.Substring(0, split);

		url = url.TrimEnd('/') + "/voice/ws";
		return IsWebSocketUrl(url, out _) ? url : "";
	}

	private static bool IsWebSocketUrl(string url, out Uri parsed)
	{
		parsed = null!;
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? candidate)) return false;
		if (candidate.Scheme != "ws" && candidate.Scheme != "wss") return false;
		parsed = candidate;
		return true;
	}

	private static string ToWebSocketScheme(string url)
	{
		if (url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)) return url;
		if (url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)) return url;
		if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "wss://" + url.Substring(8);
		if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return "ws://" + url.Substring(7);

		// No scheme at all: same rule as everywhere else, loopback gets the insecure variant.
		return Helper.EnsureScheme(url, "wss", "ws");
	}

	// Every BingBong attaches its own source through BingBongVoicePatch; the optional debug
	// source is the stage 1 harness, for testing with no BingBong in the scene.
	private void ToggleStream()
	{
		if (Client != null) Disconnect();
		else Connect();
	}

	private void Connect()
	{
		if (Client != null) return;

		string url = ResolveRelayUrl();
		if (url.Length == 0) return;

		Debug.Log($"[BingBongVoice] Connecting for lobby '{PhotonNetwork.CurrentRoom?.Name}' to {url}");
		Client = new BingBongVoiceClient();
		Client.Connect(url, BuildHello());

		if (!ConfigHandler.BingBongVoiceDebugSource.Value) return;
		_debugObject = new GameObject("BingBongVoiceDebug");
		UnityEngine.Object.DontDestroyOnLoad(_debugObject);
		BingBongVoiceSource.Attach(_debugObject, null);
	}

	// Reuses the Bugle's actionbar because it is the only on-screen text this mod has. Raised on
	// the main thread by the client, so touching Unity here is safe.
	private static void AnnounceSpeaker(string? speaker)
	{
		if (!ConfigHandler.BingBongVoiceAnnounceSpeaker.Value) return;
		BetterBugleUI.Instance?.ShowActionbar(speaker != null
			? $"🎙 BingBong is being puppeted by {speaker}"
			: "BingBong is himself again");
	}

	/// <summary>
	/// Photon's own view of the microphone. When our captured level is zero the question is always
	/// whether the game is feeding the device at all -- push-to-talk released, no device selected,
	/// or the recorder stopped -- rather than anything in the capture path.
	/// </summary>
	private static string DescribeMic(Character local)
	{
		try
		{
			CharacterVoiceHandler? voice = local.refs?.voice;
			if (voice == null) return "(no voice handler)";

			Recorder? recorder = voice.GetComponent<Recorder>();
			if (recorder == null) return "(no recorder)";

			string device = recorder.MicrophoneDevice != null ? recorder.MicrophoneDevice.ToString() : "none";
			return $"mic: transmitting={recorder.IsCurrentlyTransmitting}, enabled={recorder.TransmitEnabled}, device={device}";
		}
		catch (Exception e)
		{
			return $"(mic state unreadable: {e.GetType().Name})";
		}
	}

	/// <summary>
	/// Keeps the capture path in step with the config, every frame. Deliberately independent of the
	/// relay connection: loopback has to work with no network at all, and tying capture to a
	/// successful connect meant an unreachable relay silently disabled it. Also picks up a config
	/// change live, rather than only at the moment a lobby is joined.
	/// </summary>
	private void EnsureUplink()
	{
		if (!ConfigHandler.BingBongVoiceUplinkEnabled.Value)
		{
			if (Uplink != null) StopUplink();
			return;
		}

		bool wantLoopback = ConfigHandler.BingBongVoiceUplinkLoopback.Value;
		bool haveLoopback = LoopbackStream != null;
		bool senderMatchesClient = wantLoopback || (_sender != null) == (Client != null);
		if (Uplink != null && wantLoopback == haveLoopback && senderMatchesClient) return;

		StopUplink();
		StartUplink();
	}

	private void StartUplink()
	{
		if (!ConfigHandler.BingBongVoiceUplinkEnabled.Value) return;
		if (Uplink != null) return;

		MicUplinkSource source = new();

		if (ConfigHandler.BingBongVoiceUplinkLoopback.Value)
		{
			LoopbackStream = new VoiceStream(MicUplinkSource.WireRate);
			source.EmitSilenceWhenGated = true;   // keep playback continuous; see the field's note
			source.OnFrame = frame => LoopbackStream.Ring.WritePcm16Samples(frame, frame.Length);
			Debug.LogWarning("[BingBongVoice] Uplink LOOPBACK is on: your microphone plays back out of BingBong and is not sent anywhere. Wear headphones.");
		}
		else if (Client != null)
		{
			MicUplinkSender sender = new(Client);
			sender.Start();
			source.OnFrame = sender.Enqueue;
			_sender = sender;
		}

		Uplink = source;
		Debug.Log($"[BingBongVoice] Uplink capture started{(ConfigHandler.BingBongVoiceUplinkLoopback.Value ? " in LOOPBACK mode" : "")}.");
	}

	private void StopUplink()
	{
		if (_uplinkTransmitting)
		{
			Client?.TrySendControl("{\"type\":\"uplink_stop\"}");
			_uplinkTransmitting = false;
		}

		if (Uplink != null) Uplink.OnFrame = null;
		Uplink = null;
		LoopbackStream = null;

		_sender?.Stop();
		_sender = null;
	}

	private void Disconnect()
	{
		StopUplink();
		if (Client == null) return;
		Debug.Log("[BingBongVoice] Disconnecting from relay.");
		Client.Disconnect();
		Client = null;
		if (_debugObject != null) UnityEngine.Object.Destroy(_debugObject);
		_debugObject = null;
	}

	public override void Destroy()
	{
		PhotonNetworkEventListener listener = PhotonNetworkEventListener.Instance!;
		if (listener != null)
		{
			listener.JoinedRoom -= OnJoinedRoom;
			listener.LeftRoom -= Disconnect;
		}

		Client?.Disconnect();
		Client = null;
		if (_debugObject != null) UnityEngine.Object.Destroy(_debugObject);
		_debugObject = null;
		base.Destroy();
	}
}
