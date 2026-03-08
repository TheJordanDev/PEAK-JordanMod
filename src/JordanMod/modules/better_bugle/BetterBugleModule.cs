using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Networking;
using System.Linq;
using UnityEngine.Audio;
using Zorro.Settings;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;
using Zorro.Core;
using JordanMod.Utils;

namespace JordanMod.Modules.BetterBugle;

[Module(Enabled = true)]
class BetterBugleModule : Module
{

	public static BetterBugleModule? Instance { get; private set; }

	public override string ModuleName => "BetterBugle";
	public static readonly string bugleItemName = "Bugle";

	public static bool HadConfirmation { get; set; } = false;

	public static bool IsPlaying = false;
	public static AudioSource? CurrentAudioSource { get; set; } = null;

	public override Type[] GetPatches()
	{
		return [typeof(BetterBuglePatch)];
	}

	public override void Initialize()
	{
		if (Instance != null) return;
		Instance = this;
		ManageLocalizedText();
		SceneManager.sceneLoaded += OnSceneLoaded;
		AudioSyncWorker.OnAudioLoadComplete += OnAllAudioClipsLoaded;
		AudioSyncWorker.GetAudioClips();
		base.Initialize();
	}

	public override void Update()
	{
		if (Input.GetKeyDown(ConfigHandler.SyncAudioRepository.Value))
		{
			AudioSyncWorker.TrySyncAndLoadAudioClips();
		}
		if (Input.GetKeyDown(ConfigHandler.FavoriteSongToggleKey.Value))
		{
			if (Character.localCharacter == null) return;
			if (Song.Songs.Count == 0) return;
			if (!Song.Songs.ContainsKey(AudioSyncWorker.CurrentSongName)) return;
			Character character = Character.localCharacter;
			
			Optionable<byte> selectedSlot = character.refs.items.currentSelectedSlot;
			if (selectedSlot.IsNone) return;

			ItemSlot? itemSlot = character.player.itemSlots[selectedSlot.Value];
			if (itemSlot == null) return;

			Item? item = itemSlot.prefab;
			if (item == null) return;

			List<string> supportedItemNames = ["Bugle", "Bugle_Magic", "Megaphone"];
			if (!supportedItemNames.Contains(item.UIData.itemName)) return;
			
			Song? currentSong = Song.Songs.GetValueOrDefault(AudioSyncWorker.CurrentSongName);
			if (currentSong == null) return;

			if (Song.FavoriteSongs.Contains(currentSong.Name))
			{
				Song.FavoriteSongs.Remove(currentSong.Name);
				BetterBugleUI.Instance?.ShowActionbar($"Removed '{currentSong.Name}' from favorites.");
			}
			else
			{
				Song.FavoriteSongs.Add(currentSong.Name);
				BetterBugleUI.Instance?.ShowActionbar($"Added '{currentSong.Name}' to favorites.");
			}
			ConfigHandler.FavoriteSongsList.Value = string.Join("|-|", Song.FavoriteSongs);

		}
		base.Update();
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (!BetterBugleUI.Instance)
		{
			GameObject uiObject = new("BetterBugleUI");
			UnityEngine.Object.DontDestroyOnLoad(uiObject);
			uiObject.AddComponent<BetterBugleUI>();
		}
	}

	public override void Destroy()
	{
		AudioSyncService.ClearAudioClips();
		base.Destroy();
	}

	private void ManageLocalizedText()
	{
		List<string> secondaryActionLocalizations = new(LocalizedText.LANGUAGE_COUNT);
		for (int i = 0; i < LocalizedText.LANGUAGE_COUNT; i++) secondaryActionLocalizations.Add("Refresh Songs");

		List<string> scrollActionLocalizations = new(LocalizedText.LANGUAGE_COUNT);
		for (int i = 0; i < LocalizedText.LANGUAGE_COUNT; i++) scrollActionLocalizations.Add("Change Song");

		LocalizedText.mainTable.Add("SONG_LIST", secondaryActionLocalizations);
		LocalizedText.mainTable.Add("CHANGE_SONG", scrollActionLocalizations);
	}

