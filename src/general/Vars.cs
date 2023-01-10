using System.Collections.Generic;

/// <summary>
///   Network serializable variables.
/// </summary>
public class Vars : INetworkSerializable
{
    /// <summary>
    ///   Currently serializable up to 255 entries.
    /// </summary>
    private Dictionary<string, object> entries = new();

    /// <summary>
    ///   Guaranteed to accept primitive types.
    /// </summary>
    public void SetVar(string key, object variant)
    {
        entries[key] = variant;
    }

    public T GetVar<T>(string key)
    {
        return (T)entries[key];
    }

    public bool TryGetVar<T>(string key, out T value)
    {
        if (entries.TryGetValue(key, out object retrieved))
        {
            value = (T)retrieved;
            return true;
        }

        value = default(T)!;
        return false;
    }

    public virtual void NetworkSerialize(PackedBytesBuffer buffer)
    {
        buffer.Write((byte)entries.Count);
        foreach (var entry in entries)
        {
            buffer.Write(entry.Key);
            buffer.WriteVariant(entry.Value);
        }
    }

    public virtual void NetworkDeserialize(PackedBytesBuffer buffer)
    {
        var nEntries = buffer.ReadByte();
        for (int i = 0; i < nEntries; ++i)
        {
            var key = buffer.ReadString();
            var value = buffer.ReadVariant();
            entries[key] = value;
        }
    }
}
