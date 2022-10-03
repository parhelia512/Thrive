using System;
using System.Collections.Generic;
using Godot;

/// <summary>
///   Includes both the server and client code.
///   TODO: bit of a mess atm...
/// </summary>
public class NetworkManager : Node
{
    public const int DEFAULT_SERVER_ID = 1;

    private static NetworkManager? instance;

    private readonly Dictionary<int, PlayerState> connectedPeers = new();

    private int maxPlayers;

    private NetworkedMultiplayerENet? network;

    private NetworkManager()
    {
        instance = this;
    }

    [Signal]
    public delegate void ConnectionFailed(string reason)
;
    [Signal]
    public delegate void ConnectedPeersChanged();

    [Signal]
    public delegate void Kicked(string reason);

    [Signal]
    public delegate void ChatReceived(string message);

    [Signal]
    public delegate void SpawnRequested(int peerId);

    [Signal]
    public delegate void DespawnRequested(int peerId);

    [Signal]
    public delegate void ReadyForSessionReceived(int peerId, bool ready);

    public static NetworkManager Instance => instance ?? throw new InstanceNotLoadedYetException();

    /// <summary>
    ///   All peers connected in the network excluding self.
    /// </summary>
    public IReadOnlyDictionary<int, PlayerState> ConnectedPeers => connectedPeers;

    public PlayerState? State { get; private set; }

    public bool IsDedicated => GetTree().IsNetworkServer() && State == null;

    public string ServerName { get; private set; } = TranslationServer.Translate("N_A");

    public float TickRateDelay { get; private set; } = Constants.MULTIPLAYER_DEFAULT_TICK_RATE_DELAY_SECONDS;

    [RemoteSync]
    public bool GameInSession { get; private set; }

    public bool Connected => network?.GetConnectionStatus() == NetworkedMultiplayerPeer.ConnectionStatus.Connected;

    public float TimePassedConnecting { get; private set; }

    public override void _Ready()
    {
        GetTree().Connect("network_peer_connected", this, nameof(OnPeerConnected));
        GetTree().Connect("network_peer_disconnected", this, nameof(OnPeerDisconnected));
        GetTree().Connect("server_disconnected", this, nameof(OnServerDisconnected));
        GetTree().Connect("connection_failed", this, nameof(OnConnectionFailed));
    }

    public override void _Process(float delta)
    {
        if (network == null)
            return;

        if (network.GetConnectionStatus() == NetworkedMultiplayerPeer.ConnectionStatus.Connecting)
        {
            TimePassedConnecting += delta;
        }
    }

    /// <summary>
    ///   Creates a dedicated server.
    /// </summary>
    public Error CreateServer(string address, int maxPlayers = Constants.MULTIPLAYER_DEFAULT_MAX_PLAYERS)
    {
        TimePassedConnecting = 0;

        network = new NetworkedMultiplayerENet();

        var error = network.CreateServer(Constants.MULTIPLAYER_DEFAULT_PORT, maxPlayers);
        if (error != Error.Ok)
        {
            PrintErrorWithRole("An error occurred while trying to create server: ", error);
            return error;
        }

        GetTree().NetworkPeer = network;
        network.SetBindIp(address);
        ServerName = address;
        this.maxPlayers = maxPlayers;

        PrintWithRole("Created server with address: ", address, " and max player count: ", maxPlayers);

        return error;
    }

    /// <summary>
    ///   Creates a player hosted server.
    /// </summary>
    public Error CreateServer(string address, string playerName, int maxPlayers = Constants.MULTIPLAYER_DEFAULT_MAX_PLAYERS)
    {
        var error = CreateServer(address, maxPlayers);
        if (error != Error.Ok)
        {
            PrintErrorWithRole("An error occurred while trying to create server host: ", error);
            return error;
        }

        State = new PlayerState
        {
            Name = playerName,
            CurrentStatus = PlayerState.Status.Lobby | PlayerState.Status.ReadyForSession,
        };

        PrintWithRole("Server is created with client as the host");

        return error;
    }

    public Error ConnectToServer(string address, string playerName)
    {
        TimePassedConnecting = 0;

        network = new NetworkedMultiplayerENet();

        GetTree().CheckAndConnect(
            "connected_to_server", this, nameof(OnConnectedToServer), new Godot.Collections.Array { playerName },
            (int)ConnectFlags.Oneshot);

        var error = network.CreateClient(address, Constants.MULTIPLAYER_DEFAULT_PORT);
        if (error != Error.Ok)
        {
            PrintErrorWithRole("An error occurred while trying to create client: ", error);
            return error;
        }

        GetTree().NetworkPeer = network;
        ServerName = address;

        State = new PlayerState { Name = playerName };

        return error;
    }

