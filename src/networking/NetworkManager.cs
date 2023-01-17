using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Nito.Collections;
using Array = Godot.Collections.Array;

/// <summary>
///   Manages high-level multiplayer mechanisms and provides support for online game sessions.
/// </summary>
public class NetworkManager : Node
{
    public const int DEFAULT_SERVER_ID = 1;
    public const int MAX_CHAT_HISTORY_COUNT = 200;
    public const float RTT_WEIGHTING_FACTOR = 0.7f;

    private static NetworkManager? instance;

    private readonly Dictionary<int, NetworkPlayerInfo> connectedPlayers = new();

    private PingData ping;

    private Deque<string> chatHistory = new();

    private NetworkedMultiplayerENet? peer;
    private UPNP? upnp;

    private float timeStep;
    private float updateTimer;
    private float truncatedDecimals;

    private ulong timeSessionStarted;

    private NetworkManager()
    {
        instance = this;
    }

    [Signal]
    public delegate void UpnpCallResultReceived(UPNP.UPNPResult result, UpnpJobStep step);

    [Signal]
    public delegate void RegistrationResultReceived(int peerId, RegistrationResult result);

    [Signal]
    public delegate void ConnectionFailed(string reason);

    [Signal]
    public delegate void ServerStateUpdated();

    [Signal]
    public delegate void Kicked(string reason);

    [Signal]
    public delegate void ChatReceived();

    [Signal]
    public delegate void PlayerJoined(int peerId);

    [Signal]
    public delegate void PlayerLeft(int peerId);

    [Signal]
    public delegate void ReadyForSessionReceived(int peerId, bool ready);

    [Signal]
    public delegate void PlayerStatusChanged(int peerId, NetworkPlayerStatus status);

    [Signal]
    public delegate void LatencyUpdated(int peerId, int miliseconds);

    public event EventHandler<float>? NetworkTick;

    public enum UpnpJobStep
    {
        Discovery,
        PortMapping,
    }

    public enum RegistrationResult
    {
        Success,
        ServerFull,
        DuplicateName,
    }

    public static NetworkManager Instance => instance ?? throw new InstanceNotLoadedYetException();

    public ServerSettings? Settings { get; private set; }

    public NetworkedMultiplayerPeer.ConnectionStatus Status => peer?.GetConnectionStatus() ??
        NetworkedMultiplayerPeer.ConnectionStatus.Disconnected;

    /// <summary>
    ///   The delay between every update, i.e. how many times per second the server's game state data is sent if we are
    ///   the server or the input data if we are the client.
    /// </summary>
    public float TimeStep
    {
        get => timeStep;
        set
        {
            if (timeStep == value)
                return;

            timeStep = value;

            if (IsAuthoritative)
            {
                Rpc(nameof(NotifyServerTimeStepChange), timeStep);
            }
            else if (IsClient)
            {
                SystemChatNotification(TranslationServer.Translate("CLIENT_TIME_STEP_CHANGED").FormatSafe(timeStep));
            }
        }
    }

    public float TimePassedConnecting { get; private set; }

    public int PeerId { get; private set; }

    /// <summary>
    ///   Returns true if we are a peer in a network (in multiplayer mode).
    /// </summary>
    public bool IsNetworked => peer != null;

    /// <summary>
    ///   All peers connected in the network (INCLUDING SELF), stored by network ID.
    /// </summary>
    public IReadOnlyDictionary<int, NetworkPlayerInfo> ConnectedPlayers => connectedPlayers;

    public NetworkPlayerInfo? LocalPlayer => IsNetworked ? GetPlayerInfo(PeerId) : null;

    public IReadOnlyList<string> ChatHistory => chatHistory;

    public bool IsServer => PeerId == DEFAULT_SERVER_ID;

    /// <summary>
    ///   Dedicated server has no player set.
    /// </summary>
    public bool IsDedicated => IsServer && !HasPlayer(DEFAULT_SERVER_ID);

    public bool IsAuthoritative => IsNetworked && IsServer && IsNetworkMaster();

    public bool IsClient => IsNetworked && !IsServer && !IsNetworkMaster();

