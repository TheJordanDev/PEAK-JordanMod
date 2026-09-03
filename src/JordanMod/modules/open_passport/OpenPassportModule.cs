using System;
using UnityEngine;

namespace JordanMod.Modules.OpenPassport;

[Module(Enabled = true)]
class OpenPassportModule : Module
{
	public override string ModuleName => "Open Passport Module";

	public override Type[] GetPatches() { return []; }

	public override void Update()
	{
		if (!Input.GetKeyDown(ConfigHandler.OpenPassport.Value)) return;

		// PassportManager.instance is a plain static that survives a scene unload, so it can
		// be a destroyed object here; the Unity == operator is what catches that. Its
		// Initialize() also reads Character.localCharacter.characterName, so don't open it
		// before we have a character.
		PassportManager passportManager = PassportManager.instance;
		if (passportManager == null) return;
		if (Character.localCharacter == null) return;
		if (passportManager.isOpen) return;

		passportManager.ToggleOpen();
	}
}
