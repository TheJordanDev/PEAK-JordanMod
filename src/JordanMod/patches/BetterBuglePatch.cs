using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using JordanMod.Utils;
using UnityEngine;

namespace JordanMod.Modules.BetterBugle;

public class BetterBuglePatch
{

	[HarmonyPatch(typeof(Item), "Start")]
	[HarmonyPostfix]
	static void ItemStartPostfix(Item __instance)
	{
		if (__instance.itemState != ItemState.Held) return;
		if (__instance.UIData == null) return;

		List<string> supportedItemNames = ["Bugle", "Bugle_Magic", "Megaphone"];

		if (!supportedItemNames.Contains(__instance.UIData.itemName)) return;
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
		if (AudioSyncWorker.IsLoading) return;
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
			AudioSyncService.GetAudioClips();
		}

	}

	private static IEnumerator ResetConfirmation()
	{
		if (!BetterBugleModule.HadConfirmation) yield break;
		yield return new WaitForSeconds(2f);
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
		if (__instance.volume > 0f) __instance.volume = 0;
	}

}

