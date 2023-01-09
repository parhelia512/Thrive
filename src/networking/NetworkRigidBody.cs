using System.Collections.Generic;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   Wraps a boilerplate implementation for syncing the position and rotation of a Rigidbody, with the option
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

    protected Queue<StateSnapshot> stateBuffer = new();
    protected float lerpTimer;

    private StateSnapshot? fromState;

    [JsonIgnore]
    public Spatial EntityNode => this;

    [JsonIgnore]
    public AliveMarker AliveMarker { get; } = new();

    public abstract string ResourcePath { get; }

    public uint NetworkEntityId { get; set; }

    public override void _PhysicsProcess(float delta)
    {
        if (!NetworkManager.Instance.IsClient)
            return;

        ProcessBufferedStates(delta);
    }

    public virtual void NetworkSerialize(PackedBytesBuffer buffer)
    {
        var bools = new bool[6]
        {
            SyncLinearXAxis,
            SyncLinearYAxis,
            SyncLinearZAxis,
            SyncAngularXAxis,
            SyncAngularYAxis,
            SyncAngularZAxis,
        };
        buffer.Write(bools.ToByte());

        if (SyncLinearXAxis)
            buffer.Write(GlobalTranslation.x);

        if (SyncLinearYAxis)
            buffer.Write(GlobalTranslation.y);

        if (SyncLinearZAxis)
            buffer.Write(GlobalTranslation.z);

        if (SyncAngularXAxis)
            buffer.Write(GlobalRotation.x);

        if (SyncAngularYAxis)
            buffer.Write(GlobalRotation.y);

        if (SyncAngularZAxis)
            buffer.Write(GlobalRotation.z);
    }

    public virtual void NetworkDeserialize(PackedBytesBuffer buffer)
    {
        var bools = buffer.ReadByte();

        float xPos, yPos, zPos, xRot, yRot, zRot;
        xPos = yPos = zPos = xRot = yRot = zRot = 0;

        if (bools.ToBoolean(0))
            xPos = buffer.ReadSingle();

        if (bools.ToBoolean(1))
            yPos = buffer.ReadSingle();

        if (bools.ToBoolean(2))
            zPos = buffer.ReadSingle();

        if (bools.ToBoolean(3))
            xRot = buffer.ReadSingle();

        if (bools.ToBoolean(4))
            yRot = buffer.ReadSingle();

        if (bools.ToBoolean(5))
            zRot = buffer.ReadSingle();

        stateBuffer.Enqueue(new StateSnapshot
        {
            Position = new Vector3(xPos, yPos, zPos),
            Rotation = new Quat(new Vector3(xRot, yRot, zRot)),
        });
    }

    public virtual void PackSpawnState(PackedBytesBuffer buffer)
    {
        buffer.Write(GlobalTranslation.x);
        buffer.Write(GlobalTranslation.y);
        buffer.Write(GlobalTranslation.z);
    }

    public virtual void OnRemoteSpawn(PackedBytesBuffer buffer, GameProperties currentGame)
    {
        Translation = new Vector3(buffer.ReadSingle(), buffer.ReadSingle(), buffer.ReadSingle());
    }

    public virtual void OnDestroyed()
    {
        AliveMarker.Alive = false;
    }

    protected virtual void ProcessBufferedStates(float delta)
    {
        // TODO: Fix jitter

        lerpTimer += delta;

        var timestep = NetworkManager.Instance.Settings!.TimeStep;

        if (lerpTimer > timestep)
        {
            lerpTimer -= timestep;

            if (stateBuffer.Count > 1)
                fromState = stateBuffer.Dequeue();
        }

        if (stateBuffer.Count <= 0 || !fromState.HasValue)
            return;

        var toState = stateBuffer.Peek();

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

    public struct StateSnapshot
    {
        public Vector3 Position;
        public Quat Rotation;
    }
}
