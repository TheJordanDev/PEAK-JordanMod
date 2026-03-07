using System;

namespace JordanMod.Modules.OpenMesa;

[Module(Enabled = true)]
class OpenMesaModule : Module
{
	public override string ModuleName => "Open Mesa Module";
    
    public override Type[] GetPatches()
    {
        return [typeof(OpenMesaPatch)];
    }
}