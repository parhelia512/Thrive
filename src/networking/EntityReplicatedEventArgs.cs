using System;

public class EntityReplicatedEventArgs : EventArgs
{
    public EntityReplicatedEventArgs(uint id, INetEntity entity, int serverEntityCount)
    {
        ID = id;
        Entity = entity;
        ServerEntityCount = serverEntityCount;
    }

    /// <summary>
    ///   The networked entity's id.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    ///   The networked entity.
    /// </summary>
    public INetEntity Entity { get; set; }

    /// <summary>
    ///   The number of entities existed server-side.
    /// </summary>
    public int ServerEntityCount { get; set; }
}
