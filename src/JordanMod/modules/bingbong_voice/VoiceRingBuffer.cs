using System;
using System.Threading;

namespace JordanMod.Modules.BingBongVoice;

/// <summary>
/// Ring of mono float samples at the stream's sample rate. Single producer (the websocket
/// receive thread), multiple consumers (one <see cref="BingBongVoiceSource"/> per BingBong,
/// each reading from the audio thread with its own cursor).
///
/// Lock-free by construction: the producer publishes <see cref="WritePosition"/> only after the
/// samples are in place, and the ring is far deeper than the jitter target, so a consumer can
/// never observe a slot that is being overwritten under it. Consumers hold absolute positions
/// and resync if they ever fall further behind than the ring is long.
/// </summary>
public sealed class VoiceRingBuffer
{
	private readonly float[] _buf;
	private readonly int _mask;
	private long _write;

	public int SampleRate { get; }
	public int Capacity => _buf.Length;

	/// Total samples ever written. Monotonic; never reset, so consumer cursors stay meaningful.
	public long WritePosition => Volatile.Read(ref _write);

	public VoiceRingBuffer(int sampleRate, float seconds = 4f)
	{
		SampleRate = sampleRate;
		int wanted = Math.Max(1024, (int)(sampleRate * seconds));
		int size = 1;
		while (size < wanted) size <<= 1; // power of two so the wrap is a mask, not a modulo
		_buf = new float[size];
		_mask = size - 1;
	}

	/// Producer thread only. Converts interleaved little-endian PCM16 straight out of the
	/// websocket's byte[] with no intermediate allocation.
	public void WritePcm16(byte[] src, int offset, int byteCount)
	{
		int samples = byteCount >> 1;
		if (samples <= 0) return;

		long w = _write; // safe unsynchronised read: this thread is the only writer
		for (int i = 0; i < samples; i++)
		{
			int b = offset + (i << 1);
			short s = (short)(src[b] | (src[b + 1] << 8));
			_buf[(int)((w + i) & _mask)] = s * (1f / 32768f);
		}

		Volatile.Write(ref _write, w + samples); // publish only once the data is really there
	}

	/// Producer thread only. Same as WritePcm16 but from samples we produced ourselves, so the
	/// loopback path does not have to encode to bytes and immediately decode again.
	public void WritePcm16Samples(short[] src, int count)
	{
		if (count <= 0) return;
		long w = _write;
		for (int i = 0; i < count; i++) _buf[(int)((w + i) & _mask)] = src[i] * (1f / 32768f);
		Volatile.Write(ref _write, w + count);
	}

	/// Producer thread only. Used to reproduce an exact gap the sender told us about, so a
	/// dropped frame costs its own duration rather than shifting everything after it.
	public void WriteSilence(int samples)
	{
		if (samples <= 0) return;
		if (samples > _buf.Length) samples = _buf.Length;

		long w = _write;
		for (int i = 0; i < samples; i++) _buf[(int)((w + i) & _mask)] = 0f;
		Volatile.Write(ref _write, w + samples);
	}

	/// Consumer read. The caller is responsible for staying within
	/// <see cref="Capacity"/> of <see cref="WritePosition"/>.
	public float this[long index] => _buf[(int)(index & _mask)];
}
