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
    ///   Called every network tick <see cref="NetworkManager.TimeStep"/>.
    /// </summary>
    public void NetworkTick(float delta);

    /// <summary>
    ///   Called client-side BEFORE this entity is added to the scene tree.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     TODO: do away with this once delta encoding is added.
    ///   </para>
    /// </remarks>
    public void OnNetworkSpawn(PackedBytesBuffer buffer, GameProperties currentGame);

    /// <summary>
    ///   <inheritdoc cref="INetSerializable.NetSerialize"/> Only sent once on spawn BEFORE entering the scene tree.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     TODO: do away with this once delta encoding is added.
    ///   </para>
    /// </remarks>
    public void PackSpawnState(PackedBytesBuffer buffer);
}
