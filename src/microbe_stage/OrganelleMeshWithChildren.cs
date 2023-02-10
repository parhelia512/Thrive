using Godot;

/// <summary>
///   Applies the tint to the defined children
/// </summary>
public partial class OrganelleMeshWithChildren : MeshInstance3D
{
    public void SetTintOfChildren(Color value)
    {
        foreach (GeometryInstance3D mesh in GetChildren())
        {
            if (mesh.MaterialOverride is ShaderMaterial shaderMaterial)
            {
                shaderMaterial.SetShaderParameter("tint", value);
            }
        }
    }

    public void SetDissolveEffectOfChildren(float value)
    {
        foreach (GeometryInstance3D mesh in GetChildren())
        {
            if (mesh.MaterialOverride is ShaderMaterial shaderMaterial)
            {
                shaderMaterial.SetShaderParameter("dissolveValue", value);
            }
        }
    }
}