    [PuppetSync]
    public bool GameInSession { get; private set; }

    /// <summary>
    ///   The elapsed time since the start of current game session in miliseconds.
    /// </summary>
    public long ElapsedGameTime { get; private set; }

    public int ElapsedGameTimeMinutes => Mathf.FloorToInt((ElapsedGameTime * 0.001f) / 60);

    public int ElapsedGameTimeSeconds => Mathf.FloorToInt((ElapsedGameTime * 0.001f) % 60);

    /// <summary>
    ///   Returns the current game time in a short format.
    /// </summary>
    public string GameTime => StringUtils.FormatShortMinutesSeconds(ElapsedGameTimeMinutes, ElapsedGameTimeSeconds);

    /// <summary>
    ///   Returns the current game time in a more readable format (with explicit minutes and seconds).
    /// </summary>
    public string GameTimeHumanized => StringUtils.FormatLongMinutesSeconds(
        ElapsedGameTimeMinutes, ElapsedGameTimeSeconds);

    public override void _Ready()
    {
        GetTree().Connect("network_peer_connected", this, nameof(OnPeerConnected));
        GetTree().Connect("network_peer_disconnected", this, nameof(OnPeerDisconnected));
        GetTree().Connect("server_disconnected", this, nameof(OnServerDisconnected));
        GetTree().Connect("connection_failed", this, nameof(OnConnectionFailed));

        Multiplayer.Connect("network_peer_packet", this, nameof(OnPeerPacket));

        Connect(nameof(UpnpCallResultReceived), this, nameof(OnUpnpCallResultReceived));

        ProcessPriority = 100;
        PauseMode = PauseModeEnum.Process;
    }

    public override void _PhysicsProcess(float delta)
    {
        if (!IsNetworked)
            return;

        if (Status == NetworkedMultiplayerPeer.ConnectionStatus.Connecting)
        {
            TimePassedConnecting += delta;
        }

        if (Status == NetworkedMultiplayerPeer.ConnectionStatus.Connected)
            HandlePing();

        if (GameInSession)
        {
            var deltaMs = delta * 1000;

            // TODO: Why is the apparent client time always ahead of the server?
            ElapsedGameTime += (long)deltaMs + ping.DeltaRoundTripTime;
            ping.DeltaRoundTripTime = 0;

            truncatedDecimals += deltaMs - (long)deltaMs;

            // Regain back precision
            if (truncatedDecimals >= 1.0f)
            {
                ++ElapsedGameTime;
                truncatedDecimals = 0;
            }

            updateTimer += delta;

            if (updateTimer > TimeStep)
            {
                NetworkTick?.Invoke(this, delta + updateTimer);
                updateTimer = 0;
            }
        }
    }

    /// <summary>
    ///   Creates a server without creating player for this peer.
    /// </summary>
    public Error CreateServer(ServerSettings settings)
    {
        TimePassedConnecting = 0;
        timeStep = Constants.DEFAULT_SERVER_TIME_STEP_SECONDS;

        peer = new NetworkedMultiplayerENet();
        peer.SetBindIp(settings.Address);

        // TODO: enable DTLS for secure transport?

        var error = peer.CreateServer(settings.Port, Constants.MULTIPLAYER_DEFAULT_MAX_CLIENTS);
        if (error != Error.Ok)
        {
            PrintError("An error occurred while trying to create server: ", error);
            return error;
        }

        Settings = settings;

        // Could have just set this to 1 but juuust in case
        PeerId = peer.GetUniqueId();

        GetTree().NetworkPeer = peer;

        Print("Created server with the following settings ", settings);

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

        if (settings.UseUpnp)
        {
            // Automatic port mapping/forwarding with UPnP
            TaskExecutor.Instance.AddTask(new Task(() => SetupUpnp()));
        }

        OnConnectedToServer(playerName);
        NotifyReadyForSessionStatusChange(DEFAULT_SERVER_ID, true);

        Print("Server is player hosted");

        return result;
    }

