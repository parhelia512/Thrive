using System.Collections.Generic;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   Syncs the position and rotation of a Rigidbody (and velocity if needed), with the option
///   of choosing which axes to sync to minimize bandwidth usage.
/// </summary>
public abstract class NetworkRigidBody : RigidBody, INetworkEntity
{
    [Export]
    public bool SyncLinearXAxis = true;

    [Export]
    public bool SyncLinearYAxis = true;

    [Export]
    public bool SyncLinearZAxis = true;

    [Export]
    public bool SyncAngularXAxis = true;

    [Export]
    public bool SyncAngularYAxis = true;

    [Export]
    public bool SyncAngularZAxis = true;

    [Export]
    public bool SyncVelocity = true;

    /// <summary>
    ///   Enables state (position, rotation) interpolation to the newly incoming state.
    ///   This adds delay, the amount of which equals to <see cref="NetworkManager.TimeStep"/>.
    /// </summary>
    [Export]
    public bool EnableStateInterpolations = true;

    protected Queue<StateSnapshot> stateInterpolations = new();
    protected float lerpTimer;

    protected bool interpolateStatesUntilNone;
    protected bool predictedToCollide;

    private StateSnapshot? fromState;

    private PhysicsDirectBodyState bodyState = null!;
    private Vector3 predictedLinearVelocity;

    [JsonIgnore]
    public Spatial EntityNode => this;

    [JsonIgnore]
    public AliveMarker AliveMarker { get; } = new();

    public abstract string ResourcePath { get; }

    public uint NetworkEntityId { get; set; }

    /// <summary>
    ///   Helper property for interacting with velocity in networkable environment. Server always use the real velocity
    ///   whereas clients use "fake" predicted velocity for client-side prediction and then applied in
    ///   <see cref="PredictSimulation(float)"/>.
    /// </summary>
    public Vector3 NetworkLinearVelocity
    {
        get => NetworkManager.Instance.IsServer ? LinearVelocity : predictedLinearVelocity;
        set
        {
            if (NetworkManager.Instance.IsClient)
            {
                predictedLinearVelocity = value;
            }
            else
            {
                LinearVelocity = value;
            }
        }
    }

    public override void _Ready()
    {
        base._Ready();

        bodyState = PhysicsServer.BodyGetDirectState(GetRid());
    }

    public override void _PhysicsProcess(float delta)
    {
        if (!NetworkManager.Instance.IsClient)
            return;

        PredictSimulation(delta);
        InterpolateStates(delta);
    }

    /// <summary>
    ///   Client-side function to step this rigidbody's physics manually.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     For server reconciliation, each player input replays need to move the underlying rigidbodies accordingly,
    ///     in order to do this we need to step physics in a single frame. However, there's currently now way in Godot
    ///     to do that so we just predict the physics simulation hoping for the end result to be similar.
    ///   </para>
    /// </remarks>
    public virtual void PredictSimulation(float delta)
    {
        // Indirectly this method tries to replicate Bullet physics, this will be a pain...

        // TODO: In some game runs, misprediction is higher but sometimes it's also fewer??
        //       Though, higher ping consistenly have higher errors

        // Damping
        NetworkLinearVelocity *= 1.0f - delta * LinearDamp;

        var testMotionResult = new PhysicsTestMotionResult();

        predictedToCollide = PhysicsServer.BodyTestMotion(
            GetRid(), GlobalTransform, NetworkLinearVelocity * delta, false, testMotionResult);

        // TODO: Handle collision

        // Integrate transform
        GlobalTranslation += NetworkLinearVelocity * delta;

        // Just ignore rotations for now
    }

    public void ApplyPredictiveCentralImpulse(Vector3 impulse)
    {
        NetworkLinearVelocity += impulse * bodyState.InverseMass;
    }

