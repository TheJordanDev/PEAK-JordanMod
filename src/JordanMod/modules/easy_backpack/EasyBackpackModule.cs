using System;
using UnityEngine;

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

		BackpackReference backpackRefs = BackpackReference.GetFromEquippedBackpack(targetCharacter);
		int slotCount = (targetSlot.prefab as Backpack)?.slotCount ?? 4;
		GUIManager.instance.OpenBackpackWheel(backpackRefs, slotCount, targetSlot.backpackType);
		_isBackpackOpen = true;
	}

	private void CloseBackpack()
	{
		if (!Application.isFocused) return;
		if (!_isBackpackOpen) return;
		_isBackpackOpen = false;
	}

}