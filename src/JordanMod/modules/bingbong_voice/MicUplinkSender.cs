using System;
using System.Threading;

namespace JordanMod.Modules.BingBongVoice;

/// <summary>
/// Frames captured microphone audio and puts it on the socket the client already holds.
///
/// Sends from a dedicated thread with the blocking <c>Send</c> rather than <c>SendAsync</c>:
/// websocket-sharp's async path posts to the thread pool, so ordering across queued sends is not
/// guaranteed, and reordered voice sounds far worse than dropped voice. One thread and a blocking
/// send gives strict ordering for free.
///
/// There is deliberately no pacer. The microphone is the clock -- Photon hands us exactly the
/// audio the hardware produced -- so anything that paced against a wall clock on top of that would
/// only fight it and guarantee drift in one direction.
/// </summary>
public sealed class MicUplinkSender
{
	private const byte Magic = 0xB2;        // 0xB1 is downlink; a distinct magic makes a relay
	private const byte Version = 1;         // mis-route unable to replay room audio into the room
	private const byte CodecPcm16 = 0;
	private const byte RateCode16k = 0;
	private const int HeaderSize = 12;
	private const int PayloadBytes = MicUplinkSource.FrameSamples * 2;
	private const int FrameBytes = HeaderSize + PayloadBytes;
	private const int RingSlots = 32;       // ~640 ms; far more than a healthy socket ever needs

	private readonly byte[][] _slots = new byte[RingSlots][];
	private readonly BingBongVoiceClient _client;
	private readonly SemaphoreSlim _signal = new(0);

	private long _write;
	private long _read;
	private uint _seq;
	private uint _timestamp;

	private Thread? _thread;
	private volatile bool _running;
	private int _dropped;
	private int _sent;
	private int _consecutiveFailures;
	private volatile bool _stopped;

	public int Dropped => _dropped;
	public int Sent => _sent;

	public MicUplinkSender(BingBongVoiceClient client)
	{
		_client = client;
		for (int i = 0; i < RingSlots; i++) _slots[i] = new byte[FrameBytes];
	}

	public void Start()
	{
		if (_running) return;
		_running = true;
		_thread = new Thread(SendLoop) { IsBackground = true, Name = "BingBongUplink" };
		_thread.Start();
	}

	public void Stop()
	{
		_running = false;
		_signal.Release();
	}

	/// Producer side: called from the mic path. Writes the frame into a pre-allocated slot, so
	/// nothing is allocated per frame and the caller never blocks.
	public void Enqueue(short[] samples)
	{
		if (_stopped || !_running) return;

		// Ring full means the socket is not draining. Drop the OLDEST and keep the newest: on a
		// live stream, stale audio is worth less than current audio, and blocking the producer
		// would stall Photon's audio path.
		if (_write - _read >= RingSlots)
		{
			Interlocked.Increment(ref _dropped);
			Interlocked.Increment(ref _read);
		}

		byte[] slot = _slots[(int)(_write % RingSlots)];

		slot[0] = Magic;
		slot[1] = Version;
		slot[2] = CodecPcm16;
		slot[3] = RateCode16k;      // the relay overwrites the high nibble with our stream id
		WriteUInt32(slot, 4, _seq);
		WriteUInt32(slot, 8, _timestamp);

		for (int i = 0; i < MicUplinkSource.FrameSamples; i++)
		{
			short s = samples[i];
			int b = HeaderSize + (i << 1);
			slot[b] = (byte)(s & 0xFF);
			slot[b + 1] = (byte)((s >> 8) & 0xFF);
		}

		// Both counters advance only for frames we actually emit, so a gated pause leaves the
		// timeline contiguous and the receiver must not insert silence for it. The control frames
		// are what tell it to resync instead.
		_seq++;
		_timestamp += MicUplinkSource.FrameSamples;

		Interlocked.Increment(ref _write);
		_signal.Release();
	}

	private void SendLoop()
	{
		while (_running)
		{
			try
			{
				_signal.Wait(200);
				while (_read < Volatile.Read(ref _write))
				{
					byte[] slot = _slots[(int)(_read % RingSlots)];
					if (_client.TrySend(slot))
					{
						Interlocked.Increment(ref _sent);
						_consecutiveFailures = 0;
					}
					else if (++_consecutiveFailures >= 200)
					{
						// The socket has been refusing for four seconds. Stop rather than spin;
						// reconnection is the client's job and a second driver would fight it.
						_stopped = true;
						Debug.LogWarning("[BingBongVoice] Uplink stopped: the socket has been refusing frames. It will resume on the next connect.");
						return;
					}
					Interlocked.Increment(ref _read);
				}
			}
			catch (Exception e)
			{
				Debug.LogError($"[BingBongVoice] Uplink send loop failed: {e}");
				return;
			}
		}
	}

	private static void WriteUInt32(byte[] buffer, int offset, uint value)
	{
		buffer[offset] = (byte)(value & 0xFF);
		buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
		buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
		buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
	}
}
