using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   Includes both the server and client code.
/// </summary>
public class NetworkManager : Node
{
    public const int DEFAULT_SERVER_ID = 1;

    private static NetworkManager? instance;

    private readonly Dictionary<int, NetPlayerInfo> playerList = new();

    private NetworkedMultiplayerENet? peer;
    private UPNP? upnp;

    private NetworkManager()
    {
        instance = this;
    }

    [Signal]
    public delegate void UPNPCallResultReceived(UPNP.UPNPResult result, UPNPActionStep step);

    [Signal]
    public delegate void RegistrationToServerResultReceived(int peerId, RegistrationToServerResult result);

    [Signal]
    public delegate void ConnectionFailed(string reason);

    [Signal]
    public delegate void ServerStateUpdated();

    [Signal]
    public delegate void Kicked(string reason);

    [Signal]
    public delegate void ChatReceived(string message);

    [Signal]
    public delegate void PlayerJoined(int peerId);

    [Signal]
    public delegate void PlayerLeft(int peerId);

    [Signal]
    public delegate void ReadyForSessionReceived(int peerId, bool ready);

    [Signal]
    public delegate void PlayerStatusChanged(int peerId, NetPlayerStatus status);

    public enum UPNPActionStep
    {
        Setup,
        PortMapping,
    }

    public enum RegistrationToServerResult
    {
        Success,
        ServerFull,
    }

    public static NetworkManager Instance => instance ?? throw new InstanceNotLoadedYetException();

    public ServerSettings? Settings { get; private set; }

    public bool Connected => peer?.GetConnectionStatus() == NetworkedMultiplayerPeer.ConnectionStatus.Connected;

    public float UpdateRateDelay { get; set; } = Constants.MULTIPLAYER_DEFAULT_UPDATE_RATE_DELAY_SECONDS;

    public float TimePassedConnecting { get; private set; }

    /// <summary>
    ///   All peers connected in the network (INCLUDING SELF).
    /// </summary>
    public IReadOnlyDictionary<int, NetPlayerInfo> PlayerList => playerList;

    public NetPlayerInfo? Player => GetPlayerState(GetTree().GetNetworkUniqueId());

    public bool IsDedicated => GetTree().IsNetworkServer() && !HasPlayer(1);

    [RemoteSync]
    public bool GameInSession { get; private set; }

    public GameProperties? CurrentGame { get; private set; }

    public override void _Ready()
    {
        GetTree().Connect("network_peer_connected", this, nameof(OnPeerConnected));
        GetTree().Connect("network_peer_disconnected", this, nameof(OnPeerDisconnected));
        GetTree().Connect("server_disconnected", this, nameof(OnServerDisconnected));
        GetTree().Connect("connection_failed", this, nameof(OnConnectionFailed));

        Connect(nameof(UPNPCallResultReceived), this, nameof(OnUPNPCallResultReceived));
    }

    public override void _Process(float delta)
    {
        if (peer == null)
            return;

        if (peer.GetConnectionStatus() == NetworkedMultiplayerPeer.ConnectionStatus.Connecting)
        {
            TimePassedConnecting += delta;
        }
    }

    /// <summary>
    ///   Creates a server without creating player for this peer.
    /// </summary>
    public Error CreateServer(ServerSettings settings)
    {
        TimePassedConnecting = 0;

        peer = new NetworkedMultiplayerENet();
        peer.SetBindIp(settings.Address);

        // TODO: enable DTLS for secure transport?

        var error = peer.CreateServer(settings.Port, Constants.MULTIPLAYER_DEFAULT_MAX_CLIENTS);
        if (error != Error.Ok)
        {
            PrintErrorWithRole("An error occurred while trying to create server: ", error);
            return error;
        }

        Settings = settings;
        CurrentGame = GameProperties.StartNewMicrobeGame(new WorldGenerationSettings());

        GetTree().NetworkPeer = peer;

        PrintWithRole("Created server with the following settings ", settings);

        return error;
    }

    /// <summary>
    ///   Creates a server while creating player for this peer.
    /// </summary>
    public Error CreatePlayerHostedServer(string playerName, ServerSettings settings)
    {
        var result = CreateServer(settings);
        if (result != Error.Ok)
            return result;

        if (settings.UseUPNP)
        {
            // Automatic port mapping/forwarding with UPnP
            TaskExecutor.Instance.AddTask(new Task(() => SetupUPNP()));
        }

        var id = GetTree().GetNetworkUniqueId();
        NotifyPlayerConnected(id, playerName);

        PrintWithRole("Server is player hosted");

        return result;
    }

