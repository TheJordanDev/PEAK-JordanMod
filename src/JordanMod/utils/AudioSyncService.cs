using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using JordanMod.Modules.BetterBugle;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace JordanMod.Utils;

class AudioSyncService
{
	// Normalised once here rather than at each call site: the configured value is hand-typed, so
	// it may be missing a scheme or carry a trailing slash, either of which broke the Uri parse.
	public static string API_BASE_URL => Helper.EnsureScheme(ConfigHandler.BugleSoundAPIURL.Value, "https", "http").TrimEnd('/');

	public static bool DownloadAPIAudio(APIAudioFormat apiAudio, string SoundsDirectory, Song? existingSong = null)
	{
		bool success = true;
		try
		{
			// Same audio under a different name in the bank: retire the local copy.
			if (existingSong != null && apiAudio.Filename != existingSong.Name)
			{
				string renamedFrom = Path.Combine(SoundsDirectory, $"{existingSong.Name}.{existingSong.Extension}");
				if (File.Exists(renamedFrom)) AudioSyncWorker.TryQuarantine(renamedFrom);
			}

			// Same name under a different extension -- a leftover .mp3 when the bank now serves
			// .ogg, say. Both would be queued by the loader and fight over the same song name, so
			// retire every variant that isn't the one we're about to write.
			foreach (string ext in AudioSyncWorker.AudioTypes.Keys)
			{
				if (string.Equals(ext, apiAudio.Extension, StringComparison.OrdinalIgnoreCase)) continue;
				string stale = Path.Combine(SoundsDirectory, $"{apiAudio.Filename}.{ext}");
				if (File.Exists(stale)) AudioSyncWorker.TryQuarantine(stale);
			}

			apiAudio.DownloadToFolder(SoundsDirectory);
		}
		catch (Exception ex)
		{
			Debug.LogError($"Failed to download API audio: {ex.Message}");
			success = false;
		}
		return success;
	}

	public static List<APIAudioFormat> GetAudioClips()
	{
		List<APIAudioFormat> audioClips = [];

		Uri? uri = Uri.TryCreate($"{API_BASE_URL}/audio/list", UriKind.Absolute, out var result) ? result : null;
		if (uri == null)
		{
			Debug.LogError("Invalid BugleSoundAPIURL in config.");
			return audioClips;
		}

		using var client = new WebClient();
		try
		{
			string json = client.DownloadString(uri);
			var data = JsonConvert.DeserializeObject<List<APIAudioFormat>>(json);
			if (data == null) return audioClips;
			audioClips.AddRange(data);
		}
		catch (Exception ex)
		{
			Debug.LogError($"Failed to fetch or parse audio clip hashes: {ex.Message}");
		}
		return audioClips;
	}

	public static void ClearAudioClips()
	{
		// One scene scan shared by every song. Song.Dispose() used to run its own
		// FindObjectsByType<AudioSource>() call, so clearing a bank of N songs meant N full
		// scans of the scene -- the bulk of the freeze on the Bugle's right-click refresh.
		AudioSource[] audioSources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
		foreach (Song song in Song.Sounds.Values.ToList())
		{
			song.Dispose(audioSources);
		}
		Song.Sounds.Clear();
		Song.Songs.Clear();
		Song.BB_VoiceLines.Clear();
		// No GC.Collect() here on purpose: the audio memory is native and is already released by
		// Object.Destroy on each clip, so a blocking full collection only added a stall.
	}

	public class APIAudioFormat
	{
		[JsonProperty("_id")]
		public string Id { get; set; } = string.Empty;

		[JsonProperty("filename")]
		public string Filename { get; set; } = string.Empty;

		[JsonProperty("extension")]
		public string Extension { get; set; } = string.Empty;

		[JsonProperty("size")]
		public long Size { get; set; }

		[JsonProperty("hash")]
		public string Hash { get; set; } = string.Empty;

		[JsonProperty("created_at")]
		public DateTime CreatedAt { get; set; }

		[JsonProperty("modified_at")]
		public DateTime ModifiedAt { get; set; }

