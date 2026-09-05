using System;
using System.Security.Cryptography;
using System.Text;
using JordanMod.Modules.BetterBugle;
using Newtonsoft.Json;
using Photon.Pun;
using UnityEngine;

namespace JordanMod.Modules.BingBongVoice;

[Module(Enabled = true)]
class BingBongVoiceModule : Module
{
	public static BingBongVoiceModule? Instance { get; private set; }

	public override string ModuleName => "BingBong Voice Module";

	public BingBongVoiceClient? Client { get; private set; }

	private GameObject? _debugObject;

	private const string ChannelSalt = "jordanmod-bingbong-voice-v1";

	public override Type[] GetPatches()
	{
		// Config is bound before modules load (Plugin.Awake: ConfigHandler.Initialize ->
		// SetupModules -> SetupPatches), so turning the feature off here means the Harmony
		// patches are never applied at all and the mod has zero footprint.
		if (!ConfigHandler.BingBongVoiceEnabled.Value) return [];
		return [typeof(BingBongVoicePatch)];
	}

	public override void Initialize()
	{
		if (Instance != null) return;
		Instance = this;

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
		return JsonConvert.SerializeObject(new { type = "hello", player = nickname });
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
		Client.OnSpeakerChanged += AnnounceSpeaker;
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

	private void Disconnect()
	{
		if (Client == null) return;
		Client.OnSpeakerChanged -= AnnounceSpeaker;
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
