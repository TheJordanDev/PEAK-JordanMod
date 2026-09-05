using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using JordanMod.Utils;
using UnityEngine;

namespace JordanMod.Modules.BetterBugle;

public class BetterBuglePatch
{

	private static readonly List<string> SupportedItemNames = ["Bugle", "Bugle_Magic", "Megaphone"];

	[HarmonyPatch(typeof(Item), "Start")]
	[HarmonyPostfix]
	static void ItemStartPostfix(Item __instance)
	{
		if (__instance.UIData == null) return;

		if (!SupportedItemNames.Contains(__instance.UIData.itemName)) return;

		// This used to bail unless itemState was already Held. ItemState.Ground is the enum's
		// zero value and Item.Start doesn't set state, so anything spawned into the world never
		// got a BetterBugleSFX -- and since BugleSFXUpdatePostfix silences the vanilla BugleSFX,
		// a Bugle picked up off the ground was completely silent. It only ever worked because
		// SpawnItemInHand calls Interact() on the same frame it instantiates, landing
		// SetState(Held) before Start runs.
		if (__instance.TryGetComponent<BetterBugleSFX>(out _)) return;
		{
			Action secondaryAction = OnRightClick;
			Action<float> scrollAction = OnScroll;

			__instance.UIData.hasSecondInteract = true;
			__instance.UIData.hasScrollingInteract = true;

			__instance.OnSecondaryStarted += secondaryAction;
			__instance.OnScrolled += scrollAction;

			__instance.UIData.secondaryInteractPrompt = "SONG_LIST";
			__instance.UIData.scrollInteractPrompt = "CHANGE_SONG";

			BetterBugleSFX betterBugleSFX = __instance.gameObject.AddComponent<BetterBugleSFX>();
			if (__instance.UIData.itemName == "Megaphone") betterBugleSFX.isMegaphone = true;
		}
	}

	private static void OnRightClick()
	{
		// if (Song.Songs.Count == 0)
		// {
		// 	BetterBugleUI.Instance?.ShowActionbar("No songs available.");
		// 	return;
		// }
		if (AudioSyncWorker.IsLoading || AudioSyncWorker.IsSyncing) return;
		if (!BetterBugleModule.HadConfirmation)
		{
			BetterBugleUI.Instance?.ShowActionbar("Are you sure you want to refresh songs ? Right-click again to reload.");
			BetterBugleModule.HadConfirmation = true;
			Plugin.Instance.StartCoroutine(ResetConfirmation());
			return;
		}
		else
		{
			BetterBugleModule.HadConfirmation = false; // Reset confirmation state
			BetterBugleUI.Instance?.ShowActionbar("Refreshing songs...");
			// Refresh = unload everything and reload from local files only (no network).
			// For downloading from the audio bank API, use the Sync keybind instead.
			AudioSyncService.ClearAudioClips();
			AudioSyncWorker.GetAudioClips();
		}

	}

	private static IEnumerator ResetConfirmation()
	{
		yield return new WaitForSeconds(2f);
		if (!BetterBugleModule.HadConfirmation) yield break; // already confirmed (or reset) in the meantime
		BetterBugleUI.Instance?.ShowActionbar("No answer, not refreshing songs.");
		BetterBugleModule.HadConfirmation = false;
	}

	private static void OnScroll(float scrollDelta)
	{
		if (AudioSyncWorker.IsLoading) return;
		bool isNext = scrollDelta > 0;
		if (Song.Songs.Count == 0)
		{
			BetterBugleUI.Instance?.ShowActionbar("No songs available.");
			return;
		}

		if (isNext && AudioSyncWorker.CurrentSongIndex < Song.Songs.Count - 1) AudioSyncWorker.CurrentSongIndex++;
		else if (isNext && AudioSyncWorker.CurrentSongIndex == Song.Songs.Count - 1) AudioSyncWorker.CurrentSongIndex = 0;
		else if (!isNext && AudioSyncWorker.CurrentSongIndex > 0) AudioSyncWorker.CurrentSongIndex--;
		else AudioSyncWorker.CurrentSongIndex = Song.Songs.Count - 1;
		AudioSyncWorker.CurrentSongName = Song.GetSongNames_Alphabetically()[AudioSyncWorker.CurrentSongIndex];

		Song currentSong = Song.Songs[AudioSyncWorker.CurrentSongName];

		bool isFavorite = Song.FavoriteSongs.Contains(AudioSyncWorker.CurrentSongName);
		BetterBugleUI.Instance?.ShowActionbar($" {(isFavorite ? "★" : " ")} {currentSong.RealIndex} | {currentSong.Name.Replace("_", " ")}");
	}

	[HarmonyPatch(typeof(CharacterItems), "Awake")]
	[HarmonyPostfix]
	static void CharacterItemsEquipPostfix(CharacterItems __instance)
	{
		__instance.onSlotEquipped += () =>
		{
			if (__instance.character == null || __instance.character != Character.localCharacter) return;
			Item? currentItem = __instance.character.data.currentItem;
			if (currentItem == null || currentItem.UIData == null) return;
			if (currentItem.itemState != ItemState.Held) return;
			if (currentItem.TryGetComponent<BugleSFX>(out var bugleSFX))
			{
				Song? song = Song.Songs.GetValueOrDefault(AudioSyncWorker.CurrentSongName);
				if (song == null) return;

				bool isFavorite = Song.FavoriteSongs.Contains(AudioSyncWorker.CurrentSongName);
				BetterBugleUI.Instance?.ShowActionbar($"{(isFavorite ? "★" : " ")} {song.RealIndex} | {song.Name}");
			}
		};
	}

	[HarmonyPatch(typeof(BugleSFX), "Update")]
	[HarmonyPostfix]
	static void BugleSFXUpdatePostfix(BugleSFX __instance)
	{
		// Only silence the vanilla bugle where our replacement actually took over. Muting
		// unconditionally meant any bugle we failed to attach to played nothing at all.
		if (!__instance.TryGetComponent<BetterBugleSFX>(out _)) return;
		if (__instance.volume > 0f) __instance.volume = 0;
	}

}

