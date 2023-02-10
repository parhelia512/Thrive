using Godot;
using Newtonsoft.Json;

[JSONAlwaysDynamicType]
[SceneLoadedClass("res://src/microbe_stage/particles/CellBurstEffect.tscn", UsesEarlyResolve = false)]
public partial class CellBurstEffect : Node3D, ITimedLife
{
    [JsonProperty]
    public float Radius;

#pragma warning disable CA2213
    private GpuParticles3D particles = null!;
#pragma warning restore CA2213

    public float TimeToLiveRemaining { get; set; }

    public override void _Ready()
    {
        particles = GetNode<GpuParticles3D>("GPUParticles3D");

        TimeToLiveRemaining = (float)particles.Lifetime;

        var material = (ParticleProcessMaterial)particles.ProcessMaterial;

        material.EmissionSphereRadius = Radius / 2;
        material.LinearAccelMax = Radius / 2;
        particles.OneShot = true;
    }

    public void OnTimeOver()
    {
        this.DetachAndQueueFree();
    }
}
