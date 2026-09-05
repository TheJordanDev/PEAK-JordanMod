"""Development stub relay for BingBong Live Voice.

Speaks the exact wire format the real relay (an endpoint on the AudioBank FastAPI service)
will use, so the mod can be built and tested before that backend exists. Deliberately minimal:
no auth, no session directory, no persistence. It is a test fixture, not a reference
implementation — build the real thing from the plan, not from this.

Requires `websockets` (pip install websockets).

    python tools/stub_relay.py serve           # relay on ws://localhost:8787/voice/ws
    python tools/stub_relay.py tone            # stream a 1 kHz sine into channel "test"
    python tools/stub_relay.py tone --sweep    # sweep 200-2000 Hz (resampling bugs are obvious)
    python tools/stub_relay.py tone --speech   # syllables and word gaps (for mouth animation)
    python tools/stub_relay.py listen          # dump received frames, to verify fan-out

Point the mod at it with, in BepInEx config:
    [BingBong Voice]
    RelayURL = ws://localhost:8787/voice/ws?token=listen:test
Leaving RelayURL empty instead derives the URL from BugleSoundAPIURL, which is where the
relay lives in production.

Connect as:  ws://localhost:8787/voice/ws?token=listen:test
             ws://localhost:8787/voice/ws?token=speak:test

Wire format — 12-byte little-endian header, then PCM16LE mono payload:
    0  u8   magic 0xB1
    1  u8   version 1
    2  u8   codec     0=PCM16LE 1=mu-law
    3  u8   rate code 0=16k 1=8k 2=24k 3=48k
    4  u32  seq
    8  u32  sampleTimestamp (samples since stream start)
Binary frame = audio. Text frame = JSON control.
"""

import argparse
import asyncio
import json
import math
import struct
import sys
from collections import defaultdict
from urllib.parse import urlparse, parse_qs

from websockets.asyncio.server import serve
from websockets.asyncio.client import connect

HOST = "localhost"
PORT = 8787
PATH = "/voice/ws"

MAGIC = 0xB1
VERSION = 1
CODEC_PCM16 = 0
RATE_CODES = {16000: 0, 8000: 1, 24000: 2, 48000: 3}
FRAME_MS = 20

HEADER = struct.Struct("<BBBBII")


def pack_header(seq: int, ts: int, rate: int = 16000, codec: int = CODEC_PCM16) -> bytes:
	return HEADER.pack(MAGIC, VERSION, codec, RATE_CODES[rate], seq & 0xFFFFFFFF, ts & 0xFFFFFFFF)


# ---------------------------------------------------------------- relay

channels: dict[str, dict] = defaultdict(lambda: {"speaker": None, "listeners": set()})
names: dict = {}


def roster(channel: str) -> str:
	"""Who the relay believes is in this lobby -- the stub's stand-in for /voice/sessions."""
	return ", ".join(sorted(names.get(w, "?") for w in channels[channel]["listeners"])) or "(nobody)"


def print_sessions():
	if not channels:
		print("[relay] no active sessions")
		return
	for chan, ch in channels.items():
		if not ch["listeners"] and ch["speaker"] is None:
			continue
		speaking = "speaking" if ch["speaker"] is not None else "idle"
		print(f"[relay] {chan}  {len(ch['listeners'])} listening  [{speaking}]  {roster(chan)}")


def parse_token(raw_path: str):
	"""token=<role>:<channel>. Returns (role, channel) or (None, None)."""
	q = parse_qs(urlparse(raw_path).query)
	token = (q.get("token") or [""])[0]
	if not token:
		return None, None
	# "<role>:<channel>" is this stub's own convention, kept because it makes a speaker and a
	# listener easy to start from a shell. The deployed relay takes a bare channel id, which is
	# what the mod sends, so accept that too and default it to listening.
	if ":" in token:
		role, _, channel = token.partition(":")
		if role not in ("listen", "speak") or not channel:
			return None, None
		return role, channel
	return "listen", token


