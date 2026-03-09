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

	public static bool HasReplacedSounds = false;
	public static BingBongResponseData[] OriginalResponsesData = [];
    
    public override Type[] GetPatches()
    {
        return [typeof(ReplaceBingBongPatch)];
    }

    public override void Initialize()
	{
		base.Initialize();
		LocalizedText.mainTable.Add("idk_funny", ["Test subtitle!"]);
		AudioSyncWorker.OnAudioLoadComplete += OnAudioLoadComplete;
	}

	public override void Update()
	{
		base.Update();
		if (Input.GetKeyDown(KeyCode.P))
		{
			Helper.FindItemByName("BingBong_Prop Variant", out Item? item);
			if (item == null) return;
			Debug.Log($"Found item: {item.name} in scene {item.gameObject.scene.name}");
		}
	}

	private static void OnAudioLoadComplete()
	{
		if (!HasReplacedSounds) return;
		Action_AskBingBong[] allBingBongActions = UnityEngine.Object.FindObjectsByType<Action_AskBingBong>(FindObjectsSortMode.None);
		foreach (Action_AskBingBong askBingBong in allBingBongActions) {
			ReplaceBingBongResponses(askBingBong);
		}
	}

	public static void ReplaceBingBongResponses(Action_AskBingBong askBingBong)
	{
		Action_AskBingBong.BingBongResponse[] currentResponses = new Action_AskBingBong.BingBongResponse[OriginalResponsesData.Length];
		for (int index = 0; index < OriginalResponsesData.Length; index++)
		{
			currentResponses[index] = OriginalResponsesData[index].ToBingBongResponse();
		}

		askBingBong.responses = [];

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
            sfx = new SFX_Instance
            {
                name = SfxName,
                clips = (AudioClip[])Clips.Clone()
            },
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