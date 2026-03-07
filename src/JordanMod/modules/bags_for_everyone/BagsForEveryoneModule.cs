using System;

namespace JordanMod.Modules.BagsForEveryone;

[Module(Enabled = true)]
class BagsForEveryoneModule : Module
{
	public override string ModuleName => "Bags for Everyone Module";
    
    public override Type[] GetPatches()
    {
        return [typeof(BagsForEveryonePatch)];
    }
}