using UnityEngine;
using Zorro.Core;

namespace JordanMod.Modules.StashedBugle;

[Module(Enabled = true)]
class StashedBugleModule : Module
{
	public override string ModuleName => "Stashed Bugle Module";

	private readonly string _bugleItemName = "Bugle";
	private readonly string _megaphoneItemName = "Megaphone";

	private float? lastPressTime = null;

	public override void Update()
	{
		if (Input.GetKeyDown(ConfigHandler.ToggleBugle.Value)) ToggleBugle();
	}

	private void ToggleBugle()
	{
		if (lastPressTime == null || Time.time - lastPressTime > 1f) lastPressTime = Time.time;
		else return;
		
		Character localCharacter = Character.localCharacter;
		if (localCharacter == null) return;
		
		Item heldItem = localCharacter.data.currentItem;
		if (heldItem != null)
		{
			if (heldItem.UIData.itemName == _bugleItemName)
			{
				ClearHeldSlot(localCharacter);
				SpawnItemByDisplayName(localCharacter, _megaphoneItemName);
			}
			else if (heldItem.UIData.itemName == _megaphoneItemName)
			{
				ClearHeldSlot(localCharacter);
			}
		}
		else if (heldItem == null)
		{
			ItemSlot? withBugleSlot = null;
			for (int i = 0; i < CharacterItems.MAX_SLOT; i++)
			{
				ItemSlot itemSlot = localCharacter.player.GetItemSlot((byte)i);
				if (itemSlot == null || itemSlot.prefab == null || itemSlot.prefab.UIData == null || itemSlot.prefab.UIData.itemName != _bugleItemName) continue;
				withBugleSlot = itemSlot;
				break;
			}
			if (withBugleSlot == null) SpawnItemByDisplayName(localCharacter, _bugleItemName);
			else localCharacter.refs.items.EquipSlot(Optionable<byte>.Some(withBugleSlot.itemSlotID));
		}
	}

	// Player.EmptySlot already tells the host to clear the slot (it RPCs
	// RPCRemoveItemFromSlot to the master client when we aren't the host), so calling
	// RPCRemoveItemFromSlot ourselves on top of it only logged "Only Master Client can
	// remove items!" on every non-host press.
	private static void ClearHeldSlot(Character character)
	{
		character.refs.items.DestroyHeldItemRpc();
		character.player.EmptySlot(character.refs.items.currentSelectedSlot);
	}

	// SpawnItemInHand expects the item's Resources prefab name, which is not
	// guaranteed to match its UIData.itemName display string, so resolve it
	// via the ItemDatabase instead of assuming the two are identical.
	private static void SpawnItemByDisplayName(Character character, string displayName)
	{
		Item? match = Helper.ItemDatabase.Objects.Find(item => item.UIData != null && item.UIData.itemName == displayName);
		if (match == null)
		{
			Debug.LogWarning($"[StashedBugle] Could not find an item with UIData.itemName '{displayName}' in ItemDatabase.");
			return;
		}
		character.refs.items.SpawnItemInHand(match.name);
	}
}