    public Error ConnectToServer(string address, int port, string playerName)
    {
        TimePassedConnecting = 0;

        peer = new NetworkedMultiplayerENet();

        // TODO: enable DTLS for secure transport?

        GetTree().CheckAndConnect(
            "connected_to_server", this, nameof(OnConnectedToServer), new Godot.Collections.Array { playerName },
            (int)ConnectFlags.Oneshot);

        var result = peer.CreateClient(address, port);
        if (result != Error.Ok)
        {
            PrintErrorWithRole("An error occurred while trying to create client: ", result);
            return result;
        }

        GetTree().NetworkPeer = peer;

        return result;
    }

    public void Disconnect()
    {
        PrintWithRole("Disconnecting...");
        peer?.CloseConnection();

        if (upnp?.GetDeviceCount() > 0)
            upnp?.DeletePortMapping(Settings!.Port);

        playerList.Clear();
        GameInSession = false;
    }

    public bool HasPlayer(int peerId)
    {
        return playerList.ContainsKey(peerId);
    }

    public NetPlayerInfo? GetPlayerState(int peerId)
    {
        playerList.TryGetValue(peerId, out NetPlayerInfo result);
        return result;
    }

    public void StartGame()
    {
        if (!GetTree().IsNetworkServer() && !GameInSession)
            return;

        if (!IsDedicated && Player!.Status == NetPlayerStatus.InGame)
            return;

        if (GetTree().IsNetworkServer() && !GameInSession)
            Rset(nameof(GameInSession), true);

        NotifyWorldLoad();

        if (GetTree().IsNetworkServer())
        {
            foreach (var player in playerList)
            {
                if (player.Key == DEFAULT_SERVER_ID)
                    continue;

                if (player.Value.Status == NetPlayerStatus.Lobby)
                    RpcId(player.Key, nameof(NotifyWorldLoad));
            }
        }
    }

    public void EndGame()
    {
        if (!IsDedicated && Player!.Status == NetPlayerStatus.Lobby)
            return;

        if (GetTree().IsNetworkServer() && GameInSession)
            Rset(nameof(GameInSession), false);

        NotifyWorldExit();

        if (GetTree().IsNetworkServer())
        {
            foreach (var player in playerList)
            {
                if (player.Key == DEFAULT_SERVER_ID)
                    continue;

                if (player.Value.Status == NetPlayerStatus.InGame)
                    RpcId(player.Key, nameof(NotifyWorldExit));
            }
        }
    }

    public void SetReadyForSessionStatus(bool ready)
    {
        if (GetTree().IsNetworkServer())
            return;

        RpcId(1, nameof(NotifyReadyForSessionStatusChange), GetTree().GetNetworkUniqueId(), ready);
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
        var serverOrHost = IsDedicated ? "[Server] " : "[Host] ";
        GD.Print(GetTree().IsNetworkServer() ? serverOrHost : "[Client] ", str);
    }

    public void PrintErrorWithRole(params object[] what)
    {
        var str = string.Concat(Array.ConvertAll(what, x => x?.ToString() ?? "null"));
        var serverOrHost = IsDedicated ? "[Server] " : "[Host] ";
        GD.PrintErr(GetTree().IsNetworkServer() ? serverOrHost : "[Client] ", str);
    }

    private void SetupUPNP()
    {
        upnp ??= new UPNP();

        var result = (UPNP.UPNPResult)upnp.Discover();

        if (result != UPNP.UPNPResult.Success)
            PrintErrorWithRole("UPnP devices discovery failed: ", result);

        EmitSignal(nameof(UPNPCallResultReceived), result, UPNPActionStep.Setup);
    }

    private void AddPortMapping(int port)
    {
        if (upnp == null)
            return;

        if (upnp.GetDeviceCount() <= 0)
            return;

        if (upnp.GetGateway()?.IsValidGateway() == true)
        {
            // TODO: not tested and not sure if this really works since I can't seem to get my router's UPnP to work?
            var pmResult = (UPNP.UPNPResult)upnp.AddPortMapping(port, 0, "ThriveGame");

            // TODO: error handling

            EmitSignal(nameof(UPNPCallResultReceived), pmResult, UPNPActionStep.PortMapping);
            return;
        }

        EmitSignal(nameof(UPNPCallResultReceived), UPNP.UPNPResult.Success, UPNPActionStep.PortMapping);
    }

