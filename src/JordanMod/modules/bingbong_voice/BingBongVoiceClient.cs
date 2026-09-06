using System;
using System.Security.Authentication;
using System.Threading;
using Newtonsoft.Json.Linq;
using WebSocketSharp;

namespace JordanMod.Modules.BingBongVoice;

/// One decoded stream. Swapped wholesale when the sender changes sample rate, so consumers
/// only ever hold a consistent (ring, rate) pair.
public sealed class VoiceStream
{
	public VoiceRingBuffer Ring { get; }
	public int SampleRate => Ring.SampleRate;

	public VoiceStream(int sampleRate) => Ring = new VoiceRingBuffer(sampleRate);
}

/// <summary>
/// Holds the websocket to the voice relay and turns its binary frames into samples in a ring.
///
/// Everything here runs on websocket-sharp's own receive thread. It must never touch a Unity
/// API: hop through <see cref="Plugin.RunOnMainThread"/> for that, and never per packet — at
/// 50 frames a second that queue would balloon during any frame hitch.
/// </summary>
public sealed class BingBongVoiceClient
{
	// Wire format, kept in step with the plan and the stub relay:
	//   0 u8 magic | 1 u8 version | 2 u8 codec | 3 u8 rate code | 4 u32 seq | 8 u32 sampleTimestamp
	private const int HeaderSize = 12;
	private const byte Magic = 0xB1;
	private const byte Version = 1;
	private const byte CodecPcm16 = 0;
	private static readonly int[] RateTable = [16000, 8000, 24000, 48000];

	// A gap larger than this is treated as a restart rather than something to paper over.
	private const int MaxGapSamples = 48000;

	private WebSocket? _socket;
	private string? _hello;
	private VoiceStream? _stream;
	private Thread? _reconnectThread;
	private volatile bool _shouldRun;
	private volatile bool _speakerActive;
	private volatile bool _uplinkDenied;
	private long _expectedTimestamp = -1;
	private int _badFrames;
	private volatile int _lastCloseCode;
	private int _sendFailures;

	/// The live stream, or null before the first frame arrives. Read from the audio thread.
	public VoiceStream? Stream => Volatile.Read(ref _stream);

	/// True between speaker_joined and speaker_left control frames.
	public bool SpeakerActive => _speakerActive;

	/// Set when the relay says it will not take our microphone -- uplink switched off on its side,
	/// or too many senders on this channel. Sticky until the next connect, because the answer will
	/// not change on this socket and retrying for the rest of the session would just burn bandwidth.
	public bool UplinkDenied => _uplinkDenied;

	/// Who is speaking, when the relay tells us. Null if nobody is, or if it did not say.
	public string? SpeakerName { get; private set; }

	/// Raised on the main thread when the speaker changes, so the UI can announce it.
	public event Action<string?>? OnSpeakerChanged;

	public bool IsConnected => _socket is { ReadyState: WebSocketState.Open };

	public string Url { get; private set; } = "";

