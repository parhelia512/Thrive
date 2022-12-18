using System.Collections.Generic;

/// <summary>
///   Interface for networkable entities.
/// </summary>
[UseThriveSerializer]
public interface INetEntity : IEntity
{
    /// <summary>
    ///   The scene path to this entity for replication.
    /// </summary>
    public string ResourcePath { get; }

    /// <summary>
    ///   The unique incremental ID assigned to this entity by the server.
    /// </summary>
    public uint NetEntityId { get; set; }

    /// <summary>
    ///   Only applies if this entity is server-side.
    /// </summary>
    public bool Synchronize { get; set; }

    public void NetworkTick(float delta);

    /// <summary>
    ///   Called client-side for every network tick.
    /// </summary>
    public void OnNetworkSync(Dictionary<string, string> data);

    /// <summary>
    ///   Called client-side when this entity is replicated.
    /// </summary>
    public void OnReplicated(Dictionary<string, string>? data, GameProperties currentGame);

    /// <summary>
    ///   A naive implementation for marshaling entity states to be sent across network.
    /// </summary>
    /// <remarks>
    ///   TODO: can this possibly be optimized to be far more efficient?
    /// </remarks>
    public Dictionary<string, string>? PackStates();

    /// <summary>
    ///   A naive implementation for marshaling replicable vars to be sent across network.
    /// </summary>
    /// <remarks>
    ///   TODO: can this possibly be optimized to be far more efficient?
    /// </remarks>
    public Dictionary<string, string>? PackReplicableVars();
}
