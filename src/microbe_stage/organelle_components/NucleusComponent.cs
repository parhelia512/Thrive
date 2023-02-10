/// <summary>
///   Literally does nothing anymore. If this isn't used as PlacedOrganelle.HasComponent type
///   This serves no purpose anymore.
/// </summary>
public partial class NucleusComponent : EmptyOrganelleComponent
{
}

public partial class NucleusComponentFactory : IOrganelleComponentFactory
{
    public IOrganelleComponent Create()
    {
        return new NucleusComponent();
    }

    public void Check(string name)
    {
    }
}
