using Godot;

/// <summary>
///   A networked entity the player can control.
/// </summary>
public abstract class NetworkCharacter : NetworkRigidBody
{
    private Tween? tween;

    private bool setup;

    /// <summary>
    ///   The unique network ID self-assigned by the client. In gameplay context, this is used to differentiate
    ///   between player-character entities versus normal in-game entities.
    /// </summary>
    public int PeerId { get; set; }

    public override void _Ready()
    {
        base._Ready();

        if (!setup && PeerId > 0)
            SetupNetworkCharacter();
    }

    public virtual void SetupNetworkCharacter()
    {
        tween = new Tween();
        AddChild(tween);

        setup = true;
    }

    public override void PackSpawnState(PackedBytesBuffer buffer)
    {
        base.PackSpawnState(buffer);

        buffer.Write(PeerId);
    }

    public override void OnRemoteSpawn(PackedBytesBuffer buffer, GameProperties currentGame)
    {
        base.OnRemoteSpawn(buffer, currentGame);

        PeerId = buffer.ReadInt32();
    }

    protected override void ProcessBufferedStates(float delta)
    {
        if (PeerId != NetworkManager.Instance.PeerId)
        {
            base.ProcessBufferedStates(delta);
            return;
        }

        if (stateBuffer.Count <= 0)
            return;

        // Server reconciliation
        // TODO: Fix jitter

        var incomingState = stateBuffer.Dequeue();
        var currentState = new StateSnapshot { Position = GlobalTranslation, Rotation = GlobalTransform.basis.Quat() };

        var positionDiff = incomingState.Position - currentState.Position;
        var rotationDiff = incomingState.Rotation - currentState.Rotation;

        if (positionDiff.LengthSquared() > 0.001f)
        {
            tween?.InterpolateProperty(this, "global_translation", null, incomingState.Position, 0.1f);
            tween?.Start();
        }

        if (rotationDiff.LengthSquared > 0.001f)
        {
            GlobalRotation = incomingState.Rotation.GetEuler();
        }
    }
}