    private void OnPeerConnected(int id)
    {
        // Will probaby be useful later.
    }

    private void OnPeerDisconnected(int id)
    {
        if (!HasPlayer(id))
            return;

        PrintWithRole("User ", GetPlayerState(id)!.Name, " (", id, ") has disconnected");

        SystemBroadcastChat($"[b]{GetPlayerState(id)!.Name}[/b] has disconnected.");

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

        playerList.Clear();
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

    private void OnUPNPCallResultReceived(UPNP.UPNPResult result, UPNPActionStep step)
    {
        switch (step)
        {
            case UPNPActionStep.Setup:
            {
                if (result == UPNP.UPNPResult.Success)
                    TaskExecutor.Instance.AddTask(new Task(() => AddPortMapping(Settings!.Port)));

                break;
            }
        }
    }

    [RemoteSync]
    private void NotifyPlayerConnected(int id, string playerName)
    {
        if (GetTree().IsNetworkServer())
        {
            if (playerList.Count >= Settings!.MaxPlayers)
            {
                NotifyRegistrationToServerResult(id, RegistrationToServerResult.ServerFull);
                peer!.DisconnectPeer(id);
                return;
            }

            if (GameInSession)
                RsetId(id, nameof(GameInSession), true);

            if (!IsDedicated)
            {
                RpcId(id, nameof(NotifyPlayerConnected), GetTree().GetNetworkUniqueId(), Player!.Name);
                RpcId(id, nameof(NotifyPlayerStatusChange), GetTree().GetNetworkUniqueId(), Player.Status);
            }

            foreach (var player in playerList)
            {
                if (player.Key == DEFAULT_SERVER_ID)
                    continue;

                RpcId(player.Key, nameof(NotifyPlayerConnected), id, playerName);
                RpcId(id, nameof(NotifyPlayerConnected), player.Key, player.Value.Name);
            }

            if (id != DEFAULT_SERVER_ID)
            {
                RpcId(id, nameof(NotifyPlayerConnected), id, playerName);
                RpcId(id, nameof(NotifyServerConfigs),
                    JsonConvert.SerializeObject(Settings),
                    ThriveJsonConverter.Instance.SerializeObject(CurrentGame!));
            }

            // TODO: might not be true...
            PrintWithRole("User ", playerName, " (", id, ") has connected");
            SystemBroadcastChat($"[b]{playerName}[/b] has connected.");
        }

        if (HasPlayer(id))
            return;

        playerList.Add(id, new NetPlayerInfo { Name = playerName });
        EmitSignal(nameof(ServerStateUpdated));

        if (GetTree().IsNetworkServer())
        {
            // Tell all peers (and ourselves if this is client hosted) that a new peer have
            // been successfully registered to the server
            NotifyRegistrationToServerResult(id, RegistrationToServerResult.Success);
        }
    }

    [Remote]
    private void NotifyPlayerDisconnected(int id)
    {
        if (!HasPlayer(id))
            return;

        playerList.Remove(id);
        EmitSignal(nameof(ServerStateUpdated));
    }

    [Remote]
    private void NotifyServerConfigs(string settings, string currentGame)
    {
        try
        {
            Settings = JsonConvert.DeserializeObject<ServerSettings>(settings);
            CurrentGame = ThriveJsonConverter.Instance.DeserializeObject<GameProperties>(currentGame);
        }
        catch (Exception e)
        {
            PrintErrorWithRole("Error occured while reading server configurations: ", e);
        }
    }

    [RemoteSync]
    private void NotifyRegistrationToServerResult(int peerId, RegistrationToServerResult result)
    {
        if (GetTree().IsNetworkServer())
        {
            foreach (var player in playerList)
            {
                if (player.Key != DEFAULT_SERVER_ID)
                    RpcId(player.Key, nameof(NotifyRegistrationToServerResult), peerId, result);
            }
        }

        EmitSignal(nameof(RegistrationToServerResultReceived), peerId, result);
    }

    [RemoteSync]
    private void NotifyPlayerStatusChange(int id, NetPlayerStatus environment)
    {
        if (GetTree().IsNetworkServer())
        {
            foreach (var player in playerList)
            {
                if (player.Key == DEFAULT_SERVER_ID)
                    continue;

                RpcId(player.Key, nameof(NotifyPlayerStatusChange), id, environment);
            }
        }

        var state = GetPlayerState(id);
        if (state != null)
        {
            state.Status = environment;
            EmitSignal(nameof(PlayerStatusChanged), id, environment);
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
    private void NotifyReadyForSessionStatusChange(int peerId, bool ready)
    {
        if (GetTree().IsNetworkServer())
        {
            foreach (var player in playerList)
            {
                if (player.Key == DEFAULT_SERVER_ID)
                    continue;

                RpcId(player.Key, nameof(NotifyReadyForSessionStatusChange), peerId, ready);
            }
        }

        var state = GetPlayerState(peerId)!;
        state.ReadyForSession = ready;

        EmitSignal(nameof(ReadyForSessionReceived), peerId, ready);
    }

    [Remote]
    private void NotifyWorldLoad()
    {
        Rpc(nameof(NotifyWorldPreLoad), GetTree().GetNetworkUniqueId());

        TransitionManager.Instance.AddSequence(ScreenFade.FadeType.FadeOut, 0.4f, () =>
        {
            PackedScene scene = null!;

            switch (Settings?.SelectedGameMode)
            {
                case MultiplayerGameMode.MicrobialArena:
                    scene = SceneManager.Instance.LoadScene(MultiplayerGameMode.MicrobialArena);
                    break;
            }

            SceneManager.Instance.SwitchToScene(scene.Instance());
            Rpc(nameof(NotifyWorldPostLoad), GetTree().GetNetworkUniqueId());
        });
    }

    [Remote]
    private void NotifyWorldExit()
    {
        Rpc(nameof(NotifyWorldPreExit), GetTree().GetNetworkUniqueId());

        TransitionManager.Instance.AddSequence(ScreenFade.FadeType.FadeOut, 0.3f, () =>
        {
            var menu = SceneManager.Instance.ReturnToMenu();
            menu.OpenMultiplayerMenu(MultiplayerGUI.Submenu.Lobby);
            Rpc(nameof(NotifyWorldPostExit), GetTree().GetNetworkUniqueId());
        });
    }

    [RemoteSync]
    private void NotifyWorldPreLoad(int peerId)
    {
        var playerInfo = GetPlayerState(peerId);

        if (playerInfo == null)
            return;

        playerInfo.Status = NetPlayerStatus.JoiningGame;
        EmitSignal(nameof(PlayerStatusChanged), peerId, playerInfo.Status);
    }

    [RemoteSync]
    private void NotifyWorldPostLoad(int peerId)
    {
        var playerInfo = GetPlayerState(peerId);

        if (playerInfo == null)
            return;

        playerInfo.Status = NetPlayerStatus.InGame;
        EmitSignal(nameof(PlayerStatusChanged), peerId, playerInfo.Status);
        EmitSignal(nameof(PlayerJoined), peerId);

        SystemBroadcastChat($"[b]{playerInfo.Name}[/b] has joined.");
    }

    [RemoteSync]
    private void NotifyWorldPreExit(int peerId)
    {
        var playerInfo = GetPlayerState(peerId);

        if (playerInfo == null)
            return;

        playerInfo.Status = NetPlayerStatus.LeavingGame;
        EmitSignal(nameof(PlayerStatusChanged), peerId, playerInfo.Status);
        EmitSignal(nameof(PlayerLeft), peerId);
    }

    [RemoteSync]
    private void NotifyWorldPostExit(int peerId)
    {
        var playerInfo = GetPlayerState(peerId);

        if (playerInfo == null)
            return;

        playerInfo.Status = NetPlayerStatus.Lobby;
        EmitSignal(nameof(PlayerStatusChanged), peerId, playerInfo.Status);
        EmitSignal(nameof(ReadyForSessionReceived), peerId, playerInfo.ReadyForSession);
        EmitSignal(nameof(ServerStateUpdated));

        SystemBroadcastChat($"[b]{playerInfo.Name}[/b] has left.");
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
            formatted = $"[b]({senderState.GetStatusReadableShort()}) [{senderState.Name}]:[/b] {message}";
        }

        EmitSignal(nameof(ChatReceived), formatted);
    }
}
