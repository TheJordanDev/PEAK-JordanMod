using System;
using UnityEngine;
using Zorro.Core;

namespace JordanMod.Modules.EasyBackpack;

[Module(Enabled = true)]
class EasyBackpackModule : Module
{
	public static EasyBackpackModule? Instance { get; private set; }

	public override string ModuleName => "Easy Backpack Module";
    
	public bool _isBackpackOpen = false;

    public override Type[] GetPatches()
    {
        return [typeof(EasyBackpackPatch)];
    }

	public override void Initialize()
	{
		if (Instance != null) return;
		Instance = this;
		base.Initialize();
	}

	public override void Update()
	{
		if (!_isBackpackOpen && Input.GetKeyDown(ConfigHandler.OpenBackpack.Value)) OpenBackpack();
		else if (_isBackpackOpen && (Input.GetKeyUp(ConfigHandler.OpenBackpack.Value) || Input.GetKeyDown(KeyCode.Escape))) CloseBackpack();
	}

	private void OpenBackpack()
	{
		if (!Application.isFocused) return;
		if (_isBackpackOpen) return;

		Character localCharacter = Character.localCharacter;
		if (localCharacter == null) return;

		Character carriedCharacter = localCharacter.data.carriedPlayer;

		Character targetCharacter;
		BackpackSlot targetSlot;
		if (localCharacter.player.backpackSlot.backpackType != BackpackSlot.BackpackType.None)
		{
			targetCharacter = localCharacter;
			targetSlot = localCharacter.player.backpackSlot;
		}
		else if (carriedCharacter != null && carriedCharacter.player.backpackSlot.backpackType != BackpackSlot.BackpackType.None)
		{
			targetCharacter = carriedCharacter;
			targetSlot = carriedCharacter.player.backpackSlot;
		}
		else return;

		// The wheel we open is the one for the backpack worn on the target's back, and its
		// on-back visuals are what BackpackWheel.Choose() pulls items out of. While the
		// backpack is held in hand (its slot, ID 3, is the equipped one) CharacterBackpackHandler
		// deactivates those visuals, so anything taken out of the wheel is spawned into a
		// disabled rig and vanishes. Refuse to open in that state; the game's own
		// hold-interact on the held backpack still works and opens the correct wheel.
		if (IsBackpackInHands(targetCharacter)) return;

		BackpackReference backpackRefs = BackpackReference.GetFromEquippedBackpack(targetCharacter);
		int slotCount = (targetSlot.prefab as Backpack)?.slotCount ?? 4;
		GUIManager.instance.OpenBackpackWheel(backpackRefs, slotCount, targetSlot.backpackType);
		_isBackpackOpen = true;
	}

	private static bool IsBackpackInHands(Character character)
	{
		Optionable<byte> selectedSlot = character.refs.items.currentSelectedSlot;
		return selectedSlot.IsSome && selectedSlot.Value == character.player.backpackSlot.itemSlotID;
	}

	private void CloseBackpack()
	{
		if (!Application.isFocused) return;
		if (!_isBackpackOpen) return;
		_isBackpackOpen = false;
	}

}