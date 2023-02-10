using System;
using Godot;

/// <summary>
///   Adds radar capability to a cell
/// </summary>
public partial class ChemoreceptorComponent : ExternallyPositionedComponent
{
    private Compound? targetCompound;
    private float searchRange;
    private float searchAmount;
    private Color lineColour = Colors.White;

    public override void UpdateAsync(float delta)
    {
        base.UpdateAsync(delta);

        organelle!.ParentMicrobe!.ReportActiveChemereception(targetCompound!, searchRange, searchAmount, lineColour);
    }

    protected override void CustomAttach()
    {
        if (organelle?.OrganelleGraphics == null)
            throw new InvalidOperationException("Chemoreceptor needs parent organelle to have graphics");

        var configuration = organelle.Upgrades?.CustomUpgradeData;

        // Use default values if not configured
        if (configuration == null)
        {
            SetDefaultConfiguration();
            return;
        }

        SetConfiguration((ChemoreceptorUpgrades)configuration);
    }

    protected override bool NeedsUpdateAnyway()
    {
        // TODO: https://github.com/Revolutionary-Games/Thrive/issues/2906
        return organelle!.OrganelleGraphics!.Transform.Basis == Transform3D.Identity.Basis;
    }

    protected override void OnPositionChanged(Quaternion rotation, float angle, Vector3 membraneCoords)
    {
        organelle!.OrganelleGraphics!.Transform = new Transform3D(new Basis(rotation), membraneCoords);
    }

    private void SetConfiguration(ChemoreceptorUpgrades configuration)
    {
        targetCompound = configuration.TargetCompound;
        searchRange = configuration.SearchRange;
        searchAmount = configuration.SearchAmount;
        lineColour = configuration.LineColour;
    }

    private void SetDefaultConfiguration()
    {
        targetCompound = SimulationParameters.Instance.GetCompound(Constants.CHEMORECEPTOR_DEFAULT_COMPOUND_NAME);
        searchRange = Constants.CHEMORECEPTOR_RANGE_DEFAULT;
        searchAmount = Constants.CHEMORECEPTOR_AMOUNT_DEFAULT;
        lineColour = Colors.White;
    }
}

public partial class ChemoreceptorComponentFactory : IOrganelleComponentFactory
{
    public IOrganelleComponent Create()
    {
        return new ChemoreceptorComponent();
    }

    public void Check(string name)
    {
    }
}

[JSONDynamicTypeAllowed]
public partial class ChemoreceptorUpgrades : IComponentSpecificUpgrades
{
    public ChemoreceptorUpgrades(Compound targetCompound, float searchRange, float searchAmount, Color lineColour)
    {
        TargetCompound = targetCompound;
        SearchRange = searchRange;
        SearchAmount = searchAmount;
        LineColour = lineColour;
    }

    public Compound TargetCompound { get; set; }
    public float SearchRange { get; set; }
    public float SearchAmount { get; set; }
    public Color LineColour { get; set; }

    public object Clone()
    {
        return new ChemoreceptorUpgrades(TargetCompound, SearchRange, SearchAmount, LineColour);
    }
}
