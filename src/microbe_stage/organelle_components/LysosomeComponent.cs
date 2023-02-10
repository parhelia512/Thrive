using Godot;

public partial class LysosomeComponent : IOrganelleComponent
{
    public void OnAttachToCell(PlacedOrganelle organelle)
    {
        var configuration = organelle.Upgrades?.CustomUpgradeData;

        var upgrades = configuration as LysosomeUpgrades;

        var enzyme = upgrades == null ? SimulationParameters.Instance.GetEnzyme("lipase") : upgrades.Enzyme;

        organelle.StoredEnzymes.Clear();
        organelle.StoredEnzymes[enzyme] = 1;
    }

    public void OnDetachFromCell(PlacedOrganelle organelle)
    {
    }

    public void UpdateAsync(float delta)
    {
        // TODO: Animate lysosomes sticking onto phagosomes (if possible)
    }

    public void UpdateSync()
    {
    }

    public void OnShapeParentChanged(Microbe newShapeParent, Vector3 offset)
    {
    }
}

public partial class LysosomeComponentFactory : IOrganelleComponentFactory
{
    public IOrganelleComponent Create()
    {
        return new LysosomeComponent();
    }

    public void Check(string name)
    {
    }
}

[JSONDynamicTypeAllowed]
public partial class LysosomeUpgrades : IComponentSpecificUpgrades
{
    public LysosomeUpgrades(Enzyme enzyme)
    {
        Enzyme = enzyme;
    }

    public Enzyme Enzyme { get; set; }

    public object Clone()
    {
        return new LysosomeUpgrades(Enzyme);
    }
}
