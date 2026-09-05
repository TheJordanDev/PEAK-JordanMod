using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace JordanMod;

public static class Helper
{
	public static ItemDatabase ItemDatabase { get; private set; } = SingletonAsset<ItemDatabase>.Instance;

	public static Item FindItemByName(string itemName, out Item? item)
	{
		item = ItemDatabase.Objects.Find(item => item.name.Equals(itemName, System.StringComparison.OrdinalIgnoreCase));
		return item;
	}

	/// <summary>
	/// Config URLs get typed by hand, so a missing scheme is common. Adds one when there isn't
	/// already a scheme, picking the insecure variant for loopback since a local dev server
	/// almost never has TLS in front of it. Call as EnsureScheme(url, "https", "http") or
	/// EnsureScheme(url, "wss", "ws").
	/// </summary>
	public static string EnsureScheme(string? url, string secureScheme, string insecureScheme)
	{
		string trimmed = (url ?? "").Trim();
		if (trimmed.Length == 0) return "";
		if (trimmed.Contains("://")) return trimmed;

		bool loopback = trimmed.StartsWith("localhost", System.StringComparison.OrdinalIgnoreCase)
			|| trimmed.StartsWith("127.0.0.1", System.StringComparison.Ordinal)
			|| trimmed.StartsWith("[::1]", System.StringComparison.Ordinal);

		return (loopback ? insecureScheme : secureScheme) + "://" + trimmed;
	}

	public static bool IsOnIsland()
	{
		return SceneManager.GetActiveScene().name.ToLower().StartsWith("level_") || SceneManager.GetActiveScene().name == "WilIsland";
	}

	public static float MouseScrollDelta()
	{
		return Input.mouseScrollDelta.y;
	}

	public static bool IsMasterClient()
	{
		return PhotonNetwork.IsConnected && PhotonNetwork.IsMasterClient;
	}

	public static void LogToPlayer(string _message)
	{
		string message;
		if (_message.Contains("<color")) message = _message;
		else message = $"<color=white>{_message}</color>";

		Object.FindAnyObjectByType<PlayerConnectionLog>()?.SendMessage(message);
	}

	public static IEnumerator WaitUntilPlayerHasCharacter(Photon.Realtime.Player player, System.Action<Character?> onComplete, int maxTries = 30, float waitTime = 1f)
	{
		int tries = 0;
		Character? character = null;
		while (character == null && tries < maxTries)
		{
			character = PlayerHandler.GetPlayerCharacter(player);
			if (character == null)
			{
				tries++;
				yield return new WaitForSeconds(waitTime);
			}
		}
		onComplete?.Invoke(character);
	}

}