		[JsonProperty("owner")]
		public string Owner { get; set; } = string.Empty;

		// Plain WebClient rather than UnityWebRequest: this runs on a background thread, and
		// UnityWebRequest is main-thread only. The old version also spun on `await Task.Yield()`
		// waiting for an operation that a pool thread never drives, burning a core.
		public void DownloadToFolder(string folderPath)
		{
			if (string.IsNullOrEmpty(Filename) || string.IsNullOrEmpty(Extension))
			{
				Debug.LogError("Invalid audio file information.");
				return;
			}
			string filePath = Path.Combine(folderPath, $"{Filename}.{Extension}");

			// Ensure the directory exists
			Directory.CreateDirectory(folderPath);

			if (File.Exists(filePath)) File.Delete(filePath);

			string url = $"{API_BASE_URL}/audio/{Id}/download?hash={Hash}";
			Debug.Log($"Downloading audio from URL: {url}");

			using var client = new WebClient();
			byte[] data = client.DownloadData(url);
			File.WriteAllBytes(filePath, data);
		}
	}

}

class AudioSyncWorker
{

	private static AudioSyncWorker? Instance { get; set; }
	public static AudioSyncWorker GetInstance()
	{
		Instance ??= new AudioSyncWorker();
		return Instance;
	}

	public static readonly string SoundsDirectory = Path.Combine(BepInEx.Paths.BepInExRootPath, "bugleSounds");
	// Files the sync pulls out of the bank folder are moved here rather than deleted. It's a
	// subfolder of the bank itself, which is safe because the loader's Directory.GetFiles calls
	// are non-recursive and will never see it.
	public static readonly string QuarantineDirectory = Path.Combine(SoundsDirectory, "_removed");

	public static readonly Dictionary<string, AudioType> AudioTypes = new()
	{
		{ "wav", AudioType.WAV },
		{ "mp3", AudioType.MPEG },
		{ "ogg", AudioType.OGGVORBIS },
		{ "aiff", AudioType.AIFF },
	};

	public static bool IsLoading = false;
	public static bool IsSyncing = false;

	// How many clips arrived from the download handler without their audio data decoded. Any
	// number above zero means the first play of each would otherwise have stalled the main
	// thread; reported once per load so the log can confirm it.
	public static int ClipsNeedingPreload = 0;

	public static int CurrentSongIndex = 0;
	public static string CurrentSongName = "None";

	public static Action? OnAudioLoadComplete;

	public static void GetAudioClips()
	{
		if (IsLoading || IsSyncing) return;
		if (!Directory.Exists(SoundsDirectory)) return;
		IsLoading = true;
		Plugin.Instance.StartCoroutine(LoadAllAudioClipsCoroutine(SoundsDirectory));
	}

	public static void TrySyncAndLoadAudioClips()
	{
		if (IsLoading || IsSyncing) return;
		Task.Run(SyncAndLoadAudioClips);
	}

