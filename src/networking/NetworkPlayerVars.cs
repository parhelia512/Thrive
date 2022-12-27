/// <summary>
///   A very tiny wrapper around <see cref="Vars"/> containing the <see cref="INetworkEntity.NetworkEntityId"/>
///   for a player.
/// </summary>
public class NetworkPlayerVars : Vars
{
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
