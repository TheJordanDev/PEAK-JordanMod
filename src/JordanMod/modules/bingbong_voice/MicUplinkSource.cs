using System;
using System.Threading;
using UnityEngine;

namespace JordanMod.Modules.BingBongVoice;

/// <summary>
/// Turns the local microphone into 20 ms frames of 16 kHz mono PCM16, ready to go on the wire.
///
/// Fed by <see cref="BingBongMicTapPatch"/> from Photon's mic read, measured at 48000 Hz mono in
/// 960-sample frames arriving 50 times a second on Unity's main thread -- so the microphone itself
/// is the clock and there is deliberately no pacer here. A deadline pacer on top of a hardware
/// clock would only fight it and guarantee drift in one direction.
///
/// Allocation-free after the first callback: this runs on the audio path of every frame.
/// </summary>
public sealed class MicUplinkSource
{
	public const int WireRate = 16000;
	public const int FrameSamples = 320;   // 20 ms at 16 kHz

	private readonly short[] _frame = new short[FrameSamples];
	private int _frameFill;

	// Scratch copy of the incoming buffer. We must never filter in place: that array belongs to
	// Photon and is encoded and sent to teammates immediately after this postfix returns, so
	// writing to it would corrupt everyone else's audio.
	private float[]? _work;

	// Resampler state, carried across callbacks. _tail is the previous buffer's last sample and is
	// addressed as index -1, so the interpolation spans the seam instead of skipping it.
	private double _read;
	private float _tail;

	// Five cascaded one-pole low-passes. Decimating 48k to 16k without one folds everything above
	// 8 kHz down into the speech band, which sounds harsh and cannot be undone afterwards. Two
	// poles measured only -14 dB of rejection at 12 kHz; five at 7 kHz gives -25 dB while still
	// passing 95% at 1 kHz, where speech energy actually is.
	private const int FilterPoles = 5;
	private readonly float[] _lp = new float[FilterPoles];
	private float _lpCoefficient = 1f;

	private int _rate;
	private int _channels = 1;
	private int _sourceId = -1;

	private float _gain;
	private readonly float _gainStep = 1f / (WireRate * 0.015f);   // ~15 ms attack

	private volatile float _targetGain;
	private volatile bool _gateOpen;
	private volatile float _lastFrameLevel;
	private volatile float _noiseFloorPublished;
	private long _framesProduced;

	private float _noiseFloor;
	private int _holdFrames;

	/// Set by the proximity logic on the main thread. 0 mutes; the ramp keeps it click-free.
	public void SetTargetGain(float gain) => _targetGain = Mathf.Clamp(gain, 0f, 8f);

	/// Whether the uplink may transmit at all (range, push-to-talk, speaker present).
	public void SetGate(bool open) => _gateOpen = open;

	/// Mean |sample| of the last completed frame, for the level indicator.
	public float Level => _lastFrameLevel;

	public long FramesProduced => Interlocked.Read(ref _framesProduced);

	/// Raised for each completed 20 ms frame that passes the gate. Assigned once, never per frame.
	public Action<short[]>? OnFrame;

	/// <summary>
	/// Emit gated frames as silence rather than dropping them, keeping the output continuous.
	///
	/// Off for the real uplink: the whole point of the gate is not to spend bandwidth on silence,
	/// and the receiver is told to freeze its timeline instead. On for loopback, where the frames
	/// feed a jitter-buffered player with no control channel -- dropping 70% of them starves it
	/// into permanent rebuffering, which sounds like the capture path is broken when it is fine.
	/// </summary>
	public bool EmitSilenceWhenGated;

	private readonly short[] _silence = new short[FrameSamples];

	public void OnMicFrame(float[] buffer, int rate, int channels, int sourceId)
	{
		if (buffer == null || buffer.Length == 0 || rate <= 0) return;

		// The mic device can change mid-session (CharacterVoiceHandler reassigns the recorder's
		// device when the setting changes), rebuilding MicWrapper with a possibly different rate.
		// Starting the resampler over is cheaper and safer than trying to splice.
		if (rate != _rate || channels != _channels || sourceId != _sourceId)
		{
			Reset(rate, channels, sourceId);
			Debug.Log($"[BingBongVoice] Uplink capture at {rate}Hz x{channels}, resampling {(double)rate / WireRate:F2}:1 down to {WireRate}Hz.");
		}

		int count = _channels > 1 ? buffer.Length / _channels : buffer.Length;
		if (count < 2) return;
		if (_work == null || _work.Length < count) _work = new float[count];

		// Downmix and anti-alias in one pass into our own buffer. The filter has to see every
		// input sample, not just the ones the cursor lands on, or it is not an anti-alias filter.
		bool filter = _lpCoefficient < 1f;
		for (int i = 0; i < count; i++)
		{
			float x;
			if (_channels > 1)
			{
				float sum = 0f;
				int b = i * _channels;
				for (int c = 0; c < _channels; c++) sum += buffer[b + c];
				x = sum / _channels;
			}
			else x = buffer[i];

			if (filter)
			{
				for (int p = 0; p < FilterPoles; p++)
				{
					_lp[p] += (x - _lp[p]) * _lpCoefficient;
					x = _lp[p];
				}
			}
			_work[i] = x;
		}

		Resample(_work, count);
	}