    public void Disconnect(int id = 0)
    {
        if (!Connected)
            return;

        PrintWithRole("Disconnecting...");
        network?.CloseConnection();
        connectedPeers.Clear();
        GameInSession = false;
    }

    public bool HasPlayer(int peerId)
    {
        return connectedPeers.ContainsKey(peerId);
    }

    public void SetPlayerStatus(PlayerState.Status status)
    {
        if (IsDedicated)
            return;

        RpcId(1, nameof(PeerStatusChanged), GetTree().GetNetworkUniqueId(), status);
    }

    public PlayerState? GetPlayerState(int peerId)
    {
        if (HasPlayer(peerId))
        {
            return connectedPeers[peerId];
        }
        else if (!IsDedicated && GetTree().GetNetworkUniqueId() == peerId)
        {
            return State!;
        }

        return null;
    }

    public void StartGameSession()
    {
        if (!IsDedicated && State!.CurrentStatus == PlayerState.Status.InGame)
            return;

        if (GetTree().IsNetworkServer())
        {
            Rpc(nameof(OnGameStarted));
            Rset(nameof(GameInSession), true);
        }
        else
        {
            if (GameInSession)
                OnGameStarted();
        }
    }

    public void EndGameSession()
    {
        if (!IsDedicated && State!.CurrentStatus != PlayerState.Status.InGame)
            return;

        if (GetTree().IsNetworkServer())
        {
            Rset(nameof(GameInSession), false);
            Rpc(nameof(OnGameEnded));
        }
        else
        {
            OnGameEnded();
        }
    }

    public void Kick(int id, string reason)
    {
        RpcId(id, nameof(OnKicked), reason);
    }

    /// <summary>
    ///   Sends a chat message to all peers.
    /// </summary>
    public void BroadcastChat(string message, bool asSystem = false)
    {
        if (!Connected)
            return;

        Rpc(nameof(OnChatSent), message, asSystem);
    }

    /// <summary>
    ///   Checks if we are a network server and if so sends a chat message to all peers as system.
    /// </summary>
    public void SystemBroadcastChat(string message)
    {
        if (GetTree().IsNetworkServer())
            BroadcastChat(message, true);
    }

    /// <summary>
    ///   Differentiates between print call from server or client. We do this so that they will stand out more
    ///   on the output log.
    /// </summary>
    public void PrintWithRole(params object[] what)
    {
        var str = string.Concat(Array.ConvertAll(what, x => x?.ToString() ?? "null"));
        GD.Print(GetTree().IsNetworkServer() ? "[Server] " : "[Client] ", str);
    }

    public void PrintErrorWithRole(params object[] what)
    {
        var str = string.Concat(Array.ConvertAll(what, x => x?.ToString() ?? "null"));
        GD.PrintErr(GetTree().IsNetworkServer() ? "[Server] " : "[Client] ", str);
    }

    private void OnPeerConnected(int id)
    {
        // Will probaby be useful later.
    }

    private void OnPeerDisconnected(int id)
    {
        PrintWithRole("User ", connectedPeers[id].Name, " (", id, ") has disconnected");

        if (GetTree().IsNetworkServer())
            SystemBroadcastChat($"[b]{connectedPeers[id].Name}[/b] has disconnected.");

        RemovePlayer(id);
    }

    private void OnConnectedToServer(string playerName)
    {
        var id = GetTree().GetNetworkUniqueId();

        PrintWithRole("Connection to ", ServerName, " succeeded, with network ID (", id, ")");

        // TODO: some kind of authentication
        RpcId(1, nameof(AddPlayer), id, playerName);

        TimePassedConnecting = 0;
    }

    private void OnServerDisconnected()
    {
        PrintWithRole("Disconnected from server");

        connectedPeers.Clear();
        GameInSession = false;
        EmitSignal(nameof(ConnectedPeersChanged));
    }

    private void OnConnectionFailed()
    {
        var reason = TranslationServer.Translate("BAD_CONNECTION");
        var log = "bad connection";

        if (Mathf.RoundToInt(TimePassedConnecting) >= Constants.MULTIPLAYER_DEFAULT_TIMEOUT_LIMIT_SECONDS)
        {
            reason = TranslationServer.Translate("TIMEOUT");
            log = "timeout";
        }

        EmitSignal(nameof(ConnectionFailed), reason);

        PrintErrorWithRole("Connection to ", ServerName, " failed: ", log);

        TimePassedConnecting = 0;
    }

