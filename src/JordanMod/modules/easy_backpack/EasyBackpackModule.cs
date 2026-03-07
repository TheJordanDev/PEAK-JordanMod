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

		bool hasBackpack = localCharacter.player.backpackSlot.hasBackpack;
		Character carriedCharacter = localCharacter.data.carriedPlayer;
		bool carriedHasBackpack = carriedCharacter != null && carriedCharacter.player.backpackSlot.hasBackpack;

		BackpackReference backpackRefs;
		if (hasBackpack) backpackRefs = BackpackReference.GetFromEquippedBackpack(localCharacter);
		else if (carriedHasBackpack) backpackRefs = BackpackReference.GetFromEquippedBackpack(carriedCharacter);
		else return;

		GUIManager.instance.OpenBackpackWheel(backpackRefs);
		_isBackpackOpen = true;
	}

	private void CloseBackpack()
	{
		if (!Application.isFocused) return;
		if (!_isBackpackOpen) return;
		_isBackpackOpen = false;
	}

}