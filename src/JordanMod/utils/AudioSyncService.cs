using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using JordanMod.Modules.BetterBugle;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace JordanMod.Utils;

class AudioSyncService
{
	public static string API_BASE_URL => ConfigHandler.BugleSoundAPIURL.Value;

	public async static Task<bool> DownloadAPIAudio(APIAudioFormat apiAudio, string SoundsDirectory, Song? existingSong = null)
	{
		bool success = true;
		try
		{
			if (existingSong != null && apiAudio.Filename != existingSong.Name)
			{
				File.Delete(Path.Combine(SoundsDirectory, $"{existingSong.Name}.{existingSong.Extension}"));
			}
			await apiAudio.DownloadToFolder(SoundsDirectory);
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

		using var client = new System.Net.WebClient();
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
		foreach (Song song in Song.Sounds.Values.ToList())
		{
			song.Dispose();
		}
		Song.Sounds.Clear();
		Song.SoundsByHash.Clear();
		Song.Songs.Clear();
		Song.BB_VoiceLines.Clear();
		GC.Collect();
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

		public async Task DownloadToFolder(string folderPath)
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
			Debug.LogError($"Downloading audio from URL: {url}");

			using UnityWebRequest www = UnityWebRequest.Get(url);
			var operation = www.SendWebRequest();
			while (!operation.isDone) await Task.Yield();
			if (www.result != UnityWebRequest.Result.Success)
			{
				Debug.LogError($"Failed to download API audio: {www.error}");
				return;
			}
			File.WriteAllBytes(filePath, www.downloadHandler.data);
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
	public static readonly Dictionary<string, AudioType> AudioTypes = new()
	{
		{ "wav", AudioType.WAV },
		{ "mp3", AudioType.MPEG },
		{ "ogg", AudioType.OGGVORBIS },
		{ "aiff", AudioType.AIFF },
	};

	public static bool IsLoading = false;
	public static bool IsSyncing = false;

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
		Task.Run(() =>
		{
			SyncAndLoadAudioClipsCoroutine().GetAwaiter().GetResult();
		});
	}

	private static IEnumerator LoadAllAudioClipsCoroutine(string directoryPath, string[]? forceReload = null)
	{
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
		OnAudioLoadComplete?.Invoke();
		IsLoading = false;
	}

	private static IEnumerator LoadAudioClipCoroutine(string filePath, string ext, string name, bool forceReload = false)
	{
		using UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip($"file://{filePath}", AudioTypes[ext]);
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

		Song song = new(name, ext, filePath, audioClip);
		song.Register();
	}
	
	private static async Task SyncAndLoadAudioClipsCoroutine()
	{
		if (IsLoading || IsSyncing) return;
		IsSyncing = true;
		Dictionary<AudioSyncService.APIAudioFormat, Song?> toDownload = new();

		string[] existingSongNames = [.. Song.Sounds.Keys];
		AudioSyncService.APIAudioFormat[] existingAPIFormats = [.. AudioSyncService.GetAudioClips()];
		string[] apiExistingNames = [.. existingAPIFormats.Select(apiAudio => apiAudio.Filename)];

		var songsToRemove = existingSongNames.Except(apiExistingNames).ToArray();
		foreach (var songName in songsToRemove)
		{
			if (Song.Sounds.TryGetValue(songName, out var songToDispose))
			{
				songToDispose.Dispose();
				songToDispose.DeleteFile();
			}
		}


		foreach (AudioSyncService.APIAudioFormat apiAudio in existingAPIFormats)
		{
			Song? existingSong = Song.SoundsByHash.GetValueOrDefault(apiAudio.Hash);
			if (existingSong == null || existingSong.Hash != apiAudio.Hash)
			{
				toDownload.Add(apiAudio, existingSong);
			}
		}

		BetterBugleUI.Instance?.ShowActionbar($"Syncing audio bank... {toDownload.Count} changed/new files found.");

		string[] filesToOverload = [];

		foreach (AudioSyncService.APIAudioFormat apiAudio in toDownload.Keys)
		{
			bool success = await AudioSyncService.DownloadAPIAudio(apiAudio, SoundsDirectory, toDownload[apiAudio]);
			if (success)
			{
				Debug.Log($"Successfully downloaded audio: {apiAudio.Filename}.{apiAudio.Extension}, adding to forceload");
				filesToOverload = [.. filesToOverload, $"{apiAudio.Filename}.{apiAudio.Extension}"];
			}
		}
		IsSyncing = false;
		IsLoading = true;
		Plugin.Instance.StartCoroutine(LoadAllAudioClipsCoroutine(SoundsDirectory, filesToOverload));
	}
}

public class Song : IDisposable
{
	public static readonly Dictionary<string, Song> Sounds = new();
	public static readonly Dictionary<string, Song> SoundsByHash = new();

	public static readonly Dictionary<string, Song> Songs = new();
	public static readonly Dictionary<string, Song> BB_VoiceLines = new();

	public static List<string> FavoriteSongs = new();

	public static List<string> GetSongNames_Alphabetically()
	{
		return [.. new List<string>(Songs.Keys)
			.OrderByDescending(FavoriteSongs.Contains)
			.ThenBy(name => name)];
	}

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

	public string Name { get; set; }
	public string Extension { get; set; }
	public string FilePath { get; set; }
	public AudioClip AudioClip { get; }
	public string Hash { get; }
	public int RealIndex { get; set; }

	public Song(string name, string extension, string filePath, AudioClip audioClip)
	{
		Name = name;
		Extension = extension;
		FilePath = filePath;
		AudioClip = audioClip;
		Hash = GenerateHash(filePath);
	}

	public void Register()
	{
		Sounds[Name] = this;
		SoundsByHash[Hash] = this;
		if (Name.StartsWith("SFX_VO_BingBong_")) BB_VoiceLines[Name] = this;
		else Songs[Name] = this;
	}

	public void Dispose()
	{
		if (AudioClip == null) return;
		var audioSources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
		foreach (var audioSource in audioSources)
		{
			if (audioSource.clip == AudioClip)
			{
				audioSource.Stop();
				audioSource.clip = null;
			}
		}
		Sounds.Remove(Name);
		SoundsByHash.Remove(Hash);
		if (Name.StartsWith("SFX_VO_BingBong_")) BB_VoiceLines.Remove(Name);
		else Songs.Remove(Name);
		UnityEngine.Object.Destroy(AudioClip);
	}

	public void DeleteFile()
	{
		if (AudioClip == null) return;
		var filePath = Path.Combine(AudioSyncWorker.SoundsDirectory, $"{Name}.{Extension}");
		if (File.Exists(filePath))
		{
			File.Delete(filePath);
			Debug.Log($"Deleted local file: {filePath}");
		}
	}

	public string GenerateHash(string filePath)
	{
		using var hasher = SHA256.Create();
		var fileBytes = File.ReadAllBytes(filePath);
		var hashBytes = hasher.ComputeHash(fileBytes);
		return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
	}
}