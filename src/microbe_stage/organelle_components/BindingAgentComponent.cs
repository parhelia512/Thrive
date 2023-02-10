/// <summary>
///   Used to detect if a binding agent is present
/// </summary>
public partial class BindingAgentComponent : EmptyOrganelleComponent
{
}

public partial class BindingAgentComponentFactory : IOrganelleComponentFactory
{
    public IOrganelleComponent Create()
    {
        return new BindingAgentComponent();
    }

    public void Check(string name)
    {
    }
}
