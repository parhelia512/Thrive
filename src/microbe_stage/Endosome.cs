using System;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   This does nothing (for now) and only exist so saving could work.
/// </summary>
[JSONAlwaysDynamicType]
[SceneLoadedClass("res://src/microbe_stage/Endosome.tscn", UsesEarlyResolve = false)]
public partial class Endosome : Node3D, IEntity
{
    [JsonProperty]
    private Color tint;

    [JsonProperty]
    private int renderPriority;

    [JsonIgnore]
    public MeshInstance3D? Mesh { get; private set; }

    [JsonIgnore]
    public Color Tint
    {
        get => tint;
        set
        {
            tint = value;
            ApplyTint();
        }
    }

    [JsonIgnore]
    public int RenderPriority
    {
        get => renderPriority;
        set
        {
            renderPriority = value;
            ApplyRenderPriority();
        }
    }

    [JsonIgnore]
    public Node3D EntityNode => this;

    [JsonIgnore]
    public AliveMarker AliveMarker { get; } = new();

    public override void _Ready()
    {
        Mesh = GetNode<MeshInstance3D>("EngulfedObjectHolder") ?? throw new Exception("mesh node not found");

        var material = Mesh!.MaterialOverride as ShaderMaterial;

        if (material == null)
            GD.PrintErr("Material is not found from the EngulfedObjectHolder mesh for Endosome");

        // This has to be done here because setting this in Godot editor truncates
        // the number to only 3 decimal places.
        material?.SetShaderParameter("jiggleAmount", 0.0001f);

        ApplyTint();
        ApplyRenderPriority();
    }

    public void OnDestroyed()
    {
        AliveMarker.Alive = false;
    }

    private void ApplyTint()
    {
        var material = Mesh?.MaterialOverride as ShaderMaterial;
        material?.SetShaderParameter("tint", tint);
    }

    private void ApplyRenderPriority()
    {
        if (Mesh == null)
            return;

        var material = Mesh.MaterialOverride;
        material.RenderPriority = renderPriority;
    }
}
