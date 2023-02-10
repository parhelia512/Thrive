public partial class SignalingAgentComponent : EmptyOrganelleComponent
{
}

public partial class SignalingAgentComponentFactory : IOrganelleComponentFactory
{
    public IOrganelleComponent Create()
    {
        return new SignalingAgentComponent();
    }

    public void Check(string name)
    {
    }
}
