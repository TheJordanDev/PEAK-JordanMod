using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace JordanMod.Modules.BingBongVoice;

/// <summary>
/// Plays the live voice stream through one AudioSource. One of these per BingBong (and one
/// non-positional debug instance), each with its own read cursor into the shared ring.
///
/// Output goes through <c>OnAudioFilterRead</c> rather than a procedural streaming AudioClip.
/// A clip created with <c>stream: true</c> fills Unity's own decode buffer ahead of playback --
/// that read-ahead is by design, and it cannot be satisfied from a live stream because the audio
/// simply has not arrived yet, so the ring drained to empty no matter how deep the jitter buffer
/// was made. OnAudioFilterRead is invoked exactly once per DSP block, in lockstep with playback,
/// so demand can never outrun real time. The AudioSource still needs *something* playing for the
/// filter to run, hence the silent carrier clip.
/// </summary>
public sealed class BingBongVoiceSource : MonoBehaviour
{
	private enum State { Buffering, Playing }

	private AudioSource? _source;
	private AudioClip? _carrier;
	private VoiceStream? _stream;

	private int _outputRate;
	private double _read;          // fractional position into the ring, audio thread only
	private State _state = State.Buffering;
	private float _gain;           // smoothed toward _targetGain, audio thread only

	private volatile float _targetGain;
	private volatile float _amplitude;    // post-gain, what you actually hear
	private volatile float _rawAmplitude; // pre-gain MEAN |sample|, matching BingBongMouth's units
	private volatile bool _dead;

	private int _underruns;
	private float _lastReportTime;
	private int _strikes;
	private volatile int _depthSamples;
	private volatile int _minDepth = int.MaxValue;
	private volatile int _streamRate = 1;
	private volatile int _blockFrames;     // frames per DSP block, as actually delivered
	private static bool _reportedBlock;    // once per session, not once per BingBong
	private static int _liveSources;       // how many voice sources currently exist
	private static int _playingSources;    // how many are actually enabled and audible
	private float _gainStep = 0.0008f;
	private long _lastWritePos;
	private float _lastRateTime;
	private float _appliedRange = -1f;

	private BingBongMouth? _mouth;
	private int _mouthHash;
	private float _mouthValue;
	private int _mouthStrikes;
	private bool _mouthDisabled;

	/// Set by the duck patch. Live voice is attenuated until this time.
	public float DuckUntil { get; set; }

	public float Amplitude => _amplitude;

	private const string ChildName = "BingBongVoice";

	/// <summary>
	/// Always lives on its own child GameObject. OnAudioFilterRead hooks the DSP chain of the
	/// AudioSource sharing its GameObject, and BingBong already carries the vanilla voice-line
	/// source on the item root -- putting ours alongside it meant our filter ran on *their*
	/// source, so the live stream was only audible while a canned line played and cut out
	/// whenever that source stopped. A child of our own keeps the two chains separate, and
	/// still follows the item since it is parented to it.
	/// </summary>
	public static BingBongVoiceSource Attach(GameObject target, AudioSource? template)
	{
		Transform existing = target.transform.Find(ChildName);
		if (existing != null && existing.TryGetComponent(out BingBongVoiceSource found)) return found;

		GameObject holder = new(ChildName);
		holder.transform.SetParent(target.transform, worldPositionStays: false);

		BingBongVoiceSource voice = holder.AddComponent<BingBongVoiceSource>();
		voice.Configure(template);
		return voice;
	}

	/// How many voice sources exist right now. Should be one per BingBong in the scene; if it
	/// climbs with every grab then dead instances are still playing and everything is audible at
	/// once, which reads as "no spatialisation" just as convincingly as a broken panner does.
	public static int LiveSources => _liveSources;

	/// Enabled instances. PEAK keeps several BingBong objects around (held and prop variants), so
	/// what matters is not how many exist but how many are audible at once -- more than one would
	/// sum to several times the volume and smear the apparent position.
	public static int PlayingSources => _playingSources;

	/// Lets the attach patch hand us BingBong's mouth animator. Kept separate from Attach so a
	/// change to BingBongMouth can only break the animation, never the audio.
	public void BindMouth(BingBongMouth mouth)
	{
		_mouth = mouth;
		string param = string.IsNullOrEmpty(mouth.animValue) ? "Mouth Blend" : mouth.animValue;
		_mouthHash = Animator.StringToHash(param);
		Debug.Log($"[BingBongVoice] Mouth bound: parameter '{param}', maxMouthOpen={mouth.maxMouthOpen}.");
	}

