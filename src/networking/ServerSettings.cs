using Godot;

/// <summary>
///   Settings shared both by the server and the client.
/// </summary>
public class ServerSettings
{
    public string Name { get; set; } = TranslationServer.Translate("N_A");

    public string Address { get; set; } = Constants.LOCAL_HOST;

    public int Port { get; set; } = Constants.MULTIPLAYER_DEFAULT_PORT;

    public int MaxPlayers { get; set; } = Constants.MULTIPLAYER_DEFAULT_MAX_PLAYERS;

    public bool UseUPNP { get; set; }

    public MultiplayerGameState SelectedGameMode { get; set; } = MultiplayerGameState.MicrobialArena;

    public override string ToString()
    {
        return $"(Name: {Name}, Address: {Address}, Port: {Port}, MaxPlayers: {MaxPlayers})";
    }

    public string GetGameModeReadable()
    {
        switch (SelectedGameMode)
        {
            case MultiplayerGameState.MicrobialArena:
                return TranslationServer.Translate("MICROBIAL_ARENA");
            default:
                return TranslationServer.Translate("N_A");
        }
    }
}