async def handler(ws):
	role, channel = parse_token(ws.request.path)
	if role is None:
		await ws.close(1008, "bad token")
		return

	ch = channels[channel]

	if role == "speak":
		old = ch["speaker"]
		if old is not None:
			try:
				await old.send(json.dumps({"type": "preempted"}))
				await old.close(1000, "preempted")
			except Exception:
				pass
		ch["speaker"] = ws
		print(f"[relay] speaker joined '{channel}' ({len(ch['listeners'])} listening)")
		await broadcast_control(channel, {"type": "speaker_joined"})
		try:
			async for msg in ws:
				if isinstance(msg, bytes):
					await fanout(channel, msg)
		except Exception:
			pass
		finally:
			if ch["speaker"] is ws:
				ch["speaker"] = None
			print(f"[relay] speaker left '{channel}'")
			await broadcast_control(channel, {"type": "speaker_left"})
		return

	ch["listeners"].add(ws)
	names[ws] = "?"
	print(f"[relay] listener joined '{channel}' -> {len(ch['listeners'])} listening")
	try:
		# Listeners send a hello frame naming the player. That is what the real relay turns into
		# its session directory, so the stub records it too and prints the roster.
		async for msg in ws:
			if not isinstance(msg, str):
				continue
			try:
				payload = json.loads(msg)
			except ValueError:
				continue
			if payload.get("type") == "hello":
				names[ws] = str(payload.get("player", "?"))
				print(f"[relay] '{names[ws]}' registered in '{channel}' -> roster: {roster(channel)}")
	except Exception:
		pass
	finally:
		ch["listeners"].discard(ws)
		who = names.pop(ws, "?")
		print(f"[relay] '{who}' left '{channel}' -> {len(ch['listeners'])} listening")


async def fanout(channel: str, payload: bytes):
	listeners = list(channels[channel]["listeners"])
	if not listeners:
		return
	# Real relay uses a bounded per-listener queue and drops oldest; the stub just fires and
	# forgets, which is fine locally and keeps this file short.
	await asyncio.gather(*(safe_send(w, payload) for w in listeners), return_exceptions=True)


async def broadcast_control(channel: str, obj: dict):
	msg = json.dumps(obj)
	await asyncio.gather(
		*(safe_send(w, msg) for w in list(channels[channel]["listeners"])),
		return_exceptions=True,
	)


async def safe_send(ws, payload):
	try:
		await ws.send(payload)
	except Exception:
		pass


async def cmd_serve(args):
	async with serve(handler, HOST, args.port, max_size=None):
		print(f"[relay] listening on ws://{HOST}:{args.port}{PATH}")
		print(f"[relay]   listen: ws://{HOST}:{args.port}{PATH}?token=listen:test")
		print(f"[relay]   speak : ws://{HOST}:{args.port}{PATH}?token=speak:test")
		print("[relay] session roster printed every 15s")
		while True:
			await asyncio.sleep(15)
			print_sessions()


# ---------------------------------------------------------------- tone generator


try:
	import numpy as _np
except ImportError:
	_np = None


def generate_frame(phase, ts, n, rate, args):
	"""Returns (new_phase, pcm16_bytes) for one frame, phase-continuous across frames."""
	if _np is not None:
		idx = _np.arange(n, dtype=_np.float64)
		t = (ts + idx) / rate
		if args.speech:
			# Speech-shaped: a voice-ish carrier under a syllable envelope with word gaps. The
			# sweep and steady tones hold a constant amplitude, so they pin a loudness-driven
			# mouth wide open -- this is the signal to test mouth animation against.
			freq = 150.0 + 60.0 * _np.sin(2 * _np.pi * 1.7 * t)
			syllables = (0.5 + 0.5 * _np.sin(2 * _np.pi * 4.0 * t)) ** 2
			words = (_np.sin(2 * _np.pi * 0.35 * t) > -0.25).astype(_np.float64)
			amp = 0.45 * syllables * words
		elif args.sweep:
			# 200-2000 Hz over 4 s: any resampling error shows up as an obvious warble
			freq = 200.0 + 1800.0 * (0.5 - 0.5 * _np.cos(2 * _np.pi * t / 4.0))
			amp = _np.full(n, 0.35)
		else:
			freq = _np.full(n, float(args.freq))
			amp = _np.full(n, 0.35)
		ph = phase + _np.cumsum(2 * _np.pi * freq / rate)
		samples = (_np.sin(ph) * amp * 32767).astype("<i2")
		return float(ph[-1] % (2 * _np.pi)), samples.tobytes()

	# Fallback without numpy. Fine for a steady tone; sweep may not hold real time.
	out = []
	for i in range(n):
		t = (ts + i) / rate
		freq = args.freq
		amp = 0.35
		if args.speech:
			freq = 150.0 + 60.0 * math.sin(2 * math.pi * 1.7 * t)
			syllables = (0.5 + 0.5 * math.sin(2 * math.pi * 4.0 * t)) ** 2
			words = 1.0 if math.sin(2 * math.pi * 0.35 * t) > -0.25 else 0.0
			amp = 0.45 * syllables * words
		elif args.sweep:
			freq = 200.0 + 1800.0 * (0.5 - 0.5 * math.cos(2 * math.pi * t / 4.0))
		phase = (phase + 2 * math.pi * freq / rate) % (2 * math.pi)
		out.append(int(math.sin(phase) * amp * 32767))
	return phase, struct.pack(f"<{n}h", *out)


