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
				localCharacter.refs.items.DestroyHeldItemRpc();
				localCharacter.player.EmptySlot(localCharacter.refs.items.currentSelectedSlot);
				localCharacter.player.RPCRemoveItemFromSlot(localCharacter.refs.items.currentSelectedSlot.Value);

				localCharacter.refs.items.SpawnItemInHand(_megaphoneItemName);
			}
			else if (heldItem.UIData.itemName == _megaphoneItemName)
			{
				localCharacter.refs.items.DestroyHeldItemRpc();
				localCharacter.player.EmptySlot(localCharacter.refs.items.currentSelectedSlot);
				localCharacter.player.RPCRemoveItemFromSlot(localCharacter.refs.items.currentSelectedSlot.Value);
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
			if (withBugleSlot == null) localCharacter.refs.items.SpawnItemInHand(_bugleItemName);
			else localCharacter.refs.items.EquipSlot(Optionable<byte>.Some(withBugleSlot.itemSlotID));
		}
	}
}