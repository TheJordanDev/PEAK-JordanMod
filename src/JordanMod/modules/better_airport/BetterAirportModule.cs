using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JordanMod.Modules.BetterAirport;

[Module(Enabled = true)]
class BetterAirportModule : Module
{
	public override string ModuleName => "Better Airport Module";
    
	private static readonly float DEFAULT_CONVEYOR_FORCE = 20f;
	private static readonly float DEFAULT_MOVEAMOUNT_1 = -0.15f;
	private static readonly float DEFAULT_MOVEAMOUNT_2 = -4.5f;
	private static readonly float DEFAULT_MOVEAMOUNT_3 = -0.15f;

    public override Type[] GetPatches()
    {
        return [];
    }

    public override void Initialize()
	{
		base.Initialize();
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name.ToLower() == "airport")
		{
			AirportCheckInKiosk checkinKiosk = UnityEngine.Object.FindFirstObjectByType<AirportCheckInKiosk>();
			if (checkinKiosk != null)
			{
				checkinKiosk.transform.position = new Vector3(-11, 1.5f, 52.5f);
				checkinKiosk.transform.eulerAngles = new Vector3(270, 0, 0);
			}
			AirportInviteFriendsKiosk friendKiosk = UnityEngine.Object.FindFirstObjectByType<AirportInviteFriendsKiosk>();
			if (friendKiosk != null)
			{
				friendKiosk.transform.position = new Vector3(-8, 1.5f, 52.5f);
				friendKiosk.transform.eulerAngles = new Vector3(270, 180, 0);
			}

			PlayerMoveZone[] conveyors = UnityEngine.Object.FindObjectsByType<PlayerMoveZone>(FindObjectsSortMode.None);
			foreach (PlayerMoveZone conveyor in conveyors)
			{
				AdjustConveyorSpeed(conveyor);
			}
			ConfigHandler.ConveyorSpeedModifier.SettingChanged += OnConveyorSpeedChanged;
		}
		else
		{
			ConfigHandler.ConveyorSpeedModifier.SettingChanged -= OnConveyorSpeedChanged;
		}
	}

	private static void OnConveyorSpeedChanged(object? sender, EventArgs e)
	{
		PlayerMoveZone[] conveyors = UnityEngine.Object.FindObjectsByType<PlayerMoveZone>(FindObjectsSortMode.None);
		foreach (PlayerMoveZone conveyor in conveyors)
		{
			AdjustConveyorSpeed(conveyor);
		}
	}

	private static void AdjustConveyorSpeed(PlayerMoveZone zone)
	{
		float speedMultiplier = ConfigHandler.ConveyorSpeedModifier.Value;

		zone.Force = DEFAULT_CONVEYOR_FORCE * speedMultiplier;
		
		AdjustConveyorAnimation(zone.transform.parent?.gameObject, speedMultiplier);
		Transform? sibling = zone.transform.parent?.Find("conveyor");
		if (sibling != null) AdjustConveyorAnimation(sibling.gameObject, speedMultiplier);
	}

	private static void AdjustConveyorAnimation(GameObject? conveyor, float speedMultiplier)
	{
		if (conveyor == null) return;
		if (!conveyor.TryGetComponent<MeshRenderer>(out MeshRenderer? meshRenderer) || meshRenderer == null) return;
		Material material = Array.Find(meshRenderer.materials, m => m != null && m.name.StartsWith("M_Conveyer"));
		if (material == null) return;
		material.SetColor("_MoveAmount1", new Color(0, DEFAULT_MOVEAMOUNT_1 * speedMultiplier, 0, 0));
		material.SetColor("_MoveAmount2", new Color(0, DEFAULT_MOVEAMOUNT_2 * speedMultiplier, 0, 0));
		material.SetColor("_MoveAmount3", new Color(0, DEFAULT_MOVEAMOUNT_3 * speedMultiplier, 0, 0));
	}

}