using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

/// <summary>
///   Base stage for the stages where the player controls a single creature, supports online gameplay.
/// </summary>
/// <typeparam name="TPlayer">The type of the player object</typeparam>
/// <remarks>
///   <para>
///     Due to how Godot RPCs work, this must ALWAYS be attached to the scene tree during gameplay since
///     this act as an intermediary for communications between the server and the client.
///   </para>
///   <para>
///     TODO: perhaps this can be combined into the normal StageBase to remove redundancies and
///     to make singleplayer to multiplayer seamless.
///   </para>
/// </remarks>
public abstract class MultiplayerStageBase<TPlayer> : StageBase<TPlayer>, IMultiplayerStage
    where TPlayer : NetworkCharacter
{
    [Export]
    public NodePath MultiplayerInputPath = null!;

    /// <summary>
    ///   Specifies value for which an error from predicted state and incoming server state is tolerable.
    /// </summary>
    [Export]
    public float PredictionErrorToleranceThreshold = 0.05f;

    protected MultiplayerInputBase playerInputHandler = null!;

    /// <summary>
    ///   The number of entities being sent to the client when registering to the server.
    /// </summary>
    private int incomingEntitiesCount;

    private float averageTickLead;

    private WorldState[] localWorldStateBuffer = new WorldState[Constants.BUFFER_MAX_TICKS];

    private Queue<Heartbeat> heartbeatBuffer = new();

    public event EventHandler? GameReady;

    public uint TickCount { get; private set; }

    public uint LastReceivedServerTick { get; private set; }

    public uint LastAckedInputTick { get; private set; }

    public MultiplayerGameWorld MultiplayerWorld => (MultiplayerGameWorld)GameWorld;

    /// <summary>
    ///   Information about the local player in relation to the current game session.
    /// </summary>
    public NetworkPlayerVars LocalPlayerVars
    {
        get
        {
            var id = NetworkManager.Instance.PeerId;
            if (MultiplayerWorld.PlayerVars.TryGetValue(id, out NetworkPlayerVars vars))
                return vars;

            throw new InvalidOperationException("Player hasn't been set");
        }
    }

    /// <summary>
    ///   The settings for this multiplayer stage.
    /// </summary>
    public Vars Settings => NetworkManager.Instance.ServerSettings.GetVar<Vars>("GameModeSettings");

    protected abstract string StageLoadingMessage { get; }

    public override void _EnterTree()
    {
        base._EnterTree();

        NetworkManager.Instance.NetworkTick += OnNetworkTick;
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        NetworkManager.Instance.NetworkTick -= OnNetworkTick;
    }

    public override void _Ready()
    {
        base._Ready();

        GetTree().Connect("network_peer_disconnected", this, nameof(OnPeerDisconnected));
        GetTree().Connect("server_disconnected", this, nameof(OnServerDisconnected));

        NetworkManager.Instance.Connect(nameof(NetworkManager.Kicked), this, nameof(OnKicked));

        if (NetworkManager.Instance.IsServer)
        {
            NetworkManager.Instance.Connect(nameof(NetworkManager.PlayerJoined), this, nameof(RegisterPlayer));
            NetworkManager.Instance.Connect(nameof(NetworkManager.PlayerLeft), this, nameof(UnregisterPlayer));

            SpawnHelpers.OnNetEntitySpawned = OnNetEntitySpawned;
            SpawnHelpers.OnNetEntityDespawned = OnNetEntityDespawned;
        }

        ResolveNodeReferences();
    }

    public override void ResolveNodeReferences()
    {
        if (NodeReferencesResolved)
            return;

        base.ResolveNodeReferences();

        playerInputHandler = GetNode<MultiplayerInputBase>(MultiplayerInputPath);
    }

    public override void _Process(float delta)
    {
        if (NetworkManager.Instance.Status == NetworkedMultiplayerPeer.ConnectionStatus.Disconnected &&
            NetworkManager.Instance.LocalPlayer?.Status == NetworkPlayerStatus.Joining &&
            LoadingScreen.Instance.Visible)
        {
            OnServerDisconnected();
        }

        if (NetworkManager.Instance.LocalPlayer?.Status == NetworkPlayerStatus.Joining && incomingEntitiesCount > 0)
        {
            LoadingScreen.Instance.Show(TranslationServer.Translate("LOADING_ENTITIES"), MainGameState.Invalid,
                TranslationServer.Translate("VALUE_SLASH_MAX_VALUE").FormatSafe(
                    MultiplayerWorld.EntityCount, incomingEntitiesCount));

            if (MultiplayerWorld.EntityCount >= incomingEntitiesCount)
            {
                // All incoming entities replicated, now tell the server we're ready
                incomingEntitiesCount = 0;
                TransitionManager.Instance.AddSequence(ScreenFade.FadeType.FadeOut, 0.5f, OnReady, false);
            }
        }

        if (NetworkManager.Instance.IsServer && !gameOver && IsGameOver())
        {
            // Only the server can truly set the game state to game over, clients can only "predict"
            GameOver();
        }
    }

    public override void OnFinishLoading(Save save)
    {
    }

    /// <summary>
    ///   Sets a single game-wide variable and syncs to all.
    /// </summary>
    public void ServerSetPlayerVar(int peerId, string key, object value)
    {
        if (!NetworkManager.Instance.IsServer)
            return;

        Rpc(nameof(SyncPlayerVar), peerId, key, value);
    }

    /// <summary>
    ///   Syncs all game-wide player vars.
    /// </summary>
    public void ServerSyncPlayerVars(int peerId, NetworkPlayerVars vars)
    {
        if (!NetworkManager.Instance.IsServer)
            return;

        var msg = new PackedBytesBuffer();
        vars.NetworkSerialize(msg);
        Rpc(nameof(SyncPlayerVars), peerId, msg.Data);
    }

    public void ServerSyncPlayerVars(int peerId)
    {
        if (!NetworkManager.Instance.IsServer)
            return;

        if (MultiplayerWorld.PlayerVars.TryGetValue(peerId, out NetworkPlayerVars vars))
            ServerSyncPlayerVars(peerId, vars);
    }

    public override bool IsGameOver()
    {
        return NetworkManager.Instance.ElapsedGameTimeMinutes >=
            NetworkManager.Instance.ServerSettings.GetVar<uint>("SessionLength");
    }

    public bool TryGetPlayer(int peerId, out TPlayer player)
    {
        var result = MultiplayerWorld.TryGetPlayerCharacter(peerId, out NetworkCharacter character);
        player = (TPlayer)character;
        return result;
    }

    protected override void SetupStage()
    {
        pauseMenu.GameProperties = CurrentGame ?? throw new InvalidOperationException("current game is not set");
    }

    protected override void OnGameStarted()
    {
        if (NetworkManager.Instance.IsClient)
        {
            TransitionManager.Instance.AddSequence(ScreenFade.FadeType.FadeIn, 0.5f, null, false, false);

            if (!NetworkManager.Instance.GameInSession)
            {
                LoadingScreen.Instance.Show(TranslationServer.Translate("WAITING_FOR_HOST"), MainGameState.Invalid);
                return;
            }

            NetworkManager.Instance.Print("Requesting game world data");
            LoadingScreen.Instance.Show(TranslationServer.Translate("REGISTERING_TO_SERVER"), MainGameState.Invalid);
            RpcId(NetworkManager.DEFAULT_SERVER_ID, nameof(ServerRequestRegistration));
        }
        else if (NetworkManager.Instance.IsServer)
        {
            OnReady();
        }
    }

    protected override void GameOver()
    {
        gameOver = true;

        if (NetworkManager.Instance.IsServer)
            Rpc(nameof(NotifyGameOver));
    }

    /// <summary>
    ///   Network updates goes here.
    /// </summary>
    protected abstract void NetworkTick(float delta);

    /// <summary>
    ///   Registers an incoming player to the server (joining).
    /// </summary>
    protected virtual void RegisterPlayer(int peerId)
    {
        if (MultiplayerWorld.PlayerVars.ContainsKey(peerId) || NetworkManager.Instance.IsClient)
            return;

        // Register to the game world
        MultiplayerWorld.PlayerVars.Add(peerId, new NetworkPlayerVars());

        var species = CreateNewSpeciesForPlayer(peerId);
        MultiplayerWorld.SetSpecies(peerId, species);

        foreach (var player in NetworkManager.Instance.ConnectedPlayers)
        {
            if (player.Key == peerId || player.Value.Status != NetworkPlayerStatus.Active)
                continue;

            if (MultiplayerWorld.Species.TryGetValue((uint)player.Key, out Species otherSpecies))
            {
                // Send species of other players to the incoming player
                var otherSpeciesMsg = new PackedBytesBuffer();
                otherSpecies.NetworkSerialize(otherSpeciesMsg);
                RpcId(peerId, nameof(SyncSpecies), player.Key, otherSpeciesMsg.Data);
            }
        }

        // Send the incoming player's species to everyone
        var speciesMsg = new PackedBytesBuffer();
        species.NetworkSerialize(speciesMsg);
        Rpc(nameof(SyncSpecies), peerId, speciesMsg.Data);

        SpawnPlayer(peerId);
    }

    /// <summary>
    ///   Unregisters outgoing player from the server.
    /// </summary>
    protected virtual void UnregisterPlayer(int peerId)
    {
        if (!NetworkManager.Instance.IsServer)
            return;

        DespawnPlayer(peerId);

        foreach (var player in NetworkManager.Instance.ConnectedPlayers)
        {
            if (player.Value.Status == NetworkPlayerStatus.Active)
                RpcId(player.Key, nameof(DestroyPlayerVars), peerId);
        }
    }

    /// <summary>
    ///   Called client-side when entity with the given id has been spawned.
    /// </summary>
    protected virtual void OnNetEntitySpawned(uint entityId, INetworkEntity entity)
    {
        if (entity is NetworkCharacter character && character.IsLocal)
            OnLocalPlayerSpawned((TPlayer)entity);
    }

    /// <summary>
    ///   Called client-side when entity with the given id needs to be destroyed.
    /// </summary>
    protected virtual void OnNetEntityDestroy(uint entityId)
    {
        if (!MultiplayerWorld.TryGetNetworkEntity(entityId, out INetworkEntity entity))
            return;

        if (entityId == LocalPlayerVars.EntityId)
            OnLocalPlayerDespawned();

        entity.DestroyDetachAndQueueFree();
    }

    /// <summary>
    ///   If the the local entity we're controlling has been spawned.
    /// </summary>
    protected virtual void OnLocalPlayerSpawned(TPlayer player)
    {
        Player = player;
        spawnedPlayer = true;
    }

    /// <summary>
    ///   If the local entity we're controlling has been despawned.
    /// </summary>
    protected virtual void OnLocalPlayerDespawned()
    {
        Player = null;
    }

    protected void SpawnPlayer(int peerId)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        if (!HandlePlayerSpawn(peerId, out TPlayer? spawned))
            return;

        if (peerId == NetworkManager.Instance.PeerId)
            OnLocalPlayerSpawned(spawned!);
    }

    protected void DespawnPlayer(int peerId)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        if (!TryGetPlayer(peerId, out TPlayer player))
            return;

        if (!HandlePlayerDespawn(player))
            return;

        if (peerId == NetworkManager.Instance.PeerId)
            OnLocalPlayerDespawned();
    }

    /// <summary>
    ///   Server-side implementation. Returns true if successfully spawned.
    /// </summary>
    protected abstract bool HandlePlayerSpawn(int peerId, out TPlayer? spawned);

    /// <summary>
    ///   Server-side implementation. Returns true if successfully despawned.
    /// </summary>
    protected abstract bool HandlePlayerDespawn(TPlayer removed);

    [RemoteSync]
    protected override void SpawnPlayer()
    {
        if (HasPlayer)
            return;

        SpawnPlayer(NetworkManager.Instance.PeerId);
    }

    protected override void AutoSave()
    {
    }

    protected override void PerformQuickSave()
    {
    }

    protected virtual void SetEntityAsAttached(INetworkEntity entity, bool attached)
    {
        if (!IsInstanceValid(entity.EntityNode))
            return;

        var isParent = rootOfDynamicallySpawned.IsAParentOf(entity.EntityNode);

        if (attached && entity.EntityNode.GetParent() == null && !entity.EntityNode.IsInsideTree())
        {
            rootOfDynamicallySpawned.AddChild(entity.EntityNode);
        }
        else if (!attached && isParent && entity.EntityNode.IsInsideTree())
        {
            rootOfDynamicallySpawned.RemoveChild(entity.EntityNode);
        }
    }

    protected abstract Species CreateNewSpeciesForPlayer(int peerId);

    [PuppetSync]
    protected abstract void SyncSpecies(int peerId, byte[] data);

    /// <summary>
    ///   Game-mode specific score calculation.
    /// </summary>
    protected abstract int CalculateScore(int peerId);

    /// <summary>
    ///   Notifies a player's new score to all other peers.
    /// </summary>
    /// <param name="peerId">The player's peer id.</param>
    protected void NotifyScoreUpdate(int peerId)
    {
        NetworkManager.Instance.SetVar(peerId, "score", CalculateScore(peerId));
    }

    private void OnNetworkTick(object sender, float delta)
    {
        playerInputHandler.NetworkTick(delta);

        if (NetworkManager.Instance.IsClient)
            RecordWorldState();

        foreach (var entry in MultiplayerWorld.Entities.Values)
        {
            var entity = entry.Value;
            if (entity == null)
                continue;

            if (!entity.EntityNode.IsInsideTree())
                continue;

            entity.NetworkTick(delta);
        }

        DebugOverlays.Instance.ReportTickCount(TickCount);
        ++TickCount;

        if (NetworkManager.Instance.IsServer)
        {
            RecordWorldState();

            // TODO: Send at a fewer rate and fix weird jiggly motion that comes with it
            BroadcastHeartbeat();
        }
        else
        {
            // TODO: fix buffer "overflowing" i.e. too many heartbeats stored in the buffer making the client
            // running behind too far in the past
            ProcessIncomingHeartbeat(delta);
        }

        NetworkTick(delta);
    }

    /// <summary>
    ///   Returns a world state snapshot.
    /// </summary>
    private WorldState CreateWorldStateSnapshot()
    {
        var state = new WorldState();

        for (int i = MultiplayerWorld.EntityIDs.Count - 1; i >= 0; --i)
        {
            var id = MultiplayerWorld.EntityIDs[i];
            var entity = MultiplayerWorld.Entities[id].Value;

            if (entity == null || !entity.EntityNode.IsInsideTree())
                continue;

            var msg = new PackedBytesBuffer();
            entity.NetworkSerialize(msg);

            state.EntityStates[id] = msg;
        }

        return state;
    }

    /// <summary>
    ///   Records a snapshot of the world state for current tick.
    /// </summary>
    private void RecordWorldState()
    {
        localWorldStateBuffer[TickCount % Constants.BUFFER_MAX_TICKS] = CreateWorldStateSnapshot();
    }

    /// <summary>
    ///   Sends a heartbeat of the current tick to all active players.
    /// </summary>
    private void BroadcastHeartbeat()
    {
        var heartbeat = new Heartbeat
        {
            Tick = TickCount,
            WorldState = CreateWorldStateSnapshot(),
        };

        foreach (var player in NetworkManager.Instance.ConnectedPlayers)
        {
            if (player.Value.Status != NetworkPlayerStatus.Active ||
                player.Key == NetworkManager.DEFAULT_SERVER_ID)
            {
                continue;
            }

            heartbeat.AckedInputTick = playerInputHandler.PeersInputs[player.Key].LatestSnapshot.Key;

            var msg = new PackedBytesBuffer();
            heartbeat.NetworkSerialize(msg);

            RpcUnreliableId(player.Key, nameof(ReceiveServerHeartbeat), msg.Data);
        }
    }

    private void ProcessIncomingHeartbeat(float delta)
    {
        if (heartbeatBuffer.Count <= 0)
        {
            // Hasn't received any new updates from the server (or we're not a client).
            return;
        }

        var heartbeat = heartbeatBuffer.Dequeue();

        LastReceivedServerTick = heartbeat.Tick;
        LastAckedInputTick = heartbeat.AckedInputTick;

        var tickLead = (int)LastAckedInputTick - (int)LastReceivedServerTick + 1;
        averageTickLead = (0.7f * averageTickLead) + ((1 - 0.7f) * tickLead);
        DebugOverlays.Instance.ReportTickLead(averageTickLead);
        AdjustTickRate();

        if (heartbeat.Tick >= TickCount)
        {
            TickCount = heartbeat.Tick;
            return;
        }

        foreach (var entityState in heartbeat.WorldState.EntityStates)
        {
            if (!MultiplayerWorld.TryGetNetworkEntity(entityState.Key, out INetworkEntity entity))
            {
                // Entity is not found locally so we need to replicate (spawn) it
                // TODO: Integrate entity spawn request with world state snapshot
                RpcId(NetworkManager.DEFAULT_SERVER_ID, nameof(RequestEntitySpawn), entityState.Key);
                continue;
            }

            if (!entity.EntityNode.IsInsideTree())
                continue;

            if (entity is NetworkCharacter character && character.IsLocal)
                Reconcile(character, heartbeat, delta);

            entity.NetworkDeserialize(entityState.Value);
        }
    }

    private void Reconcile(NetworkCharacter character, Heartbeat serverHeartbeat, float delta)
    {
        var bufferPos = serverHeartbeat.Tick % Constants.BUFFER_MAX_TICKS;
        var localWorldState = localWorldStateBuffer[bufferPos];

        if (localWorldState == null)
        {
            NetworkManager.Instance.PrintError("World state doesn't exist for tick: ", serverHeartbeat.Tick);
            return;
        }

        var serverSerialized = serverHeartbeat.WorldState.EntityStates[character.NetworkEntityId];
        var serverPlayerState = character.DecodePacket(serverSerialized);

        if (!localWorldState.EntityStates.TryGetValue(character.NetworkEntityId,
            out PackedBytesBuffer localSerialized))
        {
            // Totally unexpected
            return;
        }

        var localPlayerState = character.DecodePacket(localSerialized);

        var positionError = serverPlayerState.Position - localPlayerState.Position;

        if (positionError.LengthSquared() > PredictionErrorToleranceThreshold)
        {
            // Too much error, needs rewinding

            NetworkManager.Instance.Print("Rewinding ", serverHeartbeat.Tick, ". Error: ",
                (serverPlayerState.Position - localPlayerState.Position).Length(), ", Tick offset: ",
                TickCount - serverHeartbeat.Tick);

            // Rewind
            character.ApplyState(serverPlayerState, false);

            var tickToReplay = serverHeartbeat.Tick;

            while (tickToReplay < TickCount)
            {
                // Replay

                bufferPos = tickToReplay % Constants.BUFFER_MAX_TICKS;

                var replayStateBuffer = new PackedBytesBuffer();
                character.NetworkSerialize(replayStateBuffer);

                localWorldStateBuffer[bufferPos].EntityStates[character.NetworkEntityId] = replayStateBuffer;

                character.ApplyNetworkedInput(playerInputHandler.GetInputAtBuffer(bufferPos));
                character.NetworkTick(delta);

                ++tickToReplay;
            }
        }
    }

    private void AdjustTickRate()
    {
        var multiplier = 1f;

        // TODO: Asses effectiveness
        if (averageTickLead < -2)
        {
            multiplier = 0.96875f;
        }
        else if (averageTickLead >= 2)
        {
            multiplier = 1.015625f;
        }

        NetworkManager.Instance.TickIntervalMultiplier = multiplier;
    }

    /// <summary>
    ///   Replicates the given server-side entity to the specified target peer.
    /// </summary>
    private void RemoteSpawnEntity(int targetPeerId, INetworkEntity entity)
    {
        if (!NetworkManager.Instance.IsServer)
            return;

        var msg = new PackedBytesBuffer();
        entity.PackSpawnState(msg);
        RpcId(targetPeerId, nameof(NotifyEntitySpawn), entity.NetworkEntityId, entity.ResourcePath, msg.Data);
    }

    private void OnNetEntitySpawned(INetworkEntity spawned)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        MultiplayerWorld.RegisterNetworkEntity(spawned);

        foreach (var player in NetworkManager.Instance.ConnectedPlayers)
        {
            if (player.Key == GetTree().GetNetworkUniqueId() || player.Value.Status != NetworkPlayerStatus.Active)
                continue;

            RemoteSpawnEntity(player.Key, spawned);
        }
    }

    private void OnNetEntityDespawned(uint id)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        MultiplayerWorld.UnregisterNetworkEntity(id);

        foreach (var player in NetworkManager.Instance.ConnectedPlayers)
        {
            if (player.Key == NetworkManager.DEFAULT_SERVER_ID || player.Value.Status != NetworkPlayerStatus.Active)
                continue;

            RpcId(player.Key, nameof(DestroySpawnedEntity), id);
        }
    }

    private void OnPeerDisconnected(int peerId)
    {
        if (NetworkManager.Instance.IsServer)
            UnregisterPlayer(peerId);
    }

    private void OnServerDisconnected()
    {
        LoadingScreen.Instance.Hide();

        var menu = SceneManager.Instance.ReturnToMenu();
        menu.OpenMultiplayerMenu(MultiplayerGUI.SubMenu.Main);
        menu.ShowDisconnectedDialog();
    }

    private void OnKicked(string reason)
    {
        var menu = SceneManager.Instance.ReturnToMenu();
        menu.OpenMultiplayerMenu(MultiplayerGUI.SubMenu.Main);
        menu.ShowKickedDialog(reason);
    }

    private void OnReady()
    {
        LoadingScreen.Instance.Hide();
        GameReady?.Invoke(this, EventArgs.Empty);
        BaseHUD.OnEnterStageTransition(true, false);

        if (NetworkManager.Instance.IsServer)
        {
            RegisterPlayer(NetworkManager.Instance.PeerId);
            Rpc(nameof(NotifyServerReady));
        }
    }

    /// <summary>
    ///   Buffer received server heartbeat.
    /// </summary>
    /// <param name="data">The heartbeat data</param>
    [Puppet]
    private void ReceiveServerHeartbeat(byte[] data)
    {
        var msg = new PackedBytesBuffer(data);
        var heartbeat = new Heartbeat();
        heartbeat.NetworkDeserialize(msg);

        heartbeatBuffer.Enqueue(heartbeat);
    }

    [Puppet]
    private void NotifyServerReady()
    {
        TransitionManager.Instance.AddSequence(ScreenFade.FadeType.FadeIn, 0.5f, null, false, false);
        LoadingScreen.Instance.Show(TranslationServer.Translate("REGISTERING_TO_SERVER"), MainGameState.Invalid);
        RpcId(NetworkManager.DEFAULT_SERVER_ID, nameof(ServerRequestRegistration));
    }

    [Puppet]
    private void NotifyGameOver()
    {
        GameOver();
    }

    [PuppetSync]
    private void SyncPlayerVar(int peerId, string key, object value)
    {
        if (!MultiplayerWorld.PlayerVars.ContainsKey(peerId))
            MultiplayerWorld.PlayerVars[peerId] = new NetworkPlayerVars();

        MultiplayerWorld.PlayerVars[peerId].SetVar(key, value);
    }

    [Puppet]
    private void SyncPlayerVars(int peerId, byte[] data)
    {
        var msg = new PackedBytesBuffer(data);
        var vars = new NetworkPlayerVars();
        vars.NetworkDeserialize(msg);
        MultiplayerWorld.PlayerVars[peerId] = vars;
    }

    [PuppetSync]
    private void DestroyPlayerVars(int peerId)
    {
        MultiplayerWorld.PlayerVars.Remove(peerId);
    }

    [Puppet]
    private void NotifyEntitySpawn(uint entityId, string resourcePath, byte[] data)
    {
        if (MultiplayerWorld.Entities.ContainsKey(entityId))
            return;

        // TODO: Cache resource path
        var scene = GD.Load<PackedScene>(resourcePath);
        var spawned = scene.Instance<INetworkEntity>();

        if (data != null)
        {
            spawned.OnRemoteSpawn(new PackedBytesBuffer(data), CurrentGame ??
                throw new InvalidOperationException("current game is not set"));
        }

        rootOfDynamicallySpawned.AddChild(spawned.EntityNode);
        MultiplayerWorld.RegisterNetworkEntity(entityId, spawned);
        OnNetEntitySpawned(entityId, spawned);
    }

    [Puppet]
    private void DestroySpawnedEntity(uint entityId)
    {
        OnNetEntityDestroy(entityId);
        MultiplayerWorld.UnregisterNetworkEntity(entityId);
    }

    [Puppet]
    private void AdjustClientTick(uint tick)
    {
        LastReceivedServerTick = tick;
        TickCount = (uint)(tick + NetworkManager.Instance.Latency * 0.001f / GetPhysicsProcessDeltaTime()) + 4;
        NetworkManager.Instance.Print("Adjusted client tick to ", TickCount, " with lead offset: ", TickCount - tick);
    }

    [Puppet]
    private void NotifyEntitiesDownload(int entitiesCount)
    {
        incomingEntitiesCount = entitiesCount;
        NetworkManager.Instance.Print("Downloading entities");
    }

    /// <summary>
    ///   Client sends request to the server to be registered to the game world (stage) and be sent the game world
    ///   state.
    /// </summary>
    [Remote]
    private void ServerRequestRegistration()
    {
        if (NetworkManager.Instance.IsClient)
            return;

        var sender = GetTree().GetRpcSenderId();

        RpcId(sender, nameof(AdjustClientTick), TickCount);

        // Upload player states
        foreach (var state in MultiplayerWorld.PlayerVars)
        {
            var msg = new PackedBytesBuffer();
            state.Value.NetworkSerialize(msg);
            RpcId(sender, nameof(SyncPlayerVars), state.Key, msg.Data);
        }

        RegisterPlayer(sender);

        RpcId(sender, nameof(NotifyEntitiesDownload), MultiplayerWorld.EntityCount);

        // Upload entities to the client
        foreach (var entity in MultiplayerWorld.Entities.Values)
        {
            if (entity.Value != null)
                RemoteSpawnEntity(sender, entity.Value);
        }
    }

    [Remote]
    private void RequestEntitySpawn(uint entityId)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        var sender = GetTree().GetRpcSenderId();

        if (MultiplayerWorld.TryGetNetworkEntity(entityId, out INetworkEntity entity))
            RemoteSpawnEntity(sender, entity);
    }

    public class Heartbeat : INetworkSerializable
    {
        public uint Tick { get; set; }
        public uint AckedInputTick { get; set; }
        public WorldState WorldState { get; set; } = new();

        public void NetworkSerialize(PackedBytesBuffer buffer)
        {
            buffer.Write(Tick);
            buffer.Write(AckedInputTick);
            WorldState.NetworkSerialize(buffer);
        }

        public void NetworkDeserialize(PackedBytesBuffer buffer)
        {
            Tick = buffer.ReadUInt32();
            AckedInputTick = buffer.ReadUInt32();
            WorldState.NetworkDeserialize(buffer);
        }
    }
}
