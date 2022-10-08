using Godot;

public class PlayerState
{
    public enum Environment
    {
        InGame,
        Lobby,
        LeavingGame,
    }

    public string Name { get; set; } = string.Empty;

    public Environment CurrentEnvironment { get; set; } = Environment.Lobby;

    public bool ReadyForSession { get; set; }

    // TODO: add more important properties...

    public string GetEnvironmentReadable()
    {
        switch (CurrentEnvironment)
        {
            case Environment.InGame:
                return TranslationServer.Translate("IN_GAME");
            case Environment.Lobby:
                return TranslationServer.Translate("LOBBY");
            default:
                return TranslationServer.Translate("N_A");
        }
    }
}
