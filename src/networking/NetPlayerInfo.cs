using System.Collections.Generic;
using Godot;

public class NetPlayerInfo
{
    public string Name { get; set; } = string.Empty;

    public NetPlayerStatus Status { get; set; } = NetPlayerStatus.Lobby;

    public bool ReadyForSession { get; set; }

    public Dictionary<string, object> Vars { get; set; } = new();

    // TODO: add more important properties...

    public string GetStatusReadable()
    {
        switch (Status)
        {
            case NetPlayerStatus.Active:
                return TranslationServer.Translate("IN_GAME_LOWERCASE");
            case NetPlayerStatus.Lobby:
                return TranslationServer.Translate("LOBBY_LOWERCASE");
            case NetPlayerStatus.Joining:
                return TranslationServer.Translate("JOINING_LOWERCASE");
            case NetPlayerStatus.Leaving:
                return TranslationServer.Translate("LEAVING_LOWERCASE");
            default:
                return TranslationServer.Translate("N_A");
        }
    }

    public string GetStatusReadableShort()
    {
        switch (Status)
        {
            case NetPlayerStatus.Active:
                return TranslationServer.Translate("G_LETTER");
            case NetPlayerStatus.Lobby:
                return TranslationServer.Translate("L_LETTER");
            default:
                return TranslationServer.Translate("N_A");
        }
    }
}