    public Error ConnectToServer(string address, int port, string playerName)
    {
        TimePassedConnecting = 0;
        timeStep = Constants.DEFAULT_CLIENT_TIME_STEP_SECONDS;

        peer = new NetworkedMultiplayerENet();

        // TODO: enable DTLS for secure transport?

        GetTree().CheckAndConnect(
            "connected_to_server", this, nameof(OnConnectedToServer), new Array { playerName },
            (int)ConnectFlags.Oneshot);

        var result = peer.CreateClient(address, port);
        if (result != Error.Ok)
        {
            PrintError("An error occurred while trying to create client: ", result);
            return result;
        }

        GetTree().NetworkPeer = peer;

        return result;
    }

    public void Disconnect()
    {
        Print("Disconnecting...");
        peer?.CloseConnection();

        if (upnp?.GetDeviceCount() > 0)
        {
            upnp?.DeletePortMapping(Settings!.Port);
            upnp = null;
        }

        peer = null;
        connectedPlayers.Clear();
        GameInSession = false;
        ElapsedGameTime = 0;
        timeSessionStarted = 0;
        Settings = null;

        ClearChatHistory();
    }

    public bool HasPlayer(int peerId)
    {
        return connectedPlayers.ContainsKey(peerId);
    }

    public NetworkPlayerInfo? GetPlayerInfo(int peerId)
    {
        connectedPlayers.TryGetValue(peerId, out NetworkPlayerInfo result);
        return result;
    }

    /// <summary>
    ///   Sets a player's server-wide variable.
    /// </summary>
    public void SetVar(int peerId, string what, object variant)
    {
        if (IsClient)
            return;

        Rpc(nameof(SyncVars), peerId, what, variant);
    }

    public void Join()
    {
        if (!IsDedicated && LocalPlayer!.Status == NetworkPlayerStatus.Active)
            return;

        NotifyGameLoad();

        if (IsAuthoritative)
        {
            foreach (var player in connectedPlayers)
            {
                if (player.Key == DEFAULT_SERVER_ID)
                    continue;

                if (player.Value.Status == NetworkPlayerStatus.Lobby)
                    RpcId(player.Key, nameof(NotifyGameLoad));
            }
        }
    }

    public void Exit()
    {
        if (!IsDedicated && LocalPlayer!.Status == NetworkPlayerStatus.Lobby)
            return;

        if (IsAuthoritative && GameInSession)
            Rset(nameof(GameInSession), false);

        NotifyGameExit();

        if (IsAuthoritative)
        {
            foreach (var player in connectedPlayers)
            {
                if (player.Key == DEFAULT_SERVER_ID)
                    continue;

                if (player.Value.Status == NetworkPlayerStatus.Active)
                    RpcId(player.Key, nameof(NotifyGameExit));
            }
        }
    }

    public void SetReadyForSessionStatus(bool ready)
    {
        if (IsAuthoritative)
            return;

        RpcId(DEFAULT_SERVER_ID, nameof(NotifyReadyForSessionStatusChange), PeerId, ready);
    }

    public void Kick(int id, string reason)
    {
        RpcId(id, nameof(NotifyKick), reason);
    }

    /// <summary>
    ///   Parses a message and if appropriate sends that message to all peers.
    /// </summary>
    public void Chat(string message)
    {
        if (!IsNetworked)
            return;

        if (message.BeginsWith("/") && ParseCommand(message.TrimStart('/')))
        {
            // Is a valid command, don't send this
            return;
        }

        Rpc(nameof(SendChat), message);
    }

    /// <summary>
    ///   Outputs a chat with the sender marked as "system" to the chat history.
    /// </summary>
    public void SystemChatNotification(string message)
    {
        SendChatInternal(TranslationServer.Translate("CHAT_PREFIX_AS_SYSTEM").FormatSafe(message));
    }

    public void ClearChatHistory()
    {
        chatHistory.Clear();
    }

    /// <summary>
    ///   Differentiates between print call from server or client. We do this so that they will stand out more
    ///   on the output log.
    /// </summary>
    public void Print(params object[] what)
    {
        var str = string.Concat(System.Array.ConvertAll(what, x => x?.ToString() ?? "null"));
        var serverOrHost = IsDedicated ? "[Server] " : "[Host] ";
        GD.Print(IsAuthoritative ? serverOrHost : "[Client] ", str);
    }

