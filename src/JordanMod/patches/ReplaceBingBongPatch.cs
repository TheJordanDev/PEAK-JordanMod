using System;
using System.Collections.Generic;
using HarmonyLib;
using JordanMod.Utils;
using UnityEngine;

namespace JordanMod.Modules.ReplaceBingBong;

public class ReplaceBingBongPatch
{

	[HarmonyPatch(typeof(ItemActionBase), "OnEnable")]
	[HarmonyPrefix]
	static bool PreActionAskBingBongConstructorFix(ItemActionBase __instance)
	{
		if (__instance is not Action_AskBingBong askBingBong)
			return true;
		
		Action_AskBingBong.BingBongResponse[] currentResponses = [..askBingBong.responses];

		if (!ReplaceBingBongModule.HasReplacedSounds)
		{
			ReplaceBingBongModule.OriginalResponsesData = new BingBongResponseData[currentResponses.Length];
			for (int index = 0; index < currentResponses.Length; index++)
			{
				ReplaceBingBongModule.OriginalResponsesData[index] = BingBongResponseData.FromBingBongResponse(currentResponses[index]);
			}
			ReplaceBingBongModule.HasReplacedSounds = true;	
		}

		ReplaceBingBongModule.ReplaceBingBongResponses(askBingBong);
		return true;
	}

}