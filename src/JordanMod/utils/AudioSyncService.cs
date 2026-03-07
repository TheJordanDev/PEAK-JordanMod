using System;
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

	private static AudioSyncService? Instance { get; set; }
	public static AudioSyncService GetInstance()
	{
		Instance ??= new AudioSyncService();
		return Instance;
	}

	public List<APIAudioFormat> GetAudioClips()
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
		var filePath = Path.Combine(BetterBugleModule.SoundsDirectory, $"{Name}.{Extension}");
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