	public static bool IsAttached(GameObject target) => Find(target) != null;

	public static BingBongVoiceSource? Find(GameObject target)
	{
		Transform existing = target.transform.Find(ChildName);
		return existing != null && existing.TryGetComponent(out BingBongVoiceSource found) ? found : null;
	}

	private void Configure(AudioSource? template)
	{
		_outputRate = AudioSettings.outputSampleRate;
		_gainStep = 1f / (_outputRate * 0.015f); // ~15ms attack whatever the device runs at

		// Carrier of constant 1.0, not silence, and the filter MULTIPLIES rather than overwrites.
		// Unity applies 3D panning and distance attenuation as a per-channel gain; if that runs
		// before custom filters then the buffer handed to us already carries that gain, and
		// overwriting it threw the spatialisation away (the voice was audible at full volume from
		// anywhere). Multiplying a unit carrier reapplies it. If the panner instead runs after
		// the filter, the incoming value is simply 1.0 and multiplying changes nothing -- so this
		// is correct either way round, without having to determine which order Unity uses.
		_carrier = AudioClip.Create($"BingBongVoiceCarrier_{GetInstanceID()}", 4096, 1, _outputRate, false);
		float[] unit = new float[4096];
		for (int i = 0; i < unit.Length; i++) unit[i] = 1f;
		_carrier.SetData(unit, 0);

		_source = gameObject.AddComponent<AudioSource>();
		_source.clip = _carrier;
		_source.loop = true;
		_source.playOnAwake = false;

		if (template != null)
		{
			// Copy the vanilla voice-line source's 3D setup rather than guessing, so the live
			// voice attenuates exactly like BingBong's own lines. Routing through its mixer
			// group also means the game's volume sliders apply for free.
			_source.spatialBlend = template.spatialBlend;

			ApplyRange(ConfigHandler.BingBongVoiceMaxDistance.Value);
			_source.rolloffMode = template.rolloffMode;
			_source.dopplerLevel = template.dopplerLevel;
			_source.spread = template.spread;
			_source.outputAudioMixerGroup = template.outputAudioMixerGroup;
		}
		else
		{
			_source.spatialBlend = 0f; // debug instance: non-positional, always audible
		}

		_source.Play();
		_liveSources++;
		AudioSettings.OnAudioConfigurationChanged += OnAudioConfigChanged;
	}

	/// <summary>
	/// Range needs both distances, not just maxDistance. Under Logarithmic rolloff the falloff is
	/// governed by minDistance -- volume goes roughly as minDistance/distance -- and maxDistance
	/// only marks where attenuation stops changing. Vanilla's minDistance of 1 means the sound is
	/// at 1/30 volume by 30m, so raising maxDistance alone did nothing at all. Scaling both
	/// together stretches the same curve over the range asked for: full volume within a tenth of
	/// it, and still audible out at the limit.
	/// </summary>
	private void ApplyRange(float range)
	{
		if (_source == null) return;
		_source.maxDistance = range;
		_source.minDistance = Mathf.Max(1f, range / 10f);
		_appliedRange = range;
	}

	private void OnAudioConfigChanged(bool deviceChanged)
	{
		// Owning the resample ratio ourselves makes this a one-line update instead of a rebuild.
		_outputRate = AudioSettings.outputSampleRate;
		_gainStep = 1f / (_outputRate * 0.015f);
		_state = State.Buffering;
	}

