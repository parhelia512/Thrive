[UseThriveSerializer]
public interface INetEntity : IEntity
{
    public uint NetEntityId { get; set; }

    public void NetworkSyncEveryFrame(int peerId);
}