    public virtual void NetworkSerialize(PackedBytesBuffer buffer)
    {
        var bools = new bool[7]
        {
            SyncLinearXAxis,
            SyncLinearYAxis,
            SyncLinearZAxis,
            SyncAngularXAxis,
            SyncAngularYAxis,
            SyncAngularZAxis,
            SyncVelocity,
        };
        buffer.Write(bools.ToByte());

        if (SyncLinearXAxis)
        {
            if (SyncVelocity)
                buffer.Write(NetworkLinearVelocity.x);

            buffer.Write(GlobalTranslation.x);
        }

        if (SyncLinearYAxis)
        {
            if (SyncVelocity)
                buffer.Write(NetworkLinearVelocity.y);

            buffer.Write(GlobalTranslation.y);
        }

        if (SyncLinearZAxis)
        {
            if (SyncVelocity)
                buffer.Write(NetworkLinearVelocity.z);

            buffer.Write(GlobalTranslation.z);
        }

        if (SyncAngularXAxis)
            buffer.Write(GlobalRotation.x);

        if (SyncAngularYAxis)
            buffer.Write(GlobalRotation.y);

        if (SyncAngularZAxis)
            buffer.Write(GlobalRotation.z);
    }

    public virtual void NetworkDeserialize(PackedBytesBuffer buffer)
    {
        ApplyState(DecodePacket(buffer));
    }

    public virtual void PackSpawnState(PackedBytesBuffer buffer)
    {
        buffer.Write(GlobalTranslation);
    }

    public virtual void OnRemoteSpawn(PackedBytesBuffer buffer, GameProperties currentGame)
    {
        Translation = buffer.ReadVector3();
        Mode = ModeEnum.Kinematic;
    }

    public virtual void OnDestroyed()
    {
        AliveMarker.Alive = false;
    }

    public void ApplyState(StateSnapshot state)
    {
        if (EnableStateInterpolations)
        {
            while (stateInterpolations.Count > 2)
                stateInterpolations.Dequeue();

            stateInterpolations.Enqueue(state);
        }
        else
        {
            GlobalTransform = new Transform(state.Rotation, state.Position);
        }
    }

    protected StateSnapshot DecodePacket(PackedBytesBuffer packet)
    {
        var bools = packet.ReadByte();

        float xPos, yPos, zPos, xRot, yRot, zRot, xVel, yVel, zVel;
        xPos = yPos = zPos = xRot = yRot = zRot = xVel = yVel = zVel = 0;

        if (bools.ToBoolean(0))
        {
            if (bools.ToBoolean(6))
                xVel = packet.ReadSingle();

            xPos = packet.ReadSingle();
        }

        if (bools.ToBoolean(1))
        {
            if (bools.ToBoolean(6))
                yVel = packet.ReadSingle();

            yPos = packet.ReadSingle();
        }

        if (bools.ToBoolean(2))
        {
            if (bools.ToBoolean(6))
                zVel = packet.ReadSingle();

            zPos = packet.ReadSingle();
        }

        if (bools.ToBoolean(3))
            xRot = packet.ReadSingle();

        if (bools.ToBoolean(4))
            yRot = packet.ReadSingle();

        if (bools.ToBoolean(5))
            zRot = packet.ReadSingle();

        return new StateSnapshot
        {
            LinearVelocity = new Vector3(xVel, yVel, zVel),
            Position = new Vector3(xPos, yPos, zPos),
            Rotation = new Quat(new Vector3(xRot, yRot, zRot)),
        };
    }

    protected virtual void InterpolateStates(float delta)
    {
        lerpTimer += delta;

        var timestep = NetworkManager.Instance.Settings!.TimeStep;

        if (lerpTimer > timestep)
        {
            lerpTimer = 0;

            if (stateInterpolations.Count > (interpolateStatesUntilNone ? 0 : 1))
                fromState = stateInterpolations.Dequeue();
        }

        if (stateInterpolations.Count <= 0 || !fromState.HasValue)
            return;

        var toState = stateInterpolations.Peek();

        if (timestep <= 0)
        {
            GlobalTransform = new Transform(toState.Rotation, toState.Position);
        }
        else
        {
            var weight = lerpTimer / timestep;

            var position = fromState.Value.Position.LinearInterpolate(toState.Position, weight);
            var rotation = fromState.Value.Rotation.Slerp(toState.Rotation, weight);

            GlobalTransform = new Transform(rotation, position);
        }
    }

    protected void ClearInterpolations()
    {
        fromState = null;
        stateInterpolations.Clear();
    }

    public struct StateSnapshot : IReconcilableData
    {
        public Vector3 LinearVelocity { get; set; }
        public Vector3 Position { get; set; }
        public Quat Rotation { get; set; }
    }
}