	// Moves a file out of the bank folder instead of deleting it, so a mistaken sync is always
	// recoverable. Pure file I/O, safe to call from the sync thread.
	internal static bool TryQuarantine(string filePath)
	{
		try
		{
			Directory.CreateDirectory(QuarantineDirectory);
			string target = Path.Combine(QuarantineDirectory, Path.GetFileName(filePath));
			if (File.Exists(target))
			{
				string stem = Path.GetFileNameWithoutExtension(filePath);
				string ext = Path.GetExtension(filePath);
				target = Path.Combine(QuarantineDirectory, $"{stem}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
				if (File.Exists(target)) File.Delete(target);
			}
			File.Move(filePath, target);
			Debug.Log($"Moved '{Path.GetFileName(filePath)}' out of the bank folder into _removed");
			return true;
		}
		catch (Exception ex)
		{
			Debug.LogError($"Could not move '{filePath}' out of the sounds folder: {ex.Message}");
			return false;
		}
	}

	// Everything in the folder that the bank doesn't list gets pulled out, so every player ends
	// up with an identical set. BingBong voice lines are exempt.
	private static int QuarantineFilesNotInBank(HashSet<string> bankNames)
	{
		Dictionary<string, Song> loaded = new(Song.Sounds);
		int moved = 0;
		foreach (string ext in AudioTypes.Keys)
		{
			foreach (string file in Directory.GetFiles(SoundsDirectory, $"*.{ext}"))
			{
				string name = Path.GetFileNameWithoutExtension(file);
				if (Song.IsBingBongVoiceLine(name)) continue;
				if (bankNames.Contains(name)) continue;
				if (!TryQuarantine(file)) continue;
				moved++;
				if (loaded.TryGetValue(name, out Song? song)) Plugin.RunOnMainThread(song.Dispose);
			}
		}
		return moved;
	}

	// ShowActionbar reads Time.time and touches a MonoBehaviour, so it can't be called straight
	// from the sync thread.
	private static void ShowActionbarOnMainThread(string message)
	{
		Plugin.RunOnMainThread(() => BetterBugleUI.Instance?.ShowActionbar(message));
	}

	private static IEnumerator LoadAllAudioClipsCoroutine(string directoryPath, string[]? forceReload = null)
	{
		ClipsNeedingPreload = 0;
		List<(string filePath, string ext, string name)> filesToLoad = new();

		foreach (var ext in AudioTypes.Keys)
		{
			var files = Directory.GetFiles(directoryPath, $"*.{ext}");
			foreach (var file in files)
			{
				string name = Path.GetFileNameWithoutExtension(file);
    			bool shouldForceReload = forceReload != null && forceReload.Contains($"{name}.{ext}");
				if (!Song.Sounds.ContainsKey(name) || shouldForceReload)
				{
					filesToLoad.Add((file, ext, name));
				}
			}
		}

		const int BATCH_SIZE = 2;
		int loadedCount = 0;

		for (int i = 0; i < filesToLoad.Count; i += BATCH_SIZE)
		{
			List<Coroutine> loadCoroutines = [];

			for (int j = i; j < i + Math.Min(BATCH_SIZE, filesToLoad.Count - i) && j < filesToLoad.Count; j++)
			{
				var (filePath, ext, name) = filesToLoad[j];
				bool forceReloadClip = forceReload != null && forceReload.Contains($"{name}.{ext}");
				Coroutine loadCoroutine = Plugin.Instance.StartCoroutine(LoadAudioClipCoroutine(filePath, ext, name, forceReloadClip));
				loadCoroutines.Add(loadCoroutine);
			}

			foreach (var coroutine in loadCoroutines) yield return coroutine;
			loadedCount += loadCoroutines.Count;
			BetterBugleUI.Instance?.ShowActionbar($"Loading audio clips... {loadedCount}/{filesToLoad.Count}");
		}

		if (ClipsNeedingPreload > 0)
			Debug.Log($"Pre-decoded {ClipsNeedingPreload}/{filesToLoad.Count} clips that would have stalled the main thread on first play.");

		// Clear the flag before notifying: a subscriber that throws must not leave IsLoading
		// stuck true, which would wedge every sync and refresh for the rest of the session.
		IsLoading = false;
		try
		{
			OnAudioLoadComplete?.Invoke();
		}
		catch (Exception ex)
		{
			Debug.LogError($"An audio load handler threw: {ex}");
		}
	}

	private static IEnumerator LoadAudioClipCoroutine(string filePath, string ext, string name, bool forceReload = false)
	{
		using UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip($"file://{filePath}", AudioTypes[ext]);
		// Keep the clip compressed in memory and let Unity decode it during playback. The default
		// (DecompressOnLoad) holds raw PCM for the whole session -- roughly 10 MB per minute of
		// 44.1kHz stereo, so a ~80 MB bank becomes ~1 GB resident. Compressed costs a little CPU
		// per playing voice, which is nothing when one bugle is sounding, and avoids both the
		// memory and the big upfront decode. Must be set before SendWebRequest.
		if (www.downloadHandler is DownloadHandlerAudioClip audioHandler) audioHandler.compressed = true;
		yield return www.SendWebRequest();

		if (www.result != UnityWebRequest.Result.Success) yield break;

		bool songExists = Song.Sounds.ContainsKey(name);

		if (songExists && !forceReload) yield break;

		if (songExists && forceReload)
		{
			Song? previousSong = Song.Sounds.TryGetValue(name, out var existingSong) ? existingSong : null;
			previousSong?.Dispose();
		}

		AudioClip audioClip = DownloadHandlerAudioClip.GetContent(www);
		if (audioClip == null) yield break;

		// GetContent hands back a DecompressOnLoad clip whose PCM data is decoded lazily, on
		// first use. Left alone that decode happens inside the first AudioSource.Play(), on the
		// main thread, freezing the game the first time anyone plays a song. Force it here
		// instead: we're already on the loading path, where a stall is expected and we can yield
		// through it. If the clip already arrived loaded this is a no-op.
		if (audioClip.loadState != AudioDataLoadState.Loaded)
		{
			ClipsNeedingPreload++;
			// loadInBackground is serialized on the asset and read-only at runtime, so this
			// decodes synchronously. That's the point: the cost lands here rather than mid-game,
			// and the batching in LoadAllAudioClipsCoroutine already yields between clips.
			audioClip.LoadAudioData();
			while (audioClip.loadState == AudioDataLoadState.Loading) yield return null;
		}

		Song song = new(name, ext, filePath, audioClip);
		song.Register();
	}

	// Runs on a thread pool thread via Task.Run. Everything in here must stay off Unity APIs;
	// hop back through Plugin.RunOnMainThread for anything that touches the engine.
	private static void SyncAndLoadAudioClips()
	{
		if (IsLoading || IsSyncing) return;
		IsSyncing = true;
		try
		{
			Dictionary<AudioSyncService.APIAudioFormat, Song?> toDownload = new();

			AudioSyncService.APIAudioFormat[] existingAPIFormats = [.. AudioSyncService.GetAudioClips()];

			// A failed fetch (bad URL, offline, server error) also returns an empty list, and an
			// empty bank would mean "nothing belongs here" -- quarantining the entire folder. Only
			// a bank that actually listed something is allowed to drive removals.
			if (existingAPIFormats.Length == 0)
			{
				Debug.LogWarning("Audio bank returned no entries; cancelling sync rather than emptying the folder.");
				ShowActionbarOnMainThread("Audio bank unreachable or empty, sync cancelled.");
				IsSyncing = false;
				return;
			}

			HashSet<string> bankNames = new(existingAPIFormats.Select(apiAudio => apiAudio.Filename), StringComparer.OrdinalIgnoreCase);
			int quarantined = QuarantineFilesNotInBank(bankNames);

			// Hashing reads the whole file off disk, so build the lookup here on the background
			// thread rather than eagerly during loading where it stalled the main thread once
			// per clip.
			Dictionary<string, Song> soundsByHash = new();
			foreach (Song song in Song.Sounds.Values.ToList())
			{
				// Skip anything just quarantined: its file has moved, so hashing would only fail
				// and log noise. Its Dispose is already queued on the main thread.
				if (!File.Exists(song.FilePath)) continue;
				string hash = song.Hash;
				if (!string.IsNullOrEmpty(hash)) soundsByHash[hash] = song;
			}

			foreach (AudioSyncService.APIAudioFormat apiAudio in existingAPIFormats)
			{
				Song? existingSong = soundsByHash.GetValueOrDefault(apiAudio.Hash);
				if (existingSong == null || existingSong.Hash != apiAudio.Hash)
				{
					toDownload.Add(apiAudio, existingSong);
				}
			}

			string quarantineNote = quarantined > 0 ? $", {quarantined} moved to _removed" : "";
			ShowActionbarOnMainThread($"Syncing audio bank... {toDownload.Count} changed/new files found{quarantineNote}.");

			string[] filesToOverload = [];

			foreach (AudioSyncService.APIAudioFormat apiAudio in toDownload.Keys)
			{
				bool success = AudioSyncService.DownloadAPIAudio(apiAudio, SoundsDirectory, toDownload[apiAudio]);
				if (success)
				{
					Debug.Log($"Successfully downloaded audio: {apiAudio.Filename}.{apiAudio.Extension}, adding to forceload");
					filesToOverload = [.. filesToOverload, $"{apiAudio.Filename}.{apiAudio.Extension}"];
				}
			}
			IsSyncing = false;
			IsLoading = true;
			// StartCoroutine is a Unity API - must run on the main thread, not on this Task.Run background thread.
			Plugin.RunOnMainThread(() => Plugin.Instance.StartCoroutine(LoadAllAudioClipsCoroutine(SoundsDirectory, filesToOverload)));
		}
		catch (Exception ex)
		{
			Debug.LogError($"Audio sync failed: {ex}");
			IsSyncing = false;
			IsLoading = false;
		}
	}
}

public class Song : IDisposable
{
	public static readonly Dictionary<string, Song> Sounds = new();