    public void PrintError(params object[] what)
    {
        var str = string.Concat(System.Array.ConvertAll(what, x => x?.ToString() ?? "null"));
        var serverOrHost = IsDedicated ? "[Server] " : "[Host] ";
        GD.PrintErr(IsAuthoritative ? serverOrHost : "[Client] ", str);
    }

    private void SetupUpnp()
    {
        upnp ??= new UPNP();

        var result = (UPNP.UPNPResult)upnp.Discover();

        if (result != UPNP.UPNPResult.Success)
            PrintError("UPnP devices discovery failed: ", result);

        EmitSignal(nameof(UpnpCallResultReceived), result, UpnpJobStep.Discovery);
    }

    private void AddPortMapping(int port)
    {
        if (upnp == null)
            return;

        if (upnp.GetDeviceCount() <= 0)
            return;

        if (upnp.GetGateway()?.IsValidGateway() == true)
        {
            // May not work in some cases even if the gateway supports UPNP/port forwarding. The player's network
            // might be on CGNAT or double NAT, preventing this from working succesfully
            // I can't test this because of the aforementioned issue - kasterisk
            var pmResult = (UPNP.UPNPResult)upnp.AddPortMapping(port, 0, "ThriveGame");

            // TODO: error handling

            EmitSignal(nameof(UpnpCallResultReceived), pmResult, UpnpJobStep.PortMapping);
            return;
        }

        EmitSignal(nameof(UpnpCallResultReceived), UPNP.UPNPResult.Success, UpnpJobStep.PortMapping);
    }

    private bool ParseCommand(string command)
    {
        var args = System.Array.Empty<string>();
        var name = command;

        if (command.Contains(' '))
        {
            args = command.Split(' ');
            name = args[0];
        }

        if (name == "clear")
        {
            ClearChatHistory();

            // Update chatboxes
            EmitSignal(nameof(ChatReceived));
        }
        else if (name == "timestep")
        {
            if (args.Length <= 1 || !float.TryParse(args[1], out float result))
                return false;

            TimeStep = result;
        }
        else if (name == "end")
        {
            if (Settings == null)
                return false;

            ElapsedGameTime = Settings.SessionLength * 60000;
        }

        return true;
    }

    private void SendChatInternal(string message)
    {
        if (chatHistory.Count > MAX_CHAT_HISTORY_COUNT)
            chatHistory.RemoveFromFront();

        chatHistory.AddToBack(message);

        EmitSignal(nameof(ChatReceived));
    }

    private void HandlePing()
    {
        // Must be client-initiated
        if (!IsClient)
            return;

        var currentTime = Time.GetTicksMsec();

        // Check if timed out
        if (ping.IsWaitingAck && currentTime > ping.TimeSent + Constants.NETWORK_PING_TIMEOUT_MILISECONDS)
        {
            ++ping.PacketLost;
            ping.IsWaitingAck = false;
        }

        // Ping server
        if (!ping.IsWaitingAck && currentTime > ping.TimeSent + Constants.NETWORK_PING_INTERVAL_MILISECONDS)
        {
            SendPing();
        }
    }

    private void SendPing()
    {
        // Must be client-initiated
        if (!IsClient)
            return;

        ++ping.Id;
        ping.TimeSent = Time.GetTicksMsec();
        ping.IsWaitingAck = true;

        var packet = new PackedBytesBuffer(3);
        packet.Write((byte)RawPacketFlag.Ping);
        packet.Write((ushort)ping.Id);

        Multiplayer.SendBytes(packet.Data, DEFAULT_SERVER_ID, NetworkedMultiplayerPeer.TransferModeEnum.Unreliable);
    }

