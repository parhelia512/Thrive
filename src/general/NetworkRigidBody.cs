using Godot;
using Newtonsoft.Json;

/// <summary>
///   Wraps a boilerplate implementation for syncing the position and rotation of a Rigidbody, with the option
///   of choosing which axes to sync to maximize bandwidth usage.
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

    private Tween? tween;

    [JsonIgnore]
    public Spatial EntityNode => this;

    [JsonIgnore]
    public AliveMarker AliveMarker { get; } = new();

    public abstract string ResourcePath { get; }

    public uint NetworkEntityId { get; set; }

    public virtual void NetworkTick(float delta)
    {
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

        var position = new Vector3(xPos, yPos, zPos);
        var rotation = new Vector3(xRot, yRot, zRot);

        if (tween == null)
        {
            tween = new Tween();
            AddChild(tween);
        }

        tween.InterpolateProperty(this, "global_translation", null, position, 0.1f);
        tween.Start();

        // Tweening rotation results in a very weird rotation
        GlobalRotation = rotation;
    }

    public virtual void PackSpawnState(PackedBytesBuffer buffer)
    {
        buffer.WriteVariant(GlobalTranslation);
    }

    public virtual void OnNetworkSpawn(PackedBytesBuffer buffer, GameProperties currentGame)
    {
        Translation = (Vector3)buffer.ReadVariant();
    }

    public virtual void OnDestroyed()
    {
        AliveMarker.Alive = false;
    }
}