	public static readonly Dictionary<string, Song> Songs = new();
	public static readonly Dictionary<string, Song> BB_VoiceLines = new();

	public static List<string> FavoriteSongs = new();

	public static List<string> GetSongNames_Alphabetically()
	{
		return [.. new List<string>(Songs.Keys)
			.OrderByDescending(FavoriteSongs.Contains)
			.ThenBy(name => name)];
	}

	public static bool IsBingBongVoiceLine(string name) => name.StartsWith(BingBongPrefix, StringComparison.Ordinal);

	public static void UpdateRealIndices()
	{
		var sortedNames = Songs.Keys.OrderBy(name => name).ToList();
		for (int i = 0; i < sortedNames.Count; i++)
		{
			if (Songs.TryGetValue(sortedNames[i], out var song))
			{
				song.RealIndex = i + 1;
			}
		}
	}

	// Voice lines are personal, not part of the shared bank, so the sync never touches them.
	public const string BingBongPrefix = "SFX_VO_BingBong_";

	public string Name { get; set; }
	public string Extension { get; set; }
	public string FilePath { get; set; }
	public AudioClip AudioClip { get; }
	public int RealIndex { get; set; }

	private string? _hash;

	// Computed on demand rather than in the constructor: hashing reads the entire file and runs
	// SHA256 over it, and the constructor is called from the load coroutine on the main thread.
	// The only consumer is the sync comparison, which already runs on a background thread.
	public string Hash => _hash ??= GenerateHash(FilePath);

