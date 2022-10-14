using Godot;

public class NetPlayerInfo
{
    public string Name { get; set; } = string.Empty;

    public NetPlayerStatus Status { get; set; } = NetPlayerStatus.Lobby;

    public bool ReadyForSession { get; set; }

    // TODO: add more important properties...

    public string GetEnvironmentReadable()
    {
        switch (Status)
        {
            case NetPlayerStatus.InGame:
                return TranslationServer.Translate("IN_GAME_LOWERCASE");
            case NetPlayerStatus.Lobby:
                return TranslationServer.Translate("LOBBY_LOWERCASE");
            default:
                return TranslationServer.Translate("N_A");
        }
    }

    public string GetEnvironmentReadableShort()
    {
        switch (Status)
        {
            case NetPlayerStatus.InGame:
                return TranslationServer.Translate("G_LETTER");
            case NetPlayerStatus.Lobby:
                return TranslationServer.Translate("L_LETTER");
            default:
                return TranslationServer.Translate("N_A");
        }
    }
}
