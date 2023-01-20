using System;
using Godot;

public struct NetworkInputVars : INetworkSerializable, IEquatable<NetworkInputVars>
{
    public ushort Id { get; set; }

    public float Delta { get; set; }

    public Vector3 WorldLookAtPoint { get; set; }

    public Vector3 MovementDirection { get; set; }

    /// <summary>
    ///   Bitmask of inputs.
    /// </summary>
    public byte Bools { get; set; }

    public static bool operator ==(NetworkInputVars left, NetworkInputVars right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(NetworkInputVars left, NetworkInputVars right)
    {
        return !left.Equals(right);
    }

    public void NetworkSerialize(PackedBytesBuffer buffer)
    {
        buffer.Write(Id);
        buffer.Write(Delta);
        buffer.Write(WorldLookAtPoint);
        buffer.Write(MovementDirection);
        buffer.Write(Bools);
    }

    public void NetworkDeserialize(PackedBytesBuffer buffer)
    {
        Id = buffer.ReadUInt16();
        Delta = buffer.ReadSingle();
        WorldLookAtPoint = buffer.ReadVector3();
        MovementDirection = buffer.ReadVector3();
        Bools = buffer.ReadByte();
    }

    public bool Equals(NetworkInputVars other)
    {
        return WorldLookAtPoint.IsEqualApprox(other.WorldLookAtPoint) &&
            MovementDirection.IsEqualApprox(other.MovementDirection) &&
            Bools == other.Bools;
    }

    public override bool Equals(object obj)
    {
        return obj is NetworkInputVars input && Equals(input);
    }

    public override int GetHashCode()
    {
        int hashCode = -1311921306;
        hashCode = hashCode * -1521134295 + Id.GetHashCode();
        hashCode = hashCode * -1521134295 + Delta.GetHashCode();
        hashCode = hashCode * -1521134295 + WorldLookAtPoint.GetHashCode();
        hashCode = hashCode * -1521134295 + MovementDirection.GetHashCode();
        hashCode = hashCode * -1521134295 + Bools.GetHashCode();
        return hashCode;
    }
}