    private void ProcessReceivedPing(int fromId, PackedBytesBuffer buffer)
    {
        // Must be received by the server
        if (!IsAuthoritative)
            return;

        var pingId = buffer.ReadUInt16();

        var packet = new PackedBytesBuffer(3);
        packet.Write((byte)RawPacketFlag.Pong);
        packet.Write(pingId);
        packet.Write(Time.GetTicksMsec() - timeSessionStarted);

        // Send back to the client (pong)
        Multiplayer.SendBytes(packet.Data, fromId, NetworkedMultiplayerPeer.TransferModeEnum.Unreliable);
    }

    private void ProcessReceivedPong(ulong timeReceived, PackedBytesBuffer buffer)
    {
        var receivedId = buffer.ReadUInt16();
        var serverTime = buffer.ReadUInt64();

        if (receivedId != ping.Id)
        {
            PrintError("Invalid ping/pong packet received");
            return;
        }

        ping.IsWaitingAck = false;

        var roundTripTime = timeReceived - ping.TimeSent;

        var oldRtt = ping.AverageRoundTripTime;

        // Smooth it out (TCP RTT calculation)
        ping.AverageRoundTripTime = (ulong)(ping.AverageRoundTripTime <= 0 ?
            roundTripTime :
            (RTT_WEIGHTING_FACTOR * ping.AverageRoundTripTime) + ((1 - RTT_WEIGHTING_FACTOR) * roundTripTime));

        ping.DeltaRoundTripTime = (long)(oldRtt - ping.AverageRoundTripTime);

        ping.EstimatedTimeOffset = (long)(serverTime + (ping.AverageRoundTripTime / 2)) - ElapsedGameTime;
        ElapsedGameTime += ping.EstimatedTimeOffset;

        Rpc(nameof(NotifyPeerLatency), (ushort)ping.AverageRoundTripTime);
    }

    private void OnPeerConnected(int id)
    {
        _ = id;

        // Will probaby be useful later.
    }

    private void OnPeerDisconnected(int id)
    {
        if (!HasPlayer(id))
            return;

        Print("User \"", GetPlayerInfo(id)!.Name, "\" ID: ", id, " has disconnected");

        SystemChatNotification(
            TranslationServer.Translate("PLAYER_HAS_DISCONNECTED").FormatSafe(GetPlayerInfo(id)!.Name));

        NotifyPlayerDisconnected(id);
    }

    private void OnPeerPacket(int fromId, byte[] packet)
    {
        var timeReceived = Time.GetTicksMsec();

        var buffer = new PackedBytesBuffer(packet);

        var packetType = (RawPacketFlag)buffer.ReadByte();

        switch (packetType)
        {
            case RawPacketFlag.Ping:
                ProcessReceivedPing(fromId, buffer);
                break;
            case RawPacketFlag.Pong:
                ProcessReceivedPong(timeReceived, buffer);
                break;
        }
    }

    private void OnConnectedToServer(string playerName)
    {
        PeerId = peer!.GetUniqueId();

        var packed = new PackedBytesBuffer();
        var info = new NetworkPlayerInfo { Name = playerName };
        info.NetworkSerialize(packed);

        // TODO: some kind of authentication
        RpcId(DEFAULT_SERVER_ID, nameof(NotifyPlayerConnected), PeerId, packed.Data);

        TimePassedConnecting = 0;
    }

    private void OnServerDisconnected()
    {
        Print("Disconnected from server");

        if (upnp?.GetDeviceCount() > 0)
            upnp?.DeletePortMapping(Settings!.Port);

        peer = null;
        connectedPlayers.Clear();
        GameInSession = false;
        ElapsedGameTime = 0;
        timeSessionStarted = 0;
        Settings = null!;

        ClearChatHistory();

        EmitSignal(nameof(ServerStateUpdated));
    }

    private void OnConnectionFailed()
    {
        var reason = TranslationServer.Translate("BAD_CONNECTION");

        // TODO: This is a bit flaky
        if (Mathf.RoundToInt(TimePassedConnecting) >= Constants.MULTIPLAYER_DEFAULT_TIMEOUT_LIMIT_SECONDS)
            reason = TranslationServer.Translate("TIMEOUT");

        EmitSignal(nameof(ConnectionFailed), reason);

        TimePassedConnecting = 0;

        Print("Connection failed");
    }

