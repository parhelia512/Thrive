public interface INetworkSerializable 
{
    /// <summary>
    ///   Packs informations/properties about the object as bytes to be sent across the network.
    /// </summary>
    public void NetworkSerialize(PackedBytesBuffer buffer);

    /// <summary>
    ///   Unpacks informations/properties about the object sent from the network.
    /// </summary>
    public void NetworkDeserialize(PackedBytesBuffer buffer);
}