async def cmd_tone(args):
	url = f"ws://{HOST}:{args.port}{PATH}?token=speak:{args.channel}"
	rate = args.rate
	n = int(rate * FRAME_MS / 1000)
	print(f"[tone] {url}  {rate} Hz, {n} samples/frame ({FRAME_MS} ms)")

	loop = asyncio.get_running_loop()
	async with connect(url, max_size=None) as ws:
		seq = 0
		ts = 0
		phase = 0.0
		# Deadline-based pacing. A plain `sleep(FRAME_MS)` per iteration sleeps for *at least*
		# that long and then adds the generate+send time on top, so the sender silently drifts
		# behind real time and starves the receiver's jitter buffer. Track an absolute deadline
		# and sleep only for the remainder instead.
		next_deadline = loop.time()
		late_frames = 0

		while True:
			# Vectorised: generating 320 samples a frame in a Python loop costs more than the
			# 20 ms frame budget once sweep mode adds a second transcendental per sample, and the
			# sender then silently falls behind real time and starves the receiver.
			phase, payload = generate_frame(phase, ts, n, rate, args)

			await ws.send(pack_header(seq, ts, rate) + payload)
			seq += 1
			ts += n

			next_deadline += FRAME_MS / 1000
			delay = next_deadline - loop.time()
			if delay > 0:
				await asyncio.sleep(delay)
			else:
				# Generation itself overran the frame budget; we can't catch up by sleeping.
				late_frames += 1
				next_deadline = loop.time()

			if seq % 50 == 0:
				print(f"\r[tone] sent {seq} frames ({ts / rate:6.1f}s), {late_frames} late", end="", flush=True)


# ---------------------------------------------------------------- listener (verification)


async def cmd_listen(args):
	url = f"ws://{HOST}:{args.port}{PATH}?token=listen:{args.channel}"
	print(f"[listen] {url}")
	async with connect(url, max_size=None) as ws:
		count = 0
		last_seq = None
		gaps = 0
		async for msg in ws:
			if isinstance(msg, str):
				print(f"\n[listen] control: {msg}")
				continue
			magic, ver, codec, rc, seq, ts = HEADER.unpack_from(msg, 0)
			if magic != MAGIC or ver != VERSION:
				print(f"\n[listen] BAD HEADER magic={magic:#x} ver={ver}")
				continue
			if last_seq is not None and seq != last_seq + 1:
				gaps += 1
				print(f"\n[listen] SEQ GAP {last_seq} -> {seq}")
			last_seq = seq
			count += 1
			if count % 50 == 0:
				print(
					f"\r[listen] {count} frames, {len(msg) - 12} B payload, codec={codec} "
					f"rate={rc} ts={ts} gaps={gaps}",
					end="",
					flush=True,
				)


def main():
	p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
	p.add_argument("--port", type=int, default=PORT)
	sub = p.add_subparsers(dest="cmd", required=True)

	sub.add_parser("serve")

	t = sub.add_parser("tone")
	t.add_argument("--channel", default="test")
	t.add_argument("--freq", type=float, default=1000.0)
	t.add_argument("--rate", type=int, default=16000, choices=sorted(RATE_CODES))
	t.add_argument("--sweep", action="store_true", help="200-2000 Hz sweep; makes resampling errors obvious")
	t.add_argument("--speech", action="store_true", help="speech-shaped envelope; use this to test mouth animation")

	l = sub.add_parser("listen")
	l.add_argument("--channel", default="test")

	args = p.parse_args()
	fn = {"serve": cmd_serve, "tone": cmd_tone, "listen": cmd_listen}[args.cmd]
	try:
		asyncio.run(fn(args))
	except KeyboardInterrupt:
		print("\nbye")


if __name__ == "__main__":
	sys.exit(main())