	private void Update()
	{
		_stream = BingBongVoiceModule.Instance?.Client?.Stream;

		_targetGain = Time.time < DuckUntil
			? ConfigHandler.BingBongVoiceDuckVolume.Value
			: ConfigHandler.BingBongVoiceVolume.Value;

		// Picked up live, like the volumes are, so the range can be tuned without re-spawning
		// every BingBong in the scene.
		float range = ConfigHandler.BingBongVoiceMaxDistance.Value;
		if (!Mathf.Approximately(_appliedRange, range)) ApplyRange(range);

		if (_mouth != null && !_mouthDisabled)
		{
			try
			{
				DriveMouth();
			}
			catch (Exception e)
			{
				Debug.LogError($"[BingBongVoice] Mouth animation failed: {e}");
				if (++_mouthStrikes >= 3)
				{
					_mouthDisabled = true;
					Debug.LogError("[BingBongVoice] Mouth animation disabled for this session.");
				}
			}
		}

		if (!_reportedBlock && _blockFrames > 0)
		{
			_reportedBlock = true;
			AudioSettings.GetDSPBufferSize(out int dspLength, out int dspCount);
			Debug.Log($"[BingBongVoice] DSP block is {_blockFrames} frames ({_blockFrames * 1000 / Math.Max(1, _outputRate)}ms at {_outputRate}Hz); buffer {dspLength}x{dspCount}.");
		}

		if (Time.time - _lastReportTime > 5f && _underruns > 0)
		{
			_lastReportTime = Time.time;
			int depthMs = _depthSamples * 1000 / Math.Max(1, _streamRate);
			int minMs = _minDepth == int.MaxValue ? -1 : _minDepth * 1000 / Math.Max(1, _streamRate);
			int targetMs = TargetSamples(_streamRate) * 1000 / Math.Max(1, _streamRate);

			long writeNow = _stream?.Ring.WritePosition ?? 0;
			float elapsed = Time.time - _lastRateTime;
			float producedRatio = elapsed > 0.01f && _lastWritePos > 0
				? (writeNow - _lastWritePos) / (elapsed * Math.Max(1, _streamRate))
				: 1f;
			_lastWritePos = writeNow;
			_lastRateTime = Time.time;

			string hint = producedRatio < 0.95f
				? $" - SENDER is only producing {producedRatio:P0} of real time"
				: depthMs <= 1 ? " - CONSUMER is draining faster than real time" : "";

			Debug.Log($"[BingBongVoice] buffer health: {_underruns} underruns, depth {depthMs}ms (min {minMs}ms) of {targetMs}ms{hint}");
			_underruns = 0;
			_minDepth = int.MaxValue;
		}
	}

	/// <summary>
	/// Drives BingBong's mouth from the audio we are actually playing, so it works on every
	/// client rather than only the speaker's.
	///
	/// BingBongMouth.Update only writes the animator parameter while canPlay is true, so holding
	/// canPlay false is enough to take over cleanly -- no fighting over script execution order.
	/// During a duck we stop forcing it and stop writing, which lets OnAsk -> CreateCurveMap set
	/// canPlay itself and animate the canned line off its baked curve exactly as shipped.
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private void DriveMouth()
	{
		BingBongMouth mouth = _mouth!;
		if (mouth == null || mouth.animator == null) return;

		if (Time.time < DuckUntil)
		{
			// Vanilla owns the mouth for the duration of its own voice line.
			_mouthValue = 0f;
			return;
		}

		if (_stream != null)
		{
			mouth.canPlay = false;

			// Vanilla's units exactly: BingBongMouth bakes a curve of MEAN |sample| per window and
			// feeds the animator curveMap.Evaluate(t) * maxMouthOpen, so maxMouthOpen is the factor
			// that lifts those small means into the animator's range. Passing an already-normalised
			// 0-1 value and then multiplying by it saturated the parameter, which held the jaw wide
			// open for anything above near-silence. Matching the scale means it behaves like a
			// canned line does, whatever maxMouthOpen happens to be.
			float target = _rawAmplitude * mouth.maxMouthOpen;
			_mouthValue = Mathf.Lerp(_mouthValue, target, Time.deltaTime * 25f);
			mouth.animator.SetFloat(_mouthHash, _mouthValue);
		}
		else if (_mouthValue > 0.001f)
		{
			// Disconnected: close it rather than leaving the jaw hanging where it stopped.
			_mouthValue = Mathf.Lerp(_mouthValue, 0f, Time.deltaTime * 10f);
			mouth.animator.SetFloat(_mouthHash, _mouthValue);
		}
	}

	// How much audio to hold before playing. One DSP block is consumed on every call, so the
	// floor is (target - block); keeping two blocks plus the configured margin leaves that floor
	// comfortably above the point where a read cannot be satisfied.
	private int TargetSamples(int streamRate)
	{
		int blockStream = (int)(_blockFrames * (double)streamRate / Math.Max(1, _outputRate)) + 1;
		return blockStream * 2 + (int)(streamRate * ConfigHandler.BingBongVoiceJitterMs.Value / 1000f);
	}

