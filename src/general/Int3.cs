using System;

// ReSharper disable InconsistentNaming
/// <summary>
///   Just a basic 3 component integer vector for use before we get Godot.Vector3i
/// </summary>
public struct Int3 : IEquatable<Int3>
{
    public int X;
    public int Y;
    public int Z;

    public Int3(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static bool operator ==(Int3 left, Int3 right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Int3 left, Int3 right)
    {
        return !(left == right);
    }

    public override bool Equals(object? obj)
    {
        if (obj is Int3 other)
        {
            return Equals(other);
        }

        return false;
    }

    public override int GetHashCode()
    {
        return X ^ Y ^ Z;
    }

    public bool Equals(Int3 other)
    {
        return X == other.X && Y == other.Y && Z == other.Z;
    }
}
