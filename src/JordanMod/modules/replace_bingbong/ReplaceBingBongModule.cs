using System;

namespace JordanMod.Modules.ReplaceBingBong;

[Module(Enabled = true)]
class ReplaceBingBongModule : Module
{
	public override string ModuleName => "Replace BingBong Module";
    
    public override Type[] GetPatches()
    {
        return [typeof(ReplaceBingBongPatch)];
    }

    public override void Initialize()
	{
		base.Initialize();
		LocalizedText.mainTable.Add("idk_funny", ["Test subtitle!"]);
	}
}