using pworld.Scripts.Extensions;

namespace JordanMod.Events;

public class GlobalEventListener
{

	public static void Initialize()
	{
		GlobalEvents.OnItemThrown += OnItemThrown;
	}

	private static void OnItemThrown(Item item)
	{
		item.gameObject.GetOrAddComponent<Bonkable>();
	}

}
