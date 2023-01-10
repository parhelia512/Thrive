/// <summary>
///   Interface for networkable entities.
/// </summary>
public interface INetworkEntity : IEntity, INetworkSerializable
{
    /// <summary>
    ///   The scene path to this entity for remote instantiation.
    /// </summary>
    public string ResourcePath { get; }

    /// <summary>
    ///   The unique incremental entity ID assigned to this entity by the server.
    /// </summary>
    public uint NetworkEntityId { get; set; }

    /// <summary>
    ///   Called client-side BEFORE the replicated entity is added to the scene tree.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     TODO: do away with this if delta encoding is added.
    ///   </para>
    /// </remarks>
    public void OnRemoteSpawn(PackedBytesBuffer buffer, GameProperties currentGame);

    /// <summary>
    ///   <inheritdoc cref="INetworkSerializable.NetworkSerialize"/> Only sent once on spawn BEFORE the replicated
    ///   entity is added to the the scene tree.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     TODO: do away with this if delta encoding is added.
    ///   </para>
    /// </remarks>
    public void PackSpawnState(PackedBytesBuffer buffer);
}