    private void OnUpnpCallResultReceived(UPNP.UPNPResult result, UpnpJobStep step)
    {
        switch (step)
        {
            case UpnpJobStep.Discovery:
            {
                if (result == UPNP.UPNPResult.Success)
                    TaskExecutor.Instance.AddTask(new Task(() => AddPortMapping(Settings!.Port)));

                break;
            }
        }
    }

    private void OnGameReady(object sender, EventArgs args)
    {
        if (IsAuthoritative && !GameInSession)
        {
            Rset(nameof(GameInSession), true);
            timeSessionStarted = Time.GetTicksMsec();
        }

        Print("Local game is now ready, notifying the host");

        RpcId(DEFAULT_SERVER_ID, nameof(NotifyWorldReady), PeerId);
    }

    /// <summary>
    ///   How this works: a client send their information to the server (in this case when they're just connected),
    ///   the server then do validity checks and if valid, registers this client which in turn gives them the
    ///   server's and everyone's information. All the other players are also notified about their arrival in
    ///   parallel.
    /// </summary>
    [RemoteSync]
    private void NotifyPlayerConnected(int id, byte[] data)
    {
        var incomingPacket = new PackedBytesBuffer(data);
        var info = new NetworkPlayerInfo();
        info.NetworkDeserialize(incomingPacket);

        if (IsAuthoritative)
        {
            if (connectedPlayers.Count >= Settings!.MaxPlayers)
            {
                // No need to notify everyone about this
                RpcId(id, nameof(NotifyRegistrationResult), id, RegistrationResult.ServerFull);

                peer!.DisconnectPeer(id);
                return;
            }

            if (connectedPlayers.Values.Any(p => p.Name == info.Name))
            {
                // No need to notify everyone about this
                RpcId(id, nameof(NotifyRegistrationResult), id, RegistrationResult.DuplicateName);

                peer!.DisconnectPeer(id);
                return;
            }

            if (GameInSession)
                RsetId(id, nameof(GameInSession), true);

            foreach (var player in connectedPlayers)
            {
                if (player.Key != DEFAULT_SERVER_ID)
                {
                    // Forward newly connected player info to the other players
                    RpcId(player.Key, nameof(NotifyPlayerConnected), id, data);
                }

                // Forward other players' info to the newly connected player, including us if we're a listen-server
                var playerPacket = new PackedBytesBuffer();
                player.Value.NetworkSerialize(playerPacket);
                RpcId(id, nameof(NotifyPlayerConnected), player.Key, playerPacket.Data);
                RpcId(id, nameof(NotifyPlayerStatusChange), player.Key, player.Value.Status);
            }

            if (id != DEFAULT_SERVER_ID)
            {
                // Return sender client's own info so they have themselves registered locally
                RpcId(id, nameof(NotifyPlayerConnected), id, data);

                // And finally give the client our server configurations
                var settingsPacket = new PackedBytesBuffer();
                Settings.NetworkSerialize(settingsPacket);
                RpcId(id, nameof(NotifyServerConfigs), settingsPacket.Data);
            }

            // TODO: might not be true...
            Print("User \"", info.Name, "\" ID: ", id, " has connected");
        }

        if (HasPlayer(id))
            return;

        connectedPlayers.Add(id, info);
        EmitSignal(nameof(ServerStateUpdated));

        SystemChatNotification(
            TranslationServer.Translate("PLAYER_HAS_CONNECTED").FormatSafe(info.Name));

        if (IsAuthoritative)
        {
            // Tell all peers (and ourselves if this is client hosted) that a new peer have
            // been successfully registered to the server
            NotifyRegistrationResult(id, RegistrationResult.Success);
        }
    }

    [Remote]
    private void NotifyPlayerDisconnected(int id)
    {
        if (!HasPlayer(id))
            return;

        connectedPlayers.Remove(id);
        EmitSignal(nameof(ServerStateUpdated));
    }

    [Remote]
    private void NotifyServerConfigs(byte[] settings)
    {
        var buffer = new PackedBytesBuffer(settings);
        Settings = new ServerSettings();
        Settings.NetworkDeserialize(buffer);
    }

