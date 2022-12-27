using Godot;

/// <summary>
///   Describes player information in relation to the connected server.
/// </summary>
public class NetworkPlayerInfo : Vars
{
    public string Name { get; set; } = string.Empty;

    public NetworkPlayerStatus Status { get; set; } = NetworkPlayerStatus.Lobby;

    public bool ReadyForSession { get; set; }

    public override void NetworkSerialize(PackedBytesBuffer buffer)
    {
        base.NetworkSerialize(buffer);

        buffer.Write(Name);
        buffer.Write((byte)Status);
        buffer.Write(ReadyForSession);
    }

    public override void NetworkDeserialize(PackedBytesBuffer buffer)
    {
        base.NetworkDeserialize(buffer);

        Name = buffer.ReadString();
        Status = (NetworkPlayerStatus)buffer.ReadByte();
        ReadyForSession = buffer.ReadBoolean();
    }

    public string GetStatusReadable()
    {
        switch (Status)
        {
            case NetworkPlayerStatus.Active:
                return TranslationServer.Translate("IN_GAME_LOWERCASE");
            case NetworkPlayerStatus.Lobby:
                return TranslationServer.Translate("IN_LOBBY_LOWERCASE");
            case NetworkPlayerStatus.Joining:
                return TranslationServer.Translate("JOINING_LOWERCASE");
            case NetworkPlayerStatus.Leaving:
                return TranslationServer.Translate("LEAVING_LOWERCASE");
            default:
                return TranslationServer.Translate("N_A");
        }
    }

    public string GetStatusReadableShort()
    {
        switch (Status)
        {
            case NetworkPlayerStatus.Active:
                return TranslationServer.Translate("G_LETTER");
            case NetworkPlayerStatus.Lobby:
                return TranslationServer.Translate("L_LETTER");
            default:
                return TranslationServer.Translate("N_A");
        }
    }
}
