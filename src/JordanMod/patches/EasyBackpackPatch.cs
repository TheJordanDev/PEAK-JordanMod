using HarmonyLib;
using UnityEngine;

namespace JordanMod.Modules.EasyBackpack;

class EasyBackpackPatch
{
	[HarmonyPatch(typeof(BackpackWheel), "Update")]
	[HarmonyPrefix]
	static bool BackpackWheelUpdatePrefix(BackpackWheel __instance)
	{
		bool isBackPackOpen = EasyBackpackModule.Instance?._isBackpackOpen ?? false;

		if (!Character.localCharacter.input.interactIsPressed && !isBackPackOpen)
		{
			__instance.Choose();
			GUIManager.instance.CloseBackpackWheel();
			return false;
		}

		if (__instance.backpack.locationTransform != null && Vector3.Distance(__instance.backpack.locationTransform.position, Character.localCharacter.Center) > 6f)
		{
			GUIManager.instance.CloseBackpackWheel();
			return false;
		}

		if (__instance.chosenSlice.IsSome && !__instance.chosenSlice.Value.isBackpackWear && !__instance.slices[__instance.chosenSlice.Value.slotID + 1].image.enabled)
		{
			__instance.currentlyHeldItem.transform.position = Vector3.Lerp(__instance.currentlyHeldItem.transform.position, __instance.slices[__instance.chosenSlice.Value.slotID + 1].transform.GetChild(0).GetChild(0).position, Time.deltaTime * 20f);
		}
		else
		{
			__instance.currentlyHeldItem.transform.localPosition = Vector3.Lerp(__instance.currentlyHeldItem.transform.localPosition, Vector3.zero, Time.deltaTime * 20f);
		}
		return false; 
	}

}