    [RemoteSync]
    private void NotifyRegistrationResult(int peerId, RegistrationResult result)
    {
        if (IsAuthoritative)
        {
            foreach (var player in connectedPlayers)
            {
                if (player.Key != DEFAULT_SERVER_ID)
                    RpcId(player.Key, nameof(NotifyRegistrationResult), peerId, result);
            }
        }

        EmitSignal(nameof(RegistrationResultReceived), peerId, result);
    }

    [RemoteSync]
    private void NotifyPlayerStatusChange(int id, NetworkPlayerStatus environment)
    {
        if (IsAuthoritative)
        {
            foreach (var player in connectedPlayers)
            {
                if (player.Key == DEFAULT_SERVER_ID)
                    continue;

                RpcId(player.Key, nameof(NotifyPlayerStatusChange), id, environment);
            }
        }

        var info = GetPlayerInfo(id);
        if (info != null)
        {
            info.Status = environment;
            EmitSignal(nameof(PlayerStatusChanged), id, environment);
        }
    }

    [Remote]
    private void NotifyKick(string reason)
    {
        if (GetTree().GetRpcSenderId() != DEFAULT_SERVER_ID)
        {
            PrintError("Kicking is only permissible from host/server");
            return;
        }

        Disconnect();
        EmitSignal(nameof(Kicked), reason);
    }

    [RemoteSync]
    private void NotifyReadyForSessionStatusChange(int peerId, bool ready)
    {
        if (IsAuthoritative)
        {
            foreach (var player in connectedPlayers)
            {
                if (player.Key == DEFAULT_SERVER_ID)
                    continue;

                RpcId(player.Key, nameof(NotifyReadyForSessionStatusChange), peerId, ready);
            }
        }

        var info = GetPlayerInfo(peerId)!;
        info.ReadyForSession = ready;

        EmitSignal(nameof(ReadyForSessionReceived), peerId, ready);
    }

    [RemoteSync]
    private void NotifyServerTimeStepChange(float timestep)
    {
        Settings!.TimeStep = timestep;
        SystemChatNotification(TranslationServer.Translate("SERVER_TIME_STEP_CHANGED").FormatSafe(timestep));
    }

    [RemoteSync]
    private void NotifyPeerLatency(ushort latency)
    {
        var sender = GetTree().GetRpcSenderId();

        var info = GetPlayerInfo(sender);

        if (info != null)
        {
            info.Latency = latency;
            EmitSignal(nameof(LatencyUpdated), sender, latency);
        }
    }

    [Remote]
    private void NotifyGameLoad()
    {
        if (Settings == null)
            return;

        Rpc(nameof(NotifyWorldPreLoad), PeerId);

        TransitionManager.Instance.AddSequence(ScreenFade.FadeType.FadeOut, 0.4f, () =>
        {
            var scene = SceneManager.Instance.LoadScene(Settings.SelectedGameMode!.MainScene);
            var stage = (IMultiplayerStage)scene.Instance();

            stage.GameReady += OnGameReady;
            SceneManager.Instance.SwitchToScene(stage.GameStateRoot);
        });

        Print("Starting the game");
    }

    [Remote]
    private void NotifyGameExit()
    {
        Rpc(nameof(NotifyWorldPreExit), PeerId);

        TransitionManager.Instance.AddSequence(ScreenFade.FadeType.FadeOut, 0.3f, () =>
        {
            var menu = SceneManager.Instance.ReturnToMenu();
            menu.OpenMultiplayerMenu(MultiplayerGUI.SubMenu.Lobby);
            Rpc(nameof(NotifyWorldPostExit), PeerId);
        });

        ElapsedGameTime = 0;
        timeSessionStarted = 0;

        Print("Exiting current game session");
    }

    [RemoteSync]
    private void NotifyWorldPreLoad(int peerId)
    {
        var playerInfo = GetPlayerInfo(peerId);

        if (playerInfo == null)
            return;

        playerInfo.Status = NetworkPlayerStatus.Joining;
        EmitSignal(nameof(PlayerStatusChanged), peerId, playerInfo.Status);
    }

