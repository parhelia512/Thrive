using Godot;

/// <summary>
///   Line helping the player by showing a direction
/// </summary>
public partial class GuidanceLine : ImmediateMesh
{
    private Vector3 lineStart;

    private Vector3 lineEnd;

    private Color colour = Colors.White;

    private bool dirty = true;

    [Export]
    public Vector3 LineStart
    {
        get => lineStart;
        set
        {
            if (lineStart == value)
                return;

            dirty = true;
            lineStart = value;
        }
    }

    [Export]
    public Vector3 LineEnd
    {
        get => lineEnd;
        set
        {
            if (lineEnd == value)
                return;

            dirty = true;
            lineEnd = value;
        }
    }

    [Export]
    public Color Colour
    {
        get => colour;
        set
        {
            if (colour == value)
                return;

            dirty = true;
            colour = value;
        }
    }

    public GuidanceLine()
    {
        // Make the line update after any possible code that might update our parameters
        // ProcessPriority = 800;
        // ProcessMode = ProcessModeEnum.Always;

        // This material is needed for SetColor to work at all
        var material = new StandardMaterial3D();
        material.VertexColorUseAsAlbedo = true;
        SurfaceSetMaterial(0, material);

        // MaterialOverride = material;
    }

    /* public override void _Process(double delta)
    {
        if (!dirty)
            return;

        dirty = false;
        Clear();
        Begin(Mesh.PrimitiveType.Lines);

        SetColor(colour);
        AddVertex(LineStart);
        AddVertex(LineEnd);

        // TODO: if we want to have line thickness, we need to generate a quad here with the wanted *width* around the
        // points (we need to figure out the right rotation for the line at both ends for where to place those points
        // that are slightly off from the positions)

        End();
    }*/
}