	private void OnAllAudioClipsLoaded()
	{
		if (Song.Songs.Count == 0) Debug.LogWarning("No songs loaded. Please ensure audio files are in the Sounds directory.");
		else Debug.Log($"🎵 {Song.Songs.Count} songs loaded !");
		Song.UpdateRealIndices();
		BetterBugleUI.Instance?.ShowActionbar($"{Song.Songs.Count} songs loaded !");

		foreach (string songKeyName in ConfigHandler.FavoriteSongsList.Value.Split(["|-|"], StringSplitOptions.RemoveEmptyEntries))
			if (Song.Songs.ContainsKey(songKeyName) && !Song.FavoriteSongs.Contains(songKeyName))
				Song.FavoriteSongs.Add(songKeyName);

		if (!Song.Songs.ContainsKey(AudioSyncWorker.CurrentSongName))
			AudioSyncWorker.CurrentSongName = Song.GetSongNames_Alphabetically()[AudioSyncWorker.CurrentSongIndex];
	}

}

public class BetterBugleSFX : MonoBehaviourPun
{
	public Item? item;
	public MagicBugle? magicBugle;
	public bool isMegaphone = false;
	public Song? song;
	public AudioSource? audioSource;
	public float GetVolume => (isMegaphone && item?._holderCharacter != Character.localCharacter) ? ConfigHandler.BugleVolume.Value * 2 : ConfigHandler.BugleVolume.Value;

	public float maxBugleDistance = 500;
	public float maxMegaphoneDistance = 1000;

	public bool hold = false;
	public bool isTooting = false;

	private void Start()
	{
		item = GetComponent<Item>();
		TryGetComponent<MagicBugle>(out magicBugle);
		audioSource = gameObject.AddComponent<AudioSource>();
		audioSource.playOnAwake = false;
		audioSource.maxDistance = isMegaphone ? maxMegaphoneDistance : maxBugleDistance;
		audioSource.spatialBlend = 1f;
		audioSource.volume = 0f;
		audioSource.loop = true;
		if (IsLocal()) BetterBugleModule.CurrentAudioSource = audioSource;
		song = Song.Songs.GetValueOrDefault(AudioSyncWorker.CurrentSongName);
	}

	private bool IsLocal()
	{
		return item?._holderCharacter == Character.localCharacter;
	}

	private void Update()
	{
		if (item == null || audioSource == null) return;
		UpdateTooting();
		if (hold && !isTooting)
		{
			audioSource.clip = song?.AudioClip;
			if (audioSource.clip == null) return;
			audioSource.Play();
			audioSource.volume = GetVolume;
			isTooting = true;
			if (IsLocal()) BetterBugleModule.IsPlaying = true;
		}

		if (!hold && isTooting)
		{
			isTooting = false;
			if (IsLocal()) BetterBugleModule.IsPlaying = false;
		}

		if (hold) audioSource.volume = Mathf.Lerp(audioSource.volume, GetVolume, 10f * Time.deltaTime);
		if (!hold) audioSource.volume = Mathf.Lerp(audioSource.volume, 0f, 10f * Time.deltaTime);

		if (!isTooting && audioSource.volume <= 0.01f)
		{
			audioSource.Stop();
		}
	}

	private void UpdateTooting()
	{
		if (item == null || audioSource == null) return;
		if (!photonView.IsMine) return;
		bool flag = item.isUsingPrimary;
		if (magicBugle && magicBugle.currentFuel <= 0f) flag = false;

		if (flag != hold)
		{
			if (flag) photonView.RPC("RPC_StartBetterToot", RpcTarget.All, AudioSyncWorker.CurrentSongName);
			else photonView.RPC("RPC_StopBetterToot", RpcTarget.All);
			hold = flag;
		}
	}

	[PunRPC]
	private void RPC_StartBetterToot(string filename)
	{
		song = Song.Songs.GetValueOrDefault(filename);
		if (song == null) return;
		if (audioSource == null) return;
		hold = true;
	}

	[PunRPC]
	private void RPC_StopBetterToot()
	{
		if (audioSource == null) return;
		hold = false;
	}
}

public class BugleVolumeSettings : VolumeSetting
{
	public BugleVolumeSettings(AudioMixerGroup mixerGroup) : base(mixerGroup)
	{
	}

	public override string GetParameterName()
	{
		return "BugleVolume";
	}
	
	public string GetDisplayName()
	{
		return "Bugle Volume";
	}

	public string GetCategory()
	{
		return "Bugle";
	}
}

public class BetterBugleUI : MonoBehaviour
{

	public static BetterBugleUI? Instance { get; private set; }
	public bool IsVisible { get; private set; }

	private float lastChangeTime = -10f;

