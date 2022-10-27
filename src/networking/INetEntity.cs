using System.Collections.Generic;

/// <summary>
///   Interface for networkable entities.
/// </summary>
[UseThriveSerializer]
public interface INetEntity : IEntity
{
    public uint NetEntityId { get; set; }

    /// <summary>
    ///   Only applies if this entity is server-side.
    /// </summary>
    public bool Synchronize { get; set; }

    /// <summary>
    ///   Called client-side for every network tick.
    /// </summary>
    public void OnNetworkSync(Dictionary<string, string> data);

    /// <summary>
    ///   Called server-side for every network tick.
    /// </summary>
    public void OnNetworkInput(Dictionary<string, string> data);

    /// <summary>
    ///   A naive implementation for marshaling entity vars to be sent across network.
    /// </summary>
    /// <remarks>
    ///   TODO: can this possibly be optimized to be far more efficient?
    /// </remarks>
    public Dictionary<string, string>? PackStates();

    /// <summary>
    ///   A naive implementation for marshaling entity inputs to be sent to the server.
    /// </summary>
    /// <remarks>
    ///   TODO: can this possibly be optimized to be far more efficient?
    /// </remarks>
    public Dictionary<string, string>? PackInputs();

    public void OnReplicated();
}
