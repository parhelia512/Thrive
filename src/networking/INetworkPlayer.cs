/// <summary>
///   A networked entity the player can control.
/// </summary>
public interface INetworkPlayer : INetworkEntity
{
    /// <summary>
    ///   The unique network ID self-assigned by the client. In gameplay context, this is used to differentiate
    ///   between player-character entities versus normal in-game entities.
    /// </summary>
    int? PeerId { get; }

    /// <summary>
    ///   TODO: Implement proper client-to-server input handling complete with acks, lag compensation, queues, etc.
    ///   as this current setup can be easily misused by the client to send bogus data.
    /// </summary>
    public void PackInputs(PackedBytesBuffer buffer);

    /// <summary>
    ///   Called server-side for every network tick. <inheritdoc cref="PackInputs"/>
    /// </summary>
    public void OnNetworkInput(PackedBytesBuffer buffer);
}
