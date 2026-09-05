using System;
using System.Collections;
using System.Collections.Generic;
using JordanMod.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace JordanMod.Modules.ReplaceBingBong;

[Module(Enabled = true)]
class ReplaceBingBongModule : Module
{
	public override string ModuleName => "Replace BingBong Module";

	public const string BingBongSubtitleID = "IDK_FUNNY";

	public static bool HasReplacedSounds = false;
	public static BingBongResponseData[] OriginalResponsesData = [];
    
    public override Type[] GetPatches()
    {
        return [typeof(ReplaceBingBongPatch)];
    }

    public override void Initialize()
	{
		base.Initialize();
		// LocalizedText.GetText upper-cases the id before looking it up, so a lowercase key never
		// resolved at all. It also indexes the list by CURRENT_LANGUAGE, so a single-entry list
		// threw IndexOutOfRange (caught and logged by GetText) for every non-English player.
		List<string> subtitleLocalizations = new(LocalizedText.LANGUAGE_COUNT);
		for (int i = 0; i < LocalizedText.LANGUAGE_COUNT; i++) subtitleLocalizations.Add("Test subtitle!");
		LocalizedText.mainTable[BingBongSubtitleID] = subtitleLocalizations;
		AudioSyncWorker.OnAudioLoadComplete += OnAudioLoadComplete;
	}

	private static void OnAudioLoadComplete()
	{
		if (!HasReplacedSounds) return;
		RebuildResponsesForAll();
	}

	// Build the new set and hand it to every live instance before destroying the old one, so no
	// Action_AskBingBong is ever left pointing at a destroyed SFX_Instance.
	private static void RebuildResponsesForAll()
	{
		Action_AskBingBong.BingBongResponse[]? previous = _cachedResponses;
		_cachedResponses = BuildResponses();

		foreach (Action_AskBingBong askBingBong in UnityEngine.Object.FindObjectsByType<Action_AskBingBong>(FindObjectsSortMode.None))
		{
			askBingBong.responses = _cachedResponses;
		}

		if (previous == null) return;
		foreach (Action_AskBingBong.BingBongResponse response in previous)
		{
			if (response.sfx != null) UnityEngine.Object.Destroy(response.sfx);
		}
	}

	// SFX_Instance derives from ScriptableObject, which cannot be constructed with `new` --
	// doing so skips Unity's native-side init, so the object misbehaves and its overloaded
	// == null check reports oddly. CreateInstance is the only correct way to make one.
	public static SFX_Instance CreateSFX(string name, AudioClip[] clips)
	{
		SFX_Instance instance = ScriptableObject.CreateInstance<SFX_Instance>();
		instance.name = name;
		instance.clips = clips;
		return instance;
	}

	// Every Action_AskBingBong ends up with the same set -- it is derived purely from static
	// state (OriginalResponsesData plus the loaded voice lines). This used to be rebuilt on every
	// OnEnable, allocating a fresh SFX_Instance per response each time and never destroying the
	// previous ones, so BingBong items cycling in and out of scope leaked steadily.
	public static void ReplaceBingBongResponses(Action_AskBingBong askBingBong)
	{
		_cachedResponses ??= BuildResponses();
		askBingBong.responses = _cachedResponses;
	}

	private static Action_AskBingBong.BingBongResponse[]? _cachedResponses;

	private static Action_AskBingBong.BingBongResponse[] BuildResponses()
	{
		Action_AskBingBong.BingBongResponse[] currentResponses = new Action_AskBingBong.BingBongResponse[OriginalResponsesData.Length];
		for (int index = 0; index < OriginalResponsesData.Length; index++)
		{
			currentResponses[index] = OriginalResponsesData[index].ToBingBongResponse();
		}

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
				Action_AskBingBong.BingBongResponse newResponse = new()
				{
					sfx = CreateSFX(voice.Name, [clip]),
					subtitleID = BingBongSubtitleID,
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

		return currentResponses;
	}




}

public class BingBongResponseData
{
    public AudioClip[] Clips { get; set; } = [];
    public string SfxName { get; set; } = "";
    public string SubtitleID { get; set; } = "";
    public AnimationCurve? MouthCurve { get; set; } = null;
    public float MouthCurveTime { get; set; } = 0f;

    public Action_AskBingBong.BingBongResponse ToBingBongResponse()
    {
        return new Action_AskBingBong.BingBongResponse
        {
            sfx = ReplaceBingBongModule.CreateSFX(SfxName, (AudioClip[])Clips.Clone()),
            subtitleID = SubtitleID,
            mouthCurve = MouthCurve,
            mouthCurveTime = MouthCurveTime
        };
    }

    public static BingBongResponseData FromBingBongResponse(Action_AskBingBong.BingBongResponse response)
    {
		Debug.Log($"Creating BingBongResponseData from response with SFX name: {response.sfx.name}, subtitleID: {response.subtitleID}");
        return new BingBongResponseData
        {
            Clips = (AudioClip[])response.sfx.clips.Clone(),
            SfxName = response.sfx.name,
            SubtitleID = response.subtitleID,
            MouthCurve = response.mouthCurve,
            MouthCurveTime = response.mouthCurveTime
        };
    }
}