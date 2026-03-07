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
		// Each response has a .sfx which has a Object.name, store a ref to the sfx with key being sfx name
		Dictionary<string, SFX_Instance> sfxDict = new();
		for (int i = 0; i < currentResponses.Length; i++)
		{
			Action_AskBingBong.BingBongResponse response = currentResponses[i];
			if (response.sfx != null && response.sfx.clips != null && response.sfx.clips.Length > 0)
			{
				foreach (AudioClip clip in response.sfx.clips)
				{
					sfxDict[response.sfx.name] = response.sfx;
				}
			}
		}

		List<Song> voices = [.. Song.BB_VoiceLines.Values];

		foreach (Song voice in voices)
		{
			AudioClip clip = voice.AudioClip;

			bool isNew = !sfxDict.ContainsKey(voice.Name);
			if (isNew)
			{
				SFX_Instance sFX_Instance = new()
				{
					clips = [clip]
				};
				Action_AskBingBong.BingBongResponse newResponse = new()
				{
					sfx = sFX_Instance,
					subtitleID = "idk_funny",
					mouthCurve = null,
					mouthCurveTime = 1f
				};
				currentResponses = [.. currentResponses, newResponse];
			} 
			else
			{
				sfxDict[voice.Name].clips = [clip];
			}
		}

		askBingBong.responses = new Action_AskBingBong.BingBongResponse[currentResponses.Length];
		for (int i = 0; i < currentResponses.Length; i++)
		{
			askBingBong.responses[i] = currentResponses[i];
		}
		
		return true;
	}

}