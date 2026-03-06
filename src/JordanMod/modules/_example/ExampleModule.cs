using System;

namespace JordanMod.Modules.Example;

[Module(Enabled = false)]
class ExampleModule : Module
{
	public override string ModuleName => "Example Module";
    
    public override Type[] GetPatches()
    {
        return [typeof(Patches.ExamplePatch)];
    }

    public override void Initialize()
	{
		base.Initialize();
	}

    public override void Update()
	{
		base.Update();
	}

    public override void FixedUpdate()
	{
		base.FixedUpdate();
	}

    public override void Destroy()
	{
		base.Destroy();
	}
}