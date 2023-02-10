using System;

// ReSharper disable InconsistentNaming
/// <summary>
///   Just a basic 2 component integer vector for use before we get Godot.Vector2i
/// </summary>
public struct Int2 : IEquatable<Int2>
{
    public int X;
    public int Y;

    public Int2(int x, int y)
    {
        X = x;
        Y = y;
    }

    // Unary operators
    public static Int2 operator +(Int2 p)
    {
        return p;
    }

    public static Int2 operator -(Int2 p)
    {
        return new Int2(-p.X, -p.Y);
    }

    // Vector-Scalar operators
    public static Int2 operator /(Int2 p, int i)
    {
        return new Int2(p.X / i, p.Y / i);
    }

    public static Int2 operator *(Int2 p, int i)
    {
        return new Int2(p.X * i, p.Y * i);
    }

    public static Int2 operator *(int i, Int2 p)
    {
        return new Int2(p.X * i, p.Y * i);
    }

    // Vector-Vector operators
    public static Int2 operator +(Int2 p1, Int2 p2)
    {
        return new Int2(p1.X + p2.X, p1.Y + p2.Y);
    }

    public static Int2 operator -(Int2 p1, Int2 p2)
    {
        return new Int2(p1.X - p2.X, p1.Y - p2.Y);
    }

    public static Int2 operator *(Int2 p1, Int2 p2)
    {
        return new Int2(p1.X * p2.X, p1.Y * p2.Y);
    }

    public static Int2 operator /(Int2 p1, Int2 p2)
    {
        return new Int2(p1.X / p2.X, p1.Y / p2.Y);
    }

    // Comparators
    public static bool operator >(Int2 p1, Int2 p2)
    {
        return p1.X > p2.X || (p1.X == p2.X && p1.Y > p2.Y);
    }

    public static bool operator <(Int2 p1, Int2 p2)
    {
        return p1.X < p2.X || (p1.X == p2.X && p1.Y < p2.Y);
    }

    public static bool operator >=(Int2 p1, Int2 p2)
    {
        return !(p1 < p2);
    }

    public static bool operator <=(Int2 p1, Int2 p2)
    {
        return !(p1 > p2);
    }

    public static bool operator ==(Int2 left, Int2 right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Int2 left, Int2 right)
    {
        return !(left == right);
    }

    public override bool Equals(object? obj)
    {
        if (obj is Int2 converted)
        {
            return Equals(converted);
        }

        return false;
    }

    public override int GetHashCode()
    {
        return X ^ Y;
    }

    public bool Equals(Int2 other)
    {
        return X == other.X && Y == other.Y;
    }
}
