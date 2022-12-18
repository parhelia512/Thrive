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

    public bool UseUpnp { get; set; }

    public MultiplayerGameMode? SelectedGameMode { get; set; }

    public IGameModeSettings? GameModeSettings { get; set; }

    public override string ToString()
    {
        return $"(Name: {Name}, Address: {Address}, Port: {Port}, MaxPlayers: {MaxPlayers})";
    }
}
