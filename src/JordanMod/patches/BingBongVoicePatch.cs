using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace JordanMod.Modules.BingBongVoice;

/// <summary>
/// Gives every BingBong a live-voice source as it spawns.
///
/// Structured defensively on purpose. A postfix that throws propagates out of the method it
/// patched, and because Mono resolves a method's call targets when it JITs that method, a game
/// API that changed shape throws before any guard inside can run -- that is how a spawner
/// postfix once took down every SingleItemSpawner in the level (see 0.1.13). So the patch body
/// is nothing but a try/catch around a separate non-inlined method, and three failures disable
/// it for the session. A broken voice feature must never cost anyone their BingBong.
/// </summary>
public class BingBongVoicePatch
{
	private static int _strikes;
	private static bool _disabled;
	private static int _attachCount;

	private static int _duckStrikes;
	private static bool _duckDisabled;

	[HarmonyPatch(typeof(Item), "Start")]
	[HarmonyPostfix]
	static void ItemStartPostfix(Item __instance)
	{
		if (_disabled) return;
		try
		{
			AttachIfBingBong(__instance);
		}
		catch (Exception e)
		{
			Fail(e);
		}
	}

	// The only method here that touches game APIs, kept separate and un-inlined so that a
	// missing member throws inside the caller's try rather than while JITing the patch itself.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AttachIfBingBong(Item item)
	{
		if (item == null || item.UIData == null) return;
		if (!item.itemTags.HasFlag(Item.ItemTags.BingBong)) return;
		if (BingBongVoiceSource.IsAttached(item.gameObject)) return;

		// Copy the vanilla voice-line source's 3D setup so the live voice carries and attenuates
		// exactly like BingBong's own lines, and inherits its mixer group.
		AudioSource? template = item.TryGetComponent(out Action_AskBingBong ask) ? ask.source : null;

		BingBongVoiceSource voice = BingBongVoiceSource.Attach(item.gameObject, template);

		// The mouth animator lives on a child of the plush, not the item root.
		BingBongMouth mouth = item.GetComponentInChildren<BingBongMouth>(includeInactive: true);
		if (mouth != null) voice.BindMouth(mouth);

		// Log the copied values: if the live voice turns out not to attenuate with distance,
		// this distinguishes "the copy failed" from "Unity spatialises before custom filters".
		// Counted because this should happen once per BingBong that exists, ever. If the number
		// climbs every time the item is grabbed or thrown then Item.Start is re-running on a
		// fresh instance, and the per-attach cost (AudioClip.Create, AddComponent) is the hitch.
		_attachCount++;
		if (template != null)
			Debug.Log($"[BingBongVoice] Attached to a BingBong (#{_attachCount}, {BingBongVoiceSource.LiveSources} live, {BingBongVoiceSource.PlayingSources} audible); spatialBlend={template.spatialBlend}, range={ConfigHandler.BingBongVoiceMaxDistance.Value}m, rolloff={template.rolloffMode}, mixer={(template.outputAudioMixerGroup != null ? template.outputAudioMixerGroup.name : "none")}.");
		else
			Debug.LogWarning("[BingBongVoice] Attached to a BingBong, but it has no Action_AskBingBong to copy 3D settings from - the voice will not be positional.");
	}

	private static void Fail(Exception e)
	{
		Debug.LogError($"[BingBongVoice] Attach patch failed: {e}");
		if (++_strikes < 3) return;
		_disabled = true;
		Debug.LogError("[BingBongVoice] Attach patch disabled for this session after repeated failures.");
	}

	/// <summary>
	/// Ducks the live voice while one of BingBong's own lines plays, so the two never talk over
	/// each other. This is the RPC body, so it runs on every client and the duck stays in step
	/// across the lobby without any networking of our own.
	/// </summary>
	[HarmonyPatch(typeof(Action_AskBingBong), nameof(Action_AskBingBong.Ask))]
	[HarmonyPostfix]
	static void AskPostfix(Action_AskBingBong __instance, int index, bool spamming)
	{
		if (_duckDisabled) return;
		try
		{
			DuckForVoiceLine(__instance, index, spamming);
		}
		catch (Exception e)
		{
			Debug.LogError($"[BingBongVoice] Duck patch failed: {e}");
			if (++_duckStrikes < 3) return;
			_duckDisabled = true;
			Debug.LogError("[BingBongVoice] Duck patch disabled for this session after repeated failures.");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DuckForVoiceLine(Action_AskBingBong ask, int index, bool spamming)
	{
		// Mirror vanilla's own early-outs, so we never duck for audio that is not going to play.
		// Ask() does nothing at all unless the item is held, and AskRoutine bails before playing
		// anything when it decides you are spamming the interaction.
		if (ask.item == null || ask.item.holderCharacter == null) return;
		if (spamming) return;

		// RunAction picks the index locally and RPCs the raw number, while ReplaceBingBongModule
		// appends a response per loaded voice line -- so two players with different audio bank
		// state disagree on the array length. Vanilla throws in that case; we must not.
		Action_AskBingBong.BingBongResponse[] responses = ask.responses;
		if (responses == null || index < 0 || index >= responses.Length) return;

		Action_AskBingBong.BingBongResponse response = responses[index];
		if (response?.sfx == null || response.sfx.clips == null || response.sfx.clips.Length == 0) return;

		AudioClip clip = response.sfx.clips[0];
		if (clip == null) return;

		BingBongVoiceSource? voice = BingBongVoiceSource.Find(ask.item.gameObject);
		if (voice == null) return;

		// AskRoutine burns 0.5s in its update loop, stops the source, waits another 0.5s and only
		// then plays -- so audio starts about a second after this RPC lands. Ducking from now
		// rather than from the actual start reads as BingBong drawing breath, and removes a whole
		// class of "the duck began a few frames late and you heard a syllable" bugs.
		float duration = ConfigHandler.BingBongVoiceDuckLead.Value + clip.length + 0.2f;
		voice.DuckUntil = Math.Max(voice.DuckUntil, Time.time + duration);
	}
}
