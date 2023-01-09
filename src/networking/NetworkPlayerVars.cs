/// <summary>
///   A very tiny wrapper around <see cref="Vars"/> containing the <see cref="INetworkEntity.NetworkEntityId"/>
///   for a player.
/// </summary>
public class NetworkPlayerVars : Vars
{
    /// <summary>
    ///   The network entity id for this player's character.
    /// </summary>
    public uint EntityId { get; set; }

    public override void NetworkSerialize(PackedBytesBuffer buffer)
    {
        base.NetworkSerialize(buffer);

        buffer.Write(EntityId);
    }

    public override void NetworkDeserialize(PackedBytesBuffer buffer)
    {
        base.NetworkDeserialize(buffer);

        EntityId = buffer.ReadUInt32();
    }
}
