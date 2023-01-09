using Godot;

public struct NetworkMicrobeInput : INetworkInputBatch
{
    public Vector3 LookAtPoint { get; set; }
    public Vector3 MovementDirection { get; set; }
    public bool Engulf { get; set; }
    public bool EmitToxin { get; set; }
    public bool SecreteSlime { get; set; }

    public void NetworkSerialize(PackedBytesBuffer buffer)
    {
        // 21 bytes

        buffer.Write(LookAtPoint.x);
        buffer.Write(LookAtPoint.z);
        buffer.Write(MovementDirection.x);
        buffer.Write(MovementDirection.z);

        var bools = new bool[3] { Engulf, EmitToxin, SecreteSlime };
        buffer.Write(bools.ToByte());
    }

    public void NetworkDeserialize(PackedBytesBuffer buffer)
    {
        LookAtPoint = new Vector3(buffer.ReadSingle(), 0, buffer.ReadSingle());
        MovementDirection = new Vector3(buffer.ReadSingle(), 0, buffer.ReadSingle());

        var bools = buffer.ReadByte();
        Engulf = bools.ToBoolean(0);
        EmitToxin = bools.ToBoolean(1);
        SecreteSlime = bools.ToBoolean(2);
    }
}
