using System;
using pworld.Scripts.Extensions;

namespace JordanMod.Events;

public class GlobalEventListener
{

	public static void Initialize()
	{
		// GlobalEvents.OnItemThrown += OnItemThrown;
	}

	private static void OnItemThrown(Item item)
	{
		// Bonkable bonkable = item.gameObject.GetOrAddComponent<Bonkable>();
		// // A Bonkable added at runtime has no serialized prefab data, so its `bonk` SFX array
		// // defaults to null (not empty) and Bonkable.Bonk() NREs on bonk.Length. Give it an
		// // empty array so a bonk still ragdolls/knocks back the target, just without a sound.
		// bonkable.bonk ??= Array.Empty<SFX_Instance>();
	}

}
