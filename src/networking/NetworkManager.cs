using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   Includes both the server and client code.
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
    public delegate void ServerStateUpdated();

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
    ///   All peers connected in the network (EXCLUDING SELF).
    /// </summary>
    public IReadOnlyDictionary<int, PlayerState> ConnectedPeers => connectedPeers;

    public PlayerState? State { get; private set; }

    public bool IsDedicated => GetTree().IsNetworkServer() && State == null;

    public ServerSettings? Settings { get; private set; }

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
        GetTree().Multiplayer.Connect("network_peer_packet", this, nameof(OnReceivedPacket));
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
    public Error CreateServer(int maxPlayers = Constants.MULTIPLAYER_DEFAULT_MAX_PLAYERS)
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
        Settings = new ServerSettings { Name = IP.ResolveHostname(OS.GetEnvironment("COMPUTERNAME"), IP.Type.Ipv4) };
        this.maxPlayers = maxPlayers;

        PrintWithRole("Created server with address: ", Settings.Name, " and max player count: ", maxPlayers);

        return error;
    }

    /// <summary>
    ///   Creates a player hosted server.
    /// </summary>
    public Error CreateServer(string playerName, int maxPlayers = Constants.MULTIPLAYER_DEFAULT_MAX_PLAYERS)
    {
        var error = CreateServer(maxPlayers);
        if (error != Error.Ok)
        {
            PrintErrorWithRole("An error occurred while trying to create server host: ", error);
            return error;
        }

        State = new PlayerState { Name = playerName };
        NotifyPeerStatusChange(
            GetTree().GetNetworkUniqueId(), PlayerState.Status.Lobby | PlayerState.Status.ReadyForSession);

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

        RpcId(1, nameof(NotifyPeerStatusChange), GetTree().GetNetworkUniqueId(), status);
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
        if (GetTree().IsNetworkServer())
        {
            Rpc(nameof(NotifyGameStart));
            Rset(nameof(GameInSession), true);
        }
        else
        {
            if (GameInSession)
                NotifyGameStart();
        }
    }

    public void EndGameSession()
    {
        if (GetTree().IsNetworkServer())
        {
            Rset(nameof(GameInSession), false);
            Rpc(nameof(NotifyGameEnd));
        }
        else
        {
            NotifyGameEnd();
        }
    }

    public void Kick(int id, string reason)
    {
        RpcId(id, nameof(NotifyKick), reason);
    }

    /// <summary>
    ///   Sends a chat message to all peers.
    /// </summary>
    public void BroadcastChat(string message, bool asSystem = false)
    {
        if (!Connected)
            return;

        Rpc(nameof(NotifyChatSend), message, asSystem);
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

        NotifyPlayerDisconnected(id);
    }

    private void OnConnectedToServer(string playerName)
    {
        var id = GetTree().GetNetworkUniqueId();

        // TODO: some kind of authentication
        RpcId(1, nameof(NotifyPlayerConnected), id, playerName);

        TimePassedConnecting = 0;
    }

    private void OnServerDisconnected()
    {
        PrintWithRole("Disconnected from server");

        connectedPeers.Clear();
        GameInSession = false;
        EmitSignal(nameof(ServerStateUpdated));
    }

    private void OnConnectionFailed()
    {
        var reason = TranslationServer.Translate("BAD_CONNECTION");

        if (Mathf.RoundToInt(TimePassedConnecting) >= Constants.MULTIPLAYER_DEFAULT_TIMEOUT_LIMIT_SECONDS)
            reason = TranslationServer.Translate("TIMEOUT");

        EmitSignal(nameof(ConnectionFailed), reason);

        TimePassedConnecting = 0;
    }

    private void OnReceivedPacket(int senderId, byte[] packet)
    {
        ServerSettings? lobbyData;
        using (var stream = new MemoryStream(packet))
        {
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                lobbyData = JsonSerializer.Create().Deserialize(reader, typeof(ServerSettings)) as ServerSettings;
        }

        Settings = lobbyData;
    }

    [Remote]
    private void NotifyPlayerConnected(int id, string playerName)
    {
        if (GetTree().IsNetworkServer())
        {
            if (GameInSession)
                RsetId(id, nameof(GameInSession), true);

            GetTree().Multiplayer.SendBytes(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Settings)), id);

            if (!IsDedicated)
            {
                RpcId(id, nameof(NotifyPlayerConnected), GetTree().GetNetworkUniqueId(), State!.Name);
                RpcId(id, nameof(NotifyPeerStatusChange), GetTree().GetNetworkUniqueId(), State!.CurrentStatus);
            }

            // TODO: might not be true...
            PrintWithRole("User ", playerName, " (", id, ") has connected");
            SystemBroadcastChat($"[b]{playerName}[/b] has connected.");

            foreach (var peer in connectedPeers)
            {
                RpcId(peer.Key, nameof(NotifyPlayerConnected), id, playerName);
                RpcId(id, nameof(NotifyPlayerConnected), peer.Key, peer.Value.Name);
            }
        }

        if (HasPlayer(id))
            return;

        connectedPeers.Add(id, new PlayerState { Name = playerName });
        EmitSignal(nameof(ServerStateUpdated));

        GetTree().RefuseNewNetworkConnections = GetTree().IsNetworkServer() && connectedPeers.Count + 1 >= maxPlayers;
    }

    [Remote]
    private void NotifyPlayerDisconnected(int id)
    {
        if (!HasPlayer(id))
            return;

        connectedPeers.Remove(id);
        EmitSignal(nameof(ServerStateUpdated));
    }

    [RemoteSync]
    private void NotifyPeerStatusChange(int id, PlayerState.Status status)
    {
        if (IsDedicated)
            return;

        var state = GetPlayerState(id)!;

        if (GetTree().IsNetworkServer())
        {
            foreach (var peer in connectedPeers)
            {
                RpcId(peer.Key, nameof(NotifyPeerStatusChange), id, status);
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
            EmitSignal(nameof(ServerStateUpdated));
            SystemBroadcastChat($"[b]{state.Name}[/b] has left.");

            if (!GetTree().IsNetworkServer() && id == DEFAULT_SERVER_ID)
                RpcId(1, nameof(NotifyPeerStatusChange), GetTree().GetNetworkUniqueId(), state.CurrentStatus);
        }
        else if (oldStatus == PlayerState.Status.Lobby && status.HasFlag(PlayerState.Status.ReadyForSession))
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
    private void NotifyKick(string reason)
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
    private void NotifyGameStart()
    {
        if (!IsDedicated && State!.CurrentStatus == PlayerState.Status.InGame)
            return;

        var microbeStage = (MicrobeStage)SceneManager.Instance.LoadScene(MainGameState.MicrobeStage).Instance();
        SceneManager.Instance.SwitchToScene(microbeStage);

        SetPlayerStatus(PlayerState.Status.InGame);
    }

    [RemoteSync]
    private void NotifyGameEnd()
    {
        if (!IsDedicated && State!.CurrentStatus.HasFlag(PlayerState.Status.Lobby))
            return;

        SetPlayerStatus(
            GameInSession ? PlayerState.Status.Lobby | PlayerState.Status.ReadyForSession : PlayerState.Status.Lobby);

        var scene = SceneManager.Instance.LoadScene("res://src/general/MainMenu.tscn");

        var mainMenu = (MainMenu)scene.Instance();

        mainMenu.IsReturningToMenu = true;
        mainMenu.IsReturningToLobby = true;

        SceneManager.Instance.SwitchToScene(mainMenu);
    }

    [RemoteSync]
    private void NotifyChatSend(string message, bool asSystem)
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
