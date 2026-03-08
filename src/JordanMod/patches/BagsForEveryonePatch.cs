using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace JordanMod.Modules.BagsForEveryone;

public class BagsForEveryonePatch
{

	[HarmonyPatch(typeof(SingleItemSpawner), "TrySpawnItems")]
    [HarmonyPostfix]
    static void Postfix(SingleItemSpawner __instance)
    {
		ForEachPlayer(__instance);
	}

	private static void ForEachPlayer(SingleItemSpawner spawner)
	{
		if (!Helper.IsMasterClient()) return;

		bool isBackpackSPawner = spawner.transform.name == "Backpack_Spawner";
		if (!isBackpackSPawner) return;
		
		bool isFirstSpawner = spawner.transform.IsChildOf(GameObject.Find("Biome_1").transform);
		if (!isFirstSpawner) return;

		int playerCount = PhotonNetwork.PlayerList.Length - 1;
		if (playerCount <= 0) return;
		for (int i = 1; i <= playerCount; i++)
		{
			Vector3 spawnPosition = spawner.transform.position + Vector3.up * 0.1f + Vector3.right * i;
			PhotonView component = PhotonNetwork.InstantiateItemRoom(spawner.prefab.name, spawnPosition, spawner.transform.rotation).GetComponent<PhotonView>();
			if (spawner.isKinematic)
			{
				component.GetComponent<PhotonView>().RPC("SetKinematicRPC", RpcTarget.AllBuffered, true, component.transform.position, component.transform.rotation);
			}
		}
	}

}