	public Song(string name, string extension, string filePath, AudioClip audioClip)
	{
		Name = name;
		Extension = extension;
		FilePath = filePath;
		AudioClip = audioClip;
	}

	public void Register()
	{
		Sounds[Name] = this;
		if (IsBingBongVoiceLine(Name)) BB_VoiceLines[Name] = this;
		else Songs[Name] = this;
	}

	public void Dispose() => Dispose(null);

	// knownAudioSources lets a bulk clear pass in a single scene scan instead of paying for one
	// per song.
	public void Dispose(AudioSource[]? knownAudioSources)
	{
		if (AudioClip == null) return;
		var audioSources = knownAudioSources ?? UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
		foreach (var audioSource in audioSources)
		{
			if (audioSource != null && audioSource.clip == AudioClip)
			{
				audioSource.Stop();
				audioSource.clip = null;
			}
		}
		Sounds.Remove(Name);
		if (IsBingBongVoiceLine(Name)) BB_VoiceLines.Remove(Name);
		else Songs.Remove(Name);
		UnityEngine.Object.Destroy(AudioClip);
	}


	public string GenerateHash(string filePath)
	{
		try
		{
			using var hasher = SHA256.Create();
			var fileBytes = File.ReadAllBytes(filePath);
			var hashBytes = hasher.ComputeHash(fileBytes);
			return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
		}
		catch (Exception ex)
		{
			// A missing or locked file just means "no match", which makes the sync re-download it.
			Debug.LogWarning($"Could not hash '{filePath}': {ex.Message}");
			return string.Empty;
		}
	}
}
