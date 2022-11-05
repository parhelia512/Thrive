using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   This is a shot agent projectile, does damage on hitting a cell of different species
/// </summary>
[JSONAlwaysDynamicType]
[SceneLoadedClass("res://src/microbe_stage/AgentProjectile.tscn", UsesEarlyResolve = false)]
public class AgentProjectile : RigidBody, ITimedLife, INetEntity
{
    private Particles particles = null!;

    public float TimeToLiveRemaining { get; set; }
    public float Amount { get; set; }
    public AgentProperties? Properties { get; set; }
    public EntityReference<IEntity> Emitter { get; set; } = new();

    public Spatial EntityNode => this;

    public string ResourcePath => "res://src/microbe_stage/AgentProjectile.tscn";

    public uint NetEntityId { get; set; }

    public bool Synchronize { get; set; } = true;

    public AliveMarker AliveMarker { get; } = new();

    [JsonProperty]
    private float? FadeTimeRemaining { get; set; }

    public void OnTimeOver()
    {
        if (FadeTimeRemaining == null)
            BeginDestroy();
    }

    public override void _Ready()
    {
        if (Properties == null)
            throw new InvalidOperationException($"{nameof(Properties)} is required");

        particles = GetNode<Particles>("Particles");

        var emitterNode = Emitter.Value?.EntityNode;

        if (emitterNode != null)
            AddCollisionExceptionWith(emitterNode);

        Connect("body_shape_entered", this, nameof(OnContactBegin));
    }

    public override void _Process(float delta)
    {
        if (FadeTimeRemaining == null)
            return;

        FadeTimeRemaining -= delta;
        if (FadeTimeRemaining <= 0)
            this.DestroyDetachAndQueueFree();
    }

    public void NetworkTick(float delta)
    {
    }

    public void OnNetworkSync(Dictionary<string, string> data)
    {
        var rotation = (Vector3)GD.Str2Var(data[nameof(GlobalRotation)]);
        var position = (Vector3)GD.Str2Var(data[nameof(GlobalTranslation)]);

        GlobalRotation = rotation;
        GlobalTranslation = position;
    }

    public Dictionary<string, string>? PackStates()
    {
        var states = new Dictionary<string, string>
        {
            { nameof(GlobalTranslation), GD.Var2Str(GlobalTranslation) },
            { nameof(GlobalRotation), GD.Var2Str(GlobalRotation) },
        };

        return states;
    }

    public Dictionary<string, string>? PackReplicableVars()
    {
        var vars = new Dictionary<string, string>
        {
            { nameof(TimeToLiveRemaining), TimeToLiveRemaining.ToString(CultureInfo.CurrentCulture) },
            { nameof(Amount), Amount.ToString(CultureInfo.CurrentCulture) },
        };

        if (Properties != null)
            vars.Add(nameof(Properties), ThriveJsonConverter.Instance.SerializeObject(Properties));

        return vars;
    }

    public void OnReplicated(Dictionary<string, string>? data)
    {
        if (data == null)
            return;

        data.TryGetValue(nameof(TimeToLiveRemaining), out string timeToLive);
        data.TryGetValue(nameof(Amount), out string amount);
        data.TryGetValue(nameof(Properties), out string props);

        if (float.TryParse(timeToLive, out float parsedTimeToLive))
            TimeToLiveRemaining = parsedTimeToLive;

        if (float.TryParse(amount, out float parsedAmount))
            Amount = parsedAmount;

        if (!string.IsNullOrEmpty(props))
            Properties = ThriveJsonConverter.Instance.DeserializeObject<AgentProperties>(props);
    }

    public void OnDestroyed()
    {
        AliveMarker.Alive = false;
    }

    private void OnContactBegin(int bodyID, Node body, int bodyShape, int localShape)
    {
        _ = bodyID;
        _ = localShape;

        if (body is not Microbe microbe)
            return;

        if (microbe.Species == Properties!.Species)
            return;

        // If more stuff needs to be damaged we could make an IAgentDamageable interface.
        var target = microbe.GetMicrobeFromShape(bodyShape);

        if (target == null)
            return;

        int? peerId = null;
        if (Emitter.Value is INetPlayer netPlayer)
            peerId = netPlayer.PeerId;

        Invoke.Instance.Perform(
            () => target.Damage(Constants.OXYTOXY_DAMAGE * Amount, Properties.AgentType, peerId));

        if (FadeTimeRemaining == null)
        {
            // We should probably get some *POP* effect here.
            BeginDestroy();
        }
    }

    /// <summary>
    ///   Stops particle emission and destroys the object after 5 seconds.
    /// </summary>
    private void BeginDestroy()
    {
        particles.Emitting = false;

        // Disable collisions and stop this entity
        // This isn't the recommended way (disabling the collision shape), but as we don't have a reference to that here
        // this should also work for disabling the collisions
        CollisionLayer = 0;
        CollisionMask = 0;
        LinearVelocity = Vector3.Zero;

        // Timer that delays despawn of projectiles
        FadeTimeRemaining = Constants.PROJECTILE_DESPAWN_DELAY;

        AliveMarker.Alive = false;
    }
}