    [Remote]
    private void AddPlayer(int id, string playerName)
    {
        if (GetTree().IsNetworkServer())
        {
            if (!IsDedicated)
                RpcId(id, nameof(AddPlayer), GetTree().GetNetworkUniqueId(), State!.Name);

            // TODO: might not be true...
            PrintWithRole("User ", playerName, " (", id, ") has connected");
            SystemBroadcastChat($"[b]{playerName}[/b] has connected.");

            foreach (var peer in connectedPeers)
            {
                RpcId(peer.Key, nameof(AddPlayer), id, playerName);
            }
        }

        if (HasPlayer(id))
            return;

        connectedPeers.Add(id, new PlayerState { Name = playerName });
        EmitSignal(nameof(ConnectedPeersChanged));
    }

    [Remote]
    private void RemovePlayer(int id)
    {
        if (!HasPlayer(id))
            return;

        connectedPeers.Remove(id);
        EmitSignal(nameof(ConnectedPeersChanged));
    }

    [RemoteSync]
    private void PeerStatusChanged(int id, PlayerState.Status status)
    {
        if (IsDedicated)
            return;

        var state = GetPlayerState(id)!;

        if (GetTree().IsNetworkServer())
        {
            foreach (var peer in connectedPeers)
            {
                RpcId(peer.Key, nameof(PeerStatusChanged), id, status);
            }
        }

        var oldStatus = state.CurrentStatus;
        state.CurrentStatus = status;

        // TODO: find a better way to do this!!
        if (oldStatus != PlayerState.Status.InGame && state.CurrentStatus == PlayerState.Status.InGame)
        {
            EmitSignal(nameof(SpawnRequested), id);
            SystemBroadcastChat($"[b]{state.Name}[/b] has joined.");
        }
        else if (oldStatus == PlayerState.Status.InGame && state.CurrentStatus.HasFlag(PlayerState.Status.Lobby))
        {
            EmitSignal(nameof(DespawnRequested), id);
            EmitSignal(nameof(ReadyForSessionReceived), id, state.CurrentStatus.HasFlag(PlayerState.Status.ReadyForSession));
            SystemBroadcastChat($"[b]{state.Name}[/b] has left.");
        }
        else if (oldStatus == PlayerState.Status.Lobby && status.HasFlag(PlayerState.Status.Lobby))
        {
            EmitSignal(nameof(ReadyForSessionReceived), id, true);
            SystemBroadcastChat($"[b]{state.Name}[/b] is ready!");
        }
        else if (oldStatus.HasFlag(PlayerState.Status.ReadyForSession) && status == PlayerState.Status.Lobby)
        {
            EmitSignal(nameof(ReadyForSessionReceived), id, false);
            SystemBroadcastChat($"[b]{state.Name}[/b] is not ready!");
        }
    }

    [Remote]
    private void OnKicked(string reason)
    {
        if (GetTree().GetRpcSenderId() != DEFAULT_SERVER_ID)
        {
            PrintErrorWithRole("Kicking is only permissible from host/server");
            return;
        }

        Disconnect();
        EmitSignal(nameof(Kicked), reason);
    }

    [RemoteSync]
    private void OnGameStarted()
    {
        var microbeStage = (MicrobeStage)SceneManager.Instance.LoadScene(MainGameState.MicrobeStage).Instance();
        SceneManager.Instance.SwitchToScene(microbeStage);

        SetPlayerStatus(PlayerState.Status.InGame);
    }

    [RemoteSync]
    private void OnGameEnded()
    {
        SetPlayerStatus(GameInSession ? PlayerState.Status.Lobby | PlayerState.Status.ReadyForSession : PlayerState.Status.Lobby);

        var scene = SceneManager.Instance.LoadScene("res://src/general/MainMenu.tscn");

        var mainMenu = (MainMenu)scene.Instance();

        mainMenu.IsReturningToMenu = true;
        mainMenu.IsReturningToLobby = true;

        SceneManager.Instance.SwitchToScene(mainMenu);
    }

    [RemoteSync]
    private void OnChatSent(string message, bool asSystem)
    {
        var senderId = GetTree().GetRpcSenderId();
        var senderState = GetPlayerState(senderId);

        var formatted = string.Empty;
        if (senderState == null || (senderId == DEFAULT_SERVER_ID && asSystem))
        {
            formatted = $"[color=#d8d8d8][system]: {message}[/color]";
        }
        else
        {
            formatted = $"[b]({senderState.GetStatusReadable()}) [{senderState.Name}]:[/b] {message}";
        }

        EmitSignal(nameof(ChatReceived), formatted);
    }
}
