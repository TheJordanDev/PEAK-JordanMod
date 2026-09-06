using System;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Photon.Voice.Unity;

namespace JordanMod.Modules.BingBongVoice;

/// <summary>
/// Taps the local microphone for the voice uplink.
///
/// Feeds <see cref="MicUplinkSource"/>, and on the first five seconds of a session also logs a
/// one-off measurement of the capture conditions. That measurement came first and confirmed, on
/// real hardware, the assumptions the design rests on — 48000 Hz mono, 960-sample frames, 50 a
/// second, on Unity's main thread. It is kept because a mic device change or a Photon update could
/// invalidate any of them, and a wrong premise here is silent rather than loud.
///
/// What it settles:
///  - Which thread the mic callback arrives on (statically it looked like Unity's main thread via
///    VoiceConnection.Update, but LoadBalancingTransport.Service also reaches VoiceClient.Service
///    and that path could not be traced).
///  - The device's real sample rate and channel count, which drive resampling and downmixing.
///    MicWrapper is authoritative here; Recorder.SamplingRate is the *encoder* rate and can differ.
///  - The frame length, and specifically whether it exceeds 256. Photon's MicrophoneRelay.SendMic
///    copies only the first 256 samples before invoking its listeners, which is why that API — the
///    obvious one to use — cannot carry continuous audio. If frames really are longer than 256,
///    that finding is confirmed and patching Read here is the right call.
///  - Signal levels, to calibrate the noise gate. The tap sits upstream of Photon's WebRtcAudioDsp
///    (AEC/NS/AGC attach as post-processors), so this is the raw microphone.
///
/// Structured like BingBongVoicePatch: the patch body is only a try/catch around a NoInlining
/// method, with three strikes then permanent disable. That matters more here than anywhere else in
/// the mod — this sits on Photon's per-frame audio path, so an exception escaping would break all
/// voice chat, not just this feature.
/// </summary>
public class BingBongMicTapPatch
{
	private static int _strikes;
	private static bool _disabled;

	private static int _mainThreadId = -1;
	private static int _observedThreadId = -1;

	private static int _calls;
	private static int _minLength = int.MaxValue;
	private static int _maxLength;
	private static int _rate;
	private static int _channels;
	private static float _peak;
	private static double _sumAbs;
	private static long _samples;
	private static float _firstCallTime;
	private static bool _reported;
	private static bool _waitedForMic;

	/// Called from the module on the main thread, so the callback's thread can be compared to it.
	public static void NoteMainThread() => _mainThreadId = Thread.CurrentThread.ManagedThreadId;

	[HarmonyPatch(typeof(MicWrapper), nameof(MicWrapper.Read))]
	[HarmonyPostfix]
	static void ReadPostfix(MicWrapper __instance, float[] __0, bool __result)
	{
		if (_disabled || !__result) return;
		try
		{
			if (!_reported) Measure(__instance, __0);
			Feed(__instance, __0);
		}
		catch (Exception e)
		{
			Fail(e);
		}
	}

	// The only method here that touches Photon or Unity types, kept separate and un-inlined so a
	// member that changed shape throws inside the caller's try rather than while JITing the patch.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Measure(MicWrapper mic, float[] buffer)
	{
		if (buffer == null || buffer.Length == 0) return;

		if (_calls == 0)
		{
			_observedThreadId = Thread.CurrentThread.ManagedThreadId;
			_firstCallTime = UnityEngine.Time.realtimeSinceStartup;
			_rate = mic.SamplingRate;
			_channels = mic.Channels;
		}

		_calls++;
		if (buffer.Length < _minLength) _minLength = buffer.Length;
		if (buffer.Length > _maxLength) _maxLength = buffer.Length;

		// Sample sparsely: this runs on an audio-adjacent hot path and a full scan every frame
		// would be a cost we are not here to pay.
		for (int i = 0; i < buffer.Length; i += 7)
		{
			float abs = buffer[i] < 0f ? -buffer[i] : buffer[i];
			if (abs > _peak) _peak = abs;
			_sumAbs += abs;
			_samples++;
		}

		if (_calls < 250) return; // ~5 seconds at 50 frames/sec

		// Photon binds the real capture device a few seconds after joining -- until then it reports
		// "[Default] (-128)" and hands out silence. Measuring that window produces an all-zero
		// report that looks exactly like a broken microphone, so start over instead.
		if (_peak <= 0f)
		{
			_calls = 0;
			_sumAbs = 0;
			_samples = 0;
			_minLength = int.MaxValue;
			_maxLength = 0;
			if (!_waitedForMic)
			{
				_waitedForMic = true;
				Debug.Log("[BingBongVoice] Microphone is not producing yet (Photon has not bound the device); will measure again shortly.");
			}
			return;
		}

		_reported = true;
		float elapsed = UnityEngine.Time.realtimeSinceStartup - _firstCallTime;
		float mean = _samples > 0 ? (float)(_sumAbs / _samples) : 0f;
		bool onMainThread = _observedThreadId == _mainThreadId;
		bool longerThan256 = _maxLength > 256;

		Debug.Log(
			$"[BingBongVoice] MIC TAP (stage 0): {_calls} frames in {elapsed:F1}s ({_calls / Math.Max(0.01f, elapsed):F0}/s), " +
			$"rate={_rate}Hz channels={_channels}, frame length min={_minLength} max={_maxLength}, " +
			$"peak={_peak:F3} mean|s|={mean:F4}, thread={_observedThreadId} (main={_mainThreadId}, {(onMainThread ? "MAIN" : "NOT main")}).");

		Debug.Log(longerThan256
			? $"[BingBongVoice] Frames are {_maxLength} samples, longer than the 256 MicrophoneRelay forwards - confirmed, that API cannot carry audio and patching MicWrapper.Read is correct."
			: $"[BingBongVoice] Frames are only {_maxLength} samples, so MicrophoneRelay would NOT have truncated them. Revisit the capture design before building on this patch.");
	}

	// The only path that reaches the uplink. Separate and un-inlined for the same reason Measure
	// is: a Photon member that changed shape must throw inside the caller's try.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Feed(MicWrapper mic, float[] buffer)
	{
		MicUplinkSource? source = BingBongVoiceModule.Instance?.Uplink;
		source?.OnMicFrame(buffer, mic.SamplingRate, mic.Channels, mic.GetHashCode());
	}

	private static void Fail(Exception e)
	{
		Debug.LogError($"[BingBongVoice] Mic tap failed: {e}");
		if (++_strikes < 3) return;
		_disabled = true;
		Debug.LogError("[BingBongVoice] Mic tap disabled for this session after repeated failures.");
	}
}
