using System.Collections.Generic;
using Godot;

/// <summary>
///   A networked entity the player can control.
/// </summary>
public abstract class NetworkCharacter : NetworkRigidBody
{
    /// <summary>
    ///   The point towards which the character will move to point to
    /// </summary>
    public Vector3 LookAtPoint = new(0, 0, -1);

    /// <summary>
    ///   The direction the character wants to move. Doesn't need to be normalized
    /// </summary>
    public Vector3 MovementDirection = new(0, 0, 0);

    /// <summary>
    ///   Specifies value for which an error from predicted state and incoming server state is tolerable.
    /// </summary>
    [Export]
    public float PredictionErrorToleranceThreshold = 0.05f;

    [Export]
    public bool UseClientSidePrediction = true;

    protected bool replaying;

    private Tween? tween;
    private bool setup;

    private Queue<IncomingState> stateBuffer = new();
    private Queue<PredictedState> predictedStates = new();

    private NetworkInputVars? latestPredictedInput;

    /// <summary>
    ///   The unique network ID self-assigned by the client. In gameplay context, this is used to differentiate
    ///   between player-character entities versus normal in-game entities.
    /// </summary>
    public int PeerId { get; set; }

    /// <summary>
    ///   Returns true if this network character owns our peer id i.e. the one we controls (local player).
    /// </summary>
    public bool IsLocal => PeerId == NetworkManager.Instance.PeerId || !NetworkManager.Instance.IsNetworked;

    public override void _Ready()
    {
        base._Ready();

        if (!setup && PeerId > 0)
            SetupNetworkCharacter();
    }

    public override void _PhysicsProcess(float delta)
    {
        base._PhysicsProcess(delta);

        if (latestPredictedInput.HasValue)
        {
            predictedStates.Enqueue(new PredictedState
            {
                Input = latestPredictedInput.Value,
                Result = (StateSnapshot)ToSnapshot(),
            });

            latestPredictedInput = null;
        }

        if (stateBuffer.Count <= 0)
        {
            // Hasn't received any new updates from the server
            return;
        }

        var incomingState = stateBuffer.Dequeue();

        // Server reconciliation begins here

        // Discard old inputs relative to the latest received server ack
        while (predictedStates.Count > 0 && predictedStates.Peek().Input.Id < incomingState.LastAckedInputId)
            predictedStates.Dequeue();

        if (predictedStates.Count <= 0)
        {
            // No new inputs, just lerp to the server state
            ApplyState(incomingState.State);
            return;
        }

        var predictedState = predictedStates.Dequeue();

        var positionError = incomingState.State.Position - predictedState.Result.Position;

        if (positionError.LengthSquared() > PredictionErrorToleranceThreshold)
        {
            // Too much error, needs rewinding

            NetworkLinearVelocity = incomingState.State.LinearVelocity;

            // TODO: lerp just won't work right.
            ClearInterpolations();
            GlobalTransform = new Transform(incomingState.State.Rotation, incomingState.State.Position);

            // Replay remaining inputs not yet acked by the server
            foreach (var replay in predictedStates)
            {
                replaying = true;

                ApplyInput(replay.Input);

                // TODO: Fix super annoying jitters
                PredictSimulation(delta);
            }

            replaying = false;
        }
    }

    public virtual void SetupNetworkCharacter()
    {
        tween = new Tween();
        AddChild(tween);

        setup = true;
    }

    public override void NetworkDeserialize(PackedBytesBuffer buffer)
    {
        var ackedInputId = buffer.ReadUInt16();

        if (!IsLocal || !UseClientSidePrediction)
        {
            // Other player characters don't need CSP and reconciliation, just interpolate them (if enabled)
            base.NetworkDeserialize(buffer);
            return;
        }

        stateBuffer.Enqueue(new IncomingState
        {
            LastAckedInputId = ackedInputId,
            State = DecodePacket(buffer),
        });
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

        if (IsLocal)
        {
            Mode = ModeEnum.Rigid;
            interpolateStatesUntilNone = UseClientSidePrediction;
        }
    }

    public virtual void ApplyInput(NetworkInputVars input)
    {
        if (NetworkManager.Instance.IsClient)
        {
            if (!UseClientSidePrediction)
                return;

            latestPredictedInput = input;
        }

        LookAtPoint = input.WorldLookAtPoint;
        MovementDirection = input.DecodeMovementDirection();
    }

    public virtual IReconcilableData ToSnapshot()
    {
        return new StateSnapshot
        {
            LinearVelocity = NetworkLinearVelocity,
            Position = GlobalTranslation,
            Rotation = GlobalTransform.basis.Quat(),
        };
    }

    public struct IncomingState
    {
        public ushort LastAckedInputId { get; set; }
        public StateSnapshot State { get; set; }
    }

    public struct PredictedState
    {
        public NetworkInputVars Input { get; set; }
        public StateSnapshot Result { get; set; }
    }
}
