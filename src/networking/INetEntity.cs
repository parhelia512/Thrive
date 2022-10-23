using System.Collections.Generic;

[UseThriveSerializer]
public interface INetEntity : IEntity
{
    public uint NetEntityId { get; set; }

    public void NetSyncEveryFrame(Dictionary<string, string> data);

    /// <summary>
    ///   A naive implementation for marshaling entity vars to be sent across network.
    /// </summary>
    /// <remarks>
    ///   TODO: can this possibly be optimized to be far more efficient?
    /// </remarks>
    public Dictionary<string, string> PackState();

    public void OnReplicated();
}
