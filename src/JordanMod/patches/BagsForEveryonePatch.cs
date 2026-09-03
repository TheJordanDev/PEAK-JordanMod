using System;
using System.Collections.Generic;
using HarmonyLib;
using Peak;
using Photon.Pun;
using UnityEngine;

namespace JordanMod.Modules.BagsForEveryone;

public class BagsForEveryonePatch
{

	private const string BackpackSpawnerName = "Backpack_Spawner";
	private const string FirstBiomeName = "Biome_1";

	[HarmonyPatch(typeof(SingleItemSpawner), "TrySpawnItems")]
	[HarmonyPostfix]
	static void Postfix(SingleItemSpawner __instance, List<PhotonView> __result)
	{
		if (!Helper.IsMasterClient()) return;
		if (__instance.transform.name != BackpackSpawnerName) return;

		GameObject firstBiome = GameObject.Find(FirstBiomeName);
		if (firstBiome == null || !__instance.transform.IsChildOf(firstBiome.transform)) return;
		if (__result != null && __result.Count > 1) return;

		int extraBags = PhotonNetwork.PlayerList.Length - 1;
		if (extraBags <= 0) return;

		try
		{
			SpawnExtraBags(__instance, extraBags);
		}
		catch (Exception e)
		{
			Plugin.Log.LogError($"Failed to spawn extra backpacks, continuing without them: {e}");
		}
	}

	private static void SpawnExtraBags(SingleItemSpawner spawner, int count)
	{
		List<PhotonView> spawned = new(count);
		for (int i = 1; i <= count; i++)
		{
			Vector3 spawnPosition = spawner.transform.position + Vector3.up * 0.1f + Vector3.right * i;
			PhotonView view = PhotonNetwork.InstantiateItemRoom(spawner.prefab.name, spawnPosition, spawner.transform.rotation).GetComponent<PhotonView>();
			if (spawner.isKinematic)
			{
				view.RPC("SetKinematicRPC", RpcTarget.AllBuffered, true, view.transform.position, view.transform.rotation);
			}
			spawned.Add(view);
		}

		if (spawner.HasSpawnTracking(out SpawnedItemTracker tracker)) tracker.TrackSpawnedItems(spawned);
	}

}