	// ---- audio thread from here down -------------------------------------------------
	// Allocation-free, lock-free, no Unity API calls, no logging. An exception escaping onto
	// the DSP thread is not recoverable, so everything is wrapped and falls back to silence.

	private void OnAudioFilterRead(float[] data, int channels)
	{
		try
		{
			Fill(data, Math.Max(1, channels));
		}
		catch
		{
			Array.Clear(data, 0, data.Length);
			_amplitude = 0f;
			if (++_strikes >= 3) _dead = true;
		}
	}

	private void Fill(float[] data, int channels)
	{
		int frames = data.Length / channels;
		_blockFrames = frames;

		VoiceStream? stream = _stream;
		if (_dead || stream == null)
		{
			Silence(data);
			return;
		}

		VoiceRingBuffer ring = stream.Ring;
		long write = ring.WritePosition;
		double ratio = (double)ring.SampleRate / _outputRate;
		int targetSamples = TargetSamples(ring.SampleRate);

		if (_state == State.Buffering)
		{
			if (write - (long)_read < targetSamples || write < targetSamples)
			{
				Silence(data);
				return;
			}
			_read = write - targetSamples;
			_state = State.Playing;
		}

		// Drifted too far behind (sender burst, or we were paused). Rejoin the live edge rather
		// than playing further and further into the past.
		if (write - (long)_read > targetSamples * 5)
		{
			_read = write - targetSamples;
		}

		double needed = frames * ratio;
		long available = write - (long)_read;

		_depthSamples = (int)Math.Min(int.MaxValue, available);
		if (_depthSamples < _minDepth) _minDepth = _depthSamples;
		_streamRate = ring.SampleRate;

		int usableFrames = frames;
		if (available < needed + 2)
		{
			usableFrames = (int)Math.Max(0, (available - 2) / ratio);
			if (usableFrames > frames) usableFrames = frames;
			_underruns++;
			// Only rebuild the whole buffer when there was essentially nothing there. A brief
			// shortfall should cost a few samples, not a fade plus a full buffer of silence.
			if (usableFrames < frames / 4) _state = State.Buffering;
		}

		Render(data, ring, channels, usableFrames, ratio, fadeOut: usableFrames < frames);

		// Pad any frames we could not fill.
		for (int f = usableFrames; f < frames; f++)
			for (int c = 0; c < channels; c++)
				data[f * channels + c] = 0f;
	}

	private void Render(float[] data, VoiceRingBuffer ring, int channels, int frames, double ratio, bool fadeOut)
	{
		float peak = 0f;
		float rawSum = 0f;
		float fadeStep = fadeOut && frames > 0 ? _gain / frames : 0f;

		for (int f = 0; f < frames; f++)
		{
			long i0 = (long)_read;
			float frac = (float)(_read - i0);
			float sample = ring[i0] * (1f - frac) + ring[i0 + 1] * frac;

			if (fadeOut) _gain = Math.Max(0f, _gain - fadeStep);
			else _gain += (_targetGain - _gain) * _gainStep;

			float outSample = sample * _gain;
			int baseIndex = f * channels;
			for (int c = 0; c < channels; c++) data[baseIndex + c] *= outSample;

			float abs = outSample < 0f ? -outSample : outSample;
			if (abs > peak) peak = abs;

			rawSum += sample < 0f ? -sample : sample;

			_read += ratio;
		}

		_amplitude = peak;
		_rawAmplitude = frames > 0 ? rawSum / frames : 0f;
	}

	private void Silence(float[] data)
	{
		Array.Clear(data, 0, data.Length);
		_gain = 0f;
		_amplitude = 0f;
		_rawAmplitude = 0f;
	}

	// ----------------------------------------------------------------------------------

	private void OnDestroy()
	{
		// The audio thread can and will run the filter after Destroy happens on the main thread,
		// so flag it dead first: that check is what makes the teardown safe.
		_dead = true;
		_liveSources--;
		AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigChanged;

		if (_source != null)
		{
			_source.Stop();
			_source.clip = null;
		}
		if (_carrier != null) Destroy(_carrier);
	}

	private void OnDisable()
	{
		_playingSources--;
		if (_source != null) _source.Stop();
		_state = State.Buffering;
	}

	private void OnEnable()
	{
		_playingSources++;
		// Rejoin live rather than trying to resume where we left off.
		_state = State.Buffering;
		if (_source != null && _carrier != null) _source.Play();
	}
}