    [RemoteSync]
    private void NotifyWorldReady(int peerId)
    {
        var sender = GetTree().GetRpcSenderId();

        if (IsAuthoritative && sender != DEFAULT_SERVER_ID &&
            LocalPlayer?.Status != NetworkPlayerStatus.Active)
        {
            Print("Not yet ready. \"World ready\" notification from ", sender, " is rejected");
            return;
        }

        if (IsClient && sender != DEFAULT_SERVER_ID)
            return;

        var playerInfo = GetPlayerInfo(peerId);
        if (playerInfo == null)
            return;

        if (IsAuthoritative)
        {
            Print("Received \"world ready\" notification from ", peerId, ", forwarding to others");

            foreach (var player in ConnectedPlayers)
            {
                if (player.Key != DEFAULT_SERVER_ID)
                    RpcId(player.Key, nameof(NotifyWorldReady), peerId);
            }
        }

        playerInfo.Status = NetworkPlayerStatus.Active;
        EmitSignal(nameof(PlayerJoined), peerId);
        EmitSignal(nameof(PlayerStatusChanged), peerId, playerInfo.Status);

        SystemChatNotification(TranslationServer.Translate("PLAYER_HAS_JOINED").FormatSafe(playerInfo.Name));
    }

    [RemoteSync]
    private void NotifyWorldPreExit(int peerId)
    {
        var playerInfo = GetPlayerInfo(peerId);

        if (playerInfo == null)
            return;

        playerInfo.Status = NetworkPlayerStatus.Leaving;
        EmitSignal(nameof(PlayerStatusChanged), peerId, playerInfo.Status);
        EmitSignal(nameof(PlayerLeft), peerId);
    }

    [RemoteSync]
    private void NotifyWorldPostExit(int peerId)
    {
        var playerInfo = GetPlayerInfo(peerId);

        if (playerInfo == null)
            return;

        playerInfo.Status = NetworkPlayerStatus.Lobby;
        EmitSignal(nameof(PlayerStatusChanged), peerId, playerInfo.Status);
        EmitSignal(nameof(ReadyForSessionReceived), peerId, playerInfo.ReadyForSession);
        EmitSignal(nameof(ServerStateUpdated));

        SystemChatNotification(TranslationServer.Translate("PLAYER_HAS_LEFT").FormatSafe(playerInfo.Name));
    }

    [RemoteSync]
    private void SendChat(string message)
    {
        var senderId = GetTree().GetRpcSenderId();
        var senderState = GetPlayerInfo(senderId);

        var formatted = $"[b]({senderState?.GetStatusReadableShort()}) [{senderState?.Name}]:[/b] {message}";

        SendChatInternal(formatted);
    }

    [PuppetSync]
    private void SyncVars(int peerId, string what, object variant)
    {
        var sender = GetTree().GetRpcSenderId();
        if (sender != DEFAULT_SERVER_ID)
            return;

        var info = GetPlayerInfo(peerId);
        info?.SetVar(what, variant);
    }

    /// <summary>
    ///   Information derived by pinging the server.
    /// </summary>
    public struct PingData
    {
        /// <summary>
        ///   The current id of a ping packet being transmitted.
        /// </summary>
        public int Id;

        /// <summary>
        ///   A timestamp when a ping is transmitted.
        /// </summary>
        public ulong TimeSent;

        /// <summary>
        ///   The time it takes for a packet to be transmitted from the client (in this
        ///   case a ping/pong packet) to the server and back in miliseconds.
        /// </summary>
        public ulong AverageRoundTripTime;

        /// <summary>
        ///   The difference between the old and newly calculated average round trip time.
        /// </summary>
        public long DeltaRoundTripTime;

        /// <summary>
        ///   The offset between the client's and the server's clock.
        /// </summary>
        public long EstimatedTimeOffset;

        /// <summary>
        ///   Returns true if a pong is expected from the server.
        /// </summary>
        public bool IsWaitingAck;

        /// <summary>
        ///   Increments if current time > timeout factor.
        /// </summary>
        public uint PacketLost;
    }
}