	/// <param name="hello">
	/// Optional JSON control frame sent as soon as the socket opens. This is how a lobby
	/// registers itself with the relay: the channel comes from the token, and this supplies who
	/// is in it, so an admin can see the live sessions and pick one to speak into.
	/// </param>
	public void Connect(string url, string? hello = null)
	{
		_hello = hello;
		if (_shouldRun) return;
		if (string.IsNullOrWhiteSpace(url))
		{
			Debug.LogWarning("[BingBongVoice] No relay URL configured, not connecting.");
			return;
		}

		// A malformed URL will never start working, so fail here rather than letting the
		// reconnect loop retry it every minute for the rest of the session.
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) || (parsed.Scheme != "ws" && parsed.Scheme != "wss"))
		{
			Debug.LogError($"[BingBongVoice] '{url}' is not a usable WebSocket URL, not connecting.");
			return;
		}

		Url = url;
		_shouldRun = true;
		_reconnectThread = new Thread(ConnectLoop) { IsBackground = true, Name = "BingBongVoice" };
		_reconnectThread.Start();
	}

	public void Disconnect()
	{
		_shouldRun = false;
		_speakerActive = false;

		WebSocket? socket = _socket;
		_socket = null;
		if (socket == null) return;

		try { socket.Close(CloseStatusCode.Normal, "disconnecting"); }
		catch (Exception e) { Debug.LogWarning($"[BingBongVoice] Error closing socket: {e.Message}"); }
	}

	// Reconnect with exponential backoff. Runs on its own thread for the whole session.
	private void ConnectLoop()
	{
		int attempt = 0;
		System.Random jitter = new();

		while (_shouldRun)
		{
			try
			{
				OpenSocket();

				// Poll rather than block: websocket-sharp gives us no "wait until closed".
				while (_shouldRun && _socket is { ReadyState: WebSocketState.Open }) Thread.Sleep(200);

				if (!_shouldRun) return;

				// A rejected request goes straight to the ceiling rather than climbing to it.
				attempt = _lastCloseCode == 1008 ? 6 : attempt + 1;
			}
			catch (Exception e)
			{
				attempt++;
				// First failure and then every tenth, so an unreachable relay can't flood the log.
				if (attempt == 1 || attempt % 10 == 0)
					Debug.LogWarning($"[BingBongVoice] Connection attempt {attempt} failed: {e.Message}");
			}

			if (!_shouldRun) return;

			int delayMs = (int)(Math.Min(60000, 2000 * Math.Pow(2, Math.Min(attempt - 1, 5))) * (0.8 + jitter.NextDouble() * 0.4));
			for (int slept = 0; slept < delayMs && _shouldRun; slept += 100) Thread.Sleep(100);
		}
	}

	private void OpenSocket()
	{
		WebSocket socket = new(Url);

		// websocket-sharp writes its own "Fatal|WebSocket.connect|..." lines straight to the
		// console, including a run of mojibake, every single reconnect attempt. We report
		// connection state ourselves, so silence its logger entirely.
		socket.Log.Output = (_, _) => { };

		// This build of websocket-sharp defaults to a legacy protocol set, so wss:// to any
		// modern TLS terminator fails outright without this.
		if (Url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
		{
			socket.SslConfiguration.EnabledSslProtocols =
				SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls;
		}

		socket.OnMessage += OnMessage;
		socket.OnOpen += (_, _) =>
		{
			_expectedTimestamp = -1;
			_badFrames = 0;
			_uplinkDenied = false;
			Debug.Log("[BingBongVoice] Connected to relay.");

			// Register this lobby member so the relay's session directory knows who is listening.
			if (string.IsNullOrEmpty(_hello)) return;
			try { socket.Send(_hello); }
			catch (Exception ex) { Debug.LogWarning($"[BingBongVoice] Could not announce presence: {ex.Message}"); }
		};
		socket.OnClose += (_, e) =>
		{
			_speakerActive = false;
			_lastCloseCode = e.Code;

			// 1008 means the request itself is wrong -- bad token shape, uppercase channel, an
			// Origin header we should not be sending, or the relay being disabled. Retrying
			// quickly cannot fix any of those, so say so plainly and let the loop back right off.
			if (e.Code == 1008)
			{
				Debug.LogError($"[BingBongVoice] Relay rejected the connection ({e.Reason}). This will not fix itself; check the relay URL and token.");
			}
			else if (e.Code == 1002)
			{
				// The handshake got a valid HTTP response that was not an upgrade -- almost always
				// the URL pointing at the web frontend rather than the API. A single-page app
				// answers 200 with HTML for any path, so it looks alive and never works.
				Debug.LogError($"[BingBongVoice] '{Url}' answered with something that is not a WebSocket. Point it at the API rather than the website: the endpoint usually sits under /api (e.g. https://host/api -> wss://host/api/voice/ws). Clearing RelayURL derives it from BugleSoundAPIURL.");
			}
			else
			{
				Debug.Log($"[BingBongVoice] Relay closed ({e.Code} {e.Reason}).");
			}
		};
		socket.OnError += (_, e) => Debug.LogWarning($"[BingBongVoice] Socket error: {e.Message}");

		_socket = socket;
		socket.Connect();
	}

	// websocket-sharp's receive thread. No Unity APIs, no allocation beyond what we're handed.
	private void OnMessage(object? sender, MessageEventArgs e)
	{
		try
		{
			if (!e.IsBinary)
			{
				HandleControl(e.Data);
				return;
			}

			byte[] d = e.RawData;
			if (d.Length < HeaderSize) return;
			if (d[0] != Magic || d[1] != Version)
			{
				if (++_badFrames <= 3) Debug.LogWarning($"[BingBongVoice] Bad frame header (magic {d[0]:X2}, version {d[1]}).");
				return;
			}
			if (d[2] != CodecPcm16)
			{
				if (++_badFrames <= 3) Debug.LogWarning($"[BingBongVoice] Unsupported codec {d[2]}, frame dropped.");
				return;
			}

			int rate = RateTable[d[3] & 3];
			uint timestamp = (uint)(d[8] | (d[9] << 8) | (d[10] << 16) | (d[11] << 24));

			VoiceStream stream = EnsureStream(rate);

			// The sender's timestamp tells us exactly how much audio went missing, so a dropped
			// frame costs its own duration instead of silently shifting everything after it.
			if (_expectedTimestamp >= 0 && timestamp != _expectedTimestamp)
			{
				long gap = (long)timestamp - _expectedTimestamp;
				if (gap > 0 && gap <= MaxGapSamples) stream.Ring.WriteSilence((int)gap);
				// A negative or huge gap means the sender restarted; just carry on from here.
			}

			int payload = d.Length - HeaderSize;
			stream.Ring.WritePcm16(d, HeaderSize, payload);
			_expectedTimestamp = timestamp + (payload >> 1);
		}
		catch (Exception ex)
		{
			if (++_badFrames <= 3) Debug.LogError($"[BingBongVoice] Frame handling failed: {ex}");
		}
	}

	/// <summary>
	/// Sends one frame on the socket we already hold. Returns false rather than throwing when the
	/// socket is not open, so the uplink can count failures without ever driving reconnection --
	/// that belongs to ConnectLoop, and a second driver would fight it.
	/// </summary>
	public bool TrySend(byte[] frame)
	{
		WebSocket? socket = _socket;
		if (socket == null || socket.ReadyState != WebSocketState.Open) return false;
		try
		{
			socket.Send(frame);
			return true;
		}
		catch (Exception e)
		{
			if (++_sendFailures <= 3) Debug.LogWarning($"[BingBongVoice] Uplink send failed: {e.Message}");
			return false;
		}
	}

	/// Control frames for the uplink. Text, like every other control message.
	public bool TrySendControl(string json)
	{
		WebSocket? socket = _socket;
		if (socket == null || socket.ReadyState != WebSocketState.Open) return false;
		try { socket.Send(json); return true; }
		catch { return false; }
	}

	private VoiceStream EnsureStream(int rate)
	{
		VoiceStream? current = Volatile.Read(ref _stream);
		if (current != null && current.SampleRate == rate) return current;

		VoiceStream replacement = new(rate);
		Volatile.Write(ref _stream, replacement);
		_expectedTimestamp = -1;
		Debug.Log($"[BingBongVoice] Stream rate is {rate} Hz.");
		return replacement;
	}

	private void HandleControl(string json)
	{
		// Parsed leniently and fielded by substring if that fails: control messages are allowed to
		// gain fields, and a malformed payload must never take down the receive thread.
		string? speaker = null;
		JObject? parsed = null;
		try
		{
			parsed = JObject.Parse(json);
			speaker = parsed["speaker"]?.ToString();
		}
		catch
		{
			// not JSON, or no such field -- the substring checks below still work
		}

		if (json.Contains("speaker_joined"))
		{
			_speakerActive = true;
			SpeakerName = string.IsNullOrWhiteSpace(speaker) ? null : speaker;
			Debug.Log($"[BingBongVoice] A speaker joined{(SpeakerName != null ? $": {SpeakerName}" : "")}.");
			Plugin.RunOnMainThread(() => OnSpeakerChanged?.Invoke(SpeakerName));
		}
		else if (json.Contains("speaker_left") || json.Contains("preempted"))
		{
			_speakerActive = false;
			SpeakerName = null;
			_expectedTimestamp = -1;
			Debug.Log("[BingBongVoice] The speaker left.");
			Plugin.RunOnMainThread(() => OnSpeakerChanged?.Invoke(null));
		}
		else if (json.Contains("uplink_denied"))
		{
			// The relay has told us it is not taking microphones. Nothing about that changes while
			// this socket is up, so stop asking rather than sending 50 frames a second into a drop.
			_uplinkDenied = true;
			Debug.LogWarning($"[BingBongVoice] The relay will not take our microphone ({parsed?["reason"]?.ToString() ?? json}). Not sending until the next connect.");
		}
		else if (json.Contains("uplink_roster"))
		{
			// Purely informational, and the one thing that distinguishes "the relay is dropping my
			// audio" from "nobody is listening" when the uplink looks like it is working locally.
			string players = parsed?["players"] is JArray list && list.Count > 0
				? string.Join(", ", list)
				: "nobody";
			Debug.Log($"[BingBongVoice] Sending room audio: {players}.");
		}
		else if (json.Contains("rejected"))
		{
			Debug.LogWarning($"[BingBongVoice] Relay rejected this listener: {json}");
		}
	}
}
