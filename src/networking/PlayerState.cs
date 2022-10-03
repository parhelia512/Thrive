using Godot;

public class PlayerState
{
    public enum Status
    {
        InGame,
        Lobby,
        ReadyForSession,
    }

    public string Name { get; set; } = string.Empty;

    public Status CurrentStatus { get; set; } = Status.Lobby;

    // TODO: add more important properties...

    public string GetStatusReadable()
    {
        switch (CurrentStatus)
        {
            case Status.InGame:
                return TranslationServer.Translate("IN_GAME");
            case Status.Lobby:
            case Status.Lobby | Status.ReadyForSession:
                return TranslationServer.Translate("LOBBY");
            default:
                return TranslationServer.Translate("N_A");
        }
    }
}
