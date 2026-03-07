using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace JordanMod.Modules.OpenMesa;

class OpenMesaPatch
{
	[HarmonyPatch(typeof(RunManager), nameof(RunManager.StartRun))]
	[HarmonyPostfix]
	public static void PostFixStartRun(RunManager __instance)
	{
		int seed = SceneManager.GetActiveScene().path.GetHashCode();
		System.Random prng = new(seed);

		var desertRockSpawners = Object.FindObjectsByType<DesertRockSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
		foreach (var spawner in desertRockSpawners)
		{
			spawner.GetRefs();
			var entrance = spawner.enterences;
			var inside = spawner.inside;

			bool hasEntrance = entrance.Cast<Transform>()
				.SelectMany(container => container.Cast<Transform>())
				.Any(doorObject =>
					spawner.enterenceObjects.Any(e => e.name == doorObject.name) &&
					(!spawner.blockerObjects.Any(b => b.name == doorObject.name) ||
					 !doorObject.Cast<Transform>().Any(child => child.TryGetComponent<LODGroup>(out _)))
				);

			if (hasEntrance) continue;

			var targetEntranceContainer = entrance.GetChild(prng.Next(0, entrance.childCount));

			for (int i = targetEntranceContainer.childCount - 1; i >= 0; i--)
				Object.DestroyImmediate(targetEntranceContainer.GetChild(i).gameObject);

			var prefab = spawner.enterenceObjects[prng.Next(0, spawner.enterenceObjects.Length)];
			HelperFunctions.InstantiatePrefab(prefab, targetEntranceContainer.position, targetEntranceContainer.rotation, targetEntranceContainer)
				.transform.localScale = Vector3.one * 2f;

			inside.position = new Vector3(targetEntranceContainer.position.x, inside.position.y, targetEntranceContainer.position.z);

			var mazeTransform = inside.FindChildRecursive("Maze");
			if (mazeTransform != null)
			{
				// Disable Maze itself
				mazeTransform.gameObject.SetActive(false);
				mazeTransform.Find("Rocks")?.gameObject.SetActive(false);
				mazeTransform.Find("Roof")?.gameObject.SetActive(false);
				mazeTransform.Find("Floor")?.gameObject.SetActive(false);
			}
		}

	}
}