	private string soundDisplay = "";

	private GUIStyle? customStyle;

	private bool fontLoaded = false;

	private int offsetX = 0;

	private int offsetY = 70;

	private int fontSize = 42;

	private void Awake()
	{
		if (Instance != null)
		{
			Debug.LogError("Multiple instances of BetterBugleUI detected!");
			Destroy(gameObject);
			return;
		}
		Instance = this;
		DontDestroyOnLoad(gameObject);
		IsVisible = false;
	}


	public void ShowActionbar(string message)
	{
		soundDisplay = message;
		IsVisible = true;
		lastChangeTime = Time.time;
	}

	private void OnGUI()
	{
		if (!fontLoaded)
		{
			Font[] array = Resources.FindObjectsOfTypeAll<Font>();
			foreach (Font val in array)
			{
				if (val.name == "Tetsubin Gothic")
				{
					customStyle = new GUIStyle(GUI.skin.label);
					customStyle.font = val;
					customStyle.fontSize = fontSize;
					customStyle.alignment = TextAnchor.LowerCenter;
					customStyle.normal.textColor = Color.white;
					fontLoaded = true;
					break;
				}
			}
		}
		if (customStyle == null) return;
		RenderSoundDisplay();
		RenderPlayingDisplay();
	}

	private void RenderSoundDisplay()
	{
		if (customStyle == null) return;
		if (!IsVisible) return;
		if (Time.time - lastChangeTime > 3f)
		{
			IsVisible = false;
			return;
		}

		float maxWidth = Screen.width - (offsetX * 2);
		float textHeight = customStyle.CalcHeight(new GUIContent(soundDisplay), maxWidth);

		// Align the bottom of the text block to (Screen.height - offsetY)
		float y = Screen.height - offsetY - textHeight;

		GUI.Label(new Rect(offsetX, y, maxWidth, textHeight), soundDisplay, customStyle);
	}

	private void RenderPlayingDisplay()
	{
		if (customStyle == null) return;
		if (BetterBugleModule.CurrentAudioSource == null || !BetterBugleModule.IsPlaying) return;
		Song? currentAudio = Song.Songs.FirstOrDefault(s => s.Value.Name == AudioSyncWorker.CurrentSongName).Value;
		if (currentAudio == null || BetterBugleModule.CurrentAudioSource.clip == null) return;

		float MAX_WIDTH = Screen.width - (offsetX * 2);

		float audioLength = currentAudio.AudioClip.length;
		float progress = BetterBugleModule.IsPlaying ? BetterBugleModule.CurrentAudioSource.time / audioLength : 0f;

		// Progress bar: left to right, top right corner, with margin
		float margin = 32f;
		float barHeight = 18f;
		float barWidth = Mathf.Min(400f, MAX_WIDTH * 0.5f); // reasonable max width
		float barX = Screen.width - barWidth - margin;
		float barY = margin;

		// Draw background bar
		Rect barRect = new Rect(barX, barY, barWidth, barHeight);
		GUI.color = new Color(0f, 0f, 0f, 0.5f);
		GUI.DrawTexture(barRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, false);

		// Draw progress circle (left to right)
		float circleRadius = barHeight * 0.8f * 0.5f;
		float circleCenterX = barX + barWidth * progress;
		float circleCenterY = barY + barHeight / 2f;
		GUI.color = Color.white;
		GUI.DrawTexture(new Rect(circleCenterX - circleRadius, circleCenterY - circleRadius, circleRadius * 2, circleRadius * 2), Texture2D.whiteTexture, ScaleMode.StretchToFill, true);

		// Draw progress text under the bar, centered relative to the bar
		float textY = barY + barHeight + 4f;
		string FormatTime(float t)
		{
			int minutes = (int)t / 60;
			float seconds = t % 60f;
			return $"{minutes:00}:{seconds,5:00.00}";
		}
		string progressText = $"{FormatTime(BetterBugleModule.CurrentAudioSource.time)} - {FormatTime(audioLength)}";
		GUIStyle textStyle = new GUIStyle(GUI.skin.label)
		{
			alignment = TextAnchor.UpperCenter,
			fontSize = 16,
			normal = { textColor = Color.white }
		};
		// Center the text horizontally relative to the bar
		GUI.Label(new Rect(barX, textY, barWidth, 22f), progressText, textStyle);

		// Reset color
		GUI.color = Color.white;
	}
}
