using HarmonyLib;

namespace JordanMod.Patches;

public class PassportPatch
{
	[HarmonyPatch(typeof(Item), "Start")]
	[HarmonyPostfix]
	static void PostItemStartFix(Item __instance)
	{
		if (__instance.UIData.itemName.ToLower() == "passport")
		{
			__instance.UIData.canDrop = true;
			__instance.UIData.canThrow = true;
		}
	}
}