	private void Reset(int rate, int channels, int sourceId)
	{
		_rate = rate;
		_channels = Math.Max(1, channels);
		_sourceId = sourceId;
		_read = 0;
		_tail = 0f;
		Array.Clear(_lp, 0, _lp.Length);
		_frameFill = 0;
		_gain = 0f;
		_noiseFloor = 0f;
		_holdFrames = 0;

		// One-pole cutoff at ~6.5 kHz, safely under the 8 kHz Nyquist of the 16 kHz wire rate.
		_lpCoefficient = rate > WireRate ? 1f - Mathf.Exp(-2f * Mathf.PI * 7000f / rate) : 1f;
	}

	private void Resample(float[] work, int count)
	{
		double ratio = (double)_rate / WireRate;
		double pos = _read;   // may be negative; -1 addresses the carried tail sample

		while (true)
		{
			int i0 = (int)Math.Floor(pos);
			if (i0 + 1 >= count) break;

			float a = i0 < 0 ? _tail : work[i0];
			float b = work[i0 + 1];
			float frac = (float)(pos - i0);

			_gain += (_targetGain - _gain) * _gainStep;
			Push((a * (1f - frac) + b * frac) * _gain);

			pos += ratio;
		}

		_tail = work[count - 1];
		_read = pos - count;               // carries the fractional remainder to the next buffer
		if (_read < -1) _read = -1;
	}

	/// <summary>
	/// Adaptive gate. A fixed threshold cannot work here: measured on real hardware, a silent but
	/// noisy room read louder (mean 0.0088) than the same person actually speaking in a quiet one
	/// (0.0018), so no single level separates speech from silence across setups. Instead track the
	/// background and require speech to stand above it.
	///
	/// The floor follows quiet quickly and loud very slowly, so it settles on the room rather than
	/// on the voice. A hold keeps the gate open through the gaps between words.
	/// </summary>
	private bool PassesNoiseGate(float level)
	{
		if (_noiseFloor <= 0f) _noiseFloor = level;
		else if (level < _noiseFloor) _noiseFloor += (level - _noiseFloor) * 0.25f;   // drop fast
		else _noiseFloor += (level - _noiseFloor) * 0.0008f;                          // rise slowly
		_noiseFloorPublished = _noiseFloor;

		// Never transmit below the absolute floor, whatever the adaptive part decides.
		if (level < Mathf.Pow(10f, ConfigHandler.BingBongVoiceUplinkNoiseGateDb.Value / 20f)) return false;

		float open = _noiseFloor * ConfigHandler.BingBongVoiceUplinkGateSensitivity.Value;
		float close = open * 0.6f;   // hysteresis, so a steady voice cannot chatter the gate

		if (level >= open) _holdFrames = 15;        // ~300 ms, bridges the gaps between words
		else if (level < close && _holdFrames > 0) _holdFrames--;

		return _holdFrames > 0;
	}

	/// The learned background level, for diagnostics.
	public float NoiseFloor => _noiseFloorPublished;

	private void Push(float sample)
	{
		if (sample > 1f) sample = 1f;
		else if (sample < -1f) sample = -1f;

		_frame[_frameFill++] = (short)(sample * 32767f);
		if (_frameFill < FrameSamples) return;
		_frameFill = 0;

		float sum = 0f;
		for (int i = 0; i < FrameSamples; i++)
		{
			int v = _frame[i];
			sum += v < 0 ? -v : v;
		}
		_lastFrameLevel = sum / FrameSamples / 32768f;
		Interlocked.Increment(ref _framesProduced);

		// Gated after framing, so the level meter keeps reading while muted and the frame counter
		// stays a measure of capture rather than of transmission.
		bool pass = _gateOpen && PassesNoiseGate(_lastFrameLevel);
		if (pass) OnFrame?.Invoke(_frame);
		else if (EmitSilenceWhenGated) OnFrame?.Invoke(_silence);
	}
}
