using System;
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
    public event EventHandler? GameReady;

    /// <summary>
    ///   The number of entities being sent to the client when registering to the server.
    /// </summary>
    private int incomingEntitiesCount;

    public MultiplayerGameWorld MultiplayerGameWorld => (MultiplayerGameWorld)GameWorld;

    public NetworkPlayerVars LocalPlayerVars
    {
        get
        {
            var id = NetworkManager.Instance.PeerId;
            if (MultiplayerGameWorld.PlayerVars.TryGetValue(id, out NetworkPlayerVars vars))
                return vars;

            throw new InvalidOperationException("Player hasn't been set");
        }
    }

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

        if (NetworkManager.Instance.IsAuthoritative)
        {
            NetworkManager.Instance.Connect(nameof(NetworkManager.PlayerJoined), this, nameof(RegisterPlayer));
            NetworkManager.Instance.Connect(nameof(NetworkManager.PlayerLeft), this, nameof(UnregisterPlayer));

            SpawnHelpers.OnNetEntitySpawned = OnNetEntitySpawned;
            SpawnHelpers.OnNetEntityDespawned = OnNetEntityDespawned;
        }
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
                    MultiplayerGameWorld.EntityCount, incomingEntitiesCount));

            if (MultiplayerGameWorld.EntityCount >= incomingEntitiesCount)
            {
                // All incoming entities replicated, now tell the server we're ready
                incomingEntitiesCount = 0;
                TransitionManager.Instance.AddSequence(ScreenFade.FadeType.FadeOut, 0.5f, OnReady, false);
            }
        }

        if (!gameOver && IsGameOver())
        {
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
        if (!NetworkManager.Instance.IsAuthoritative)
            return;

        Rpc(nameof(SyncPlayerVar), peerId, key, value);
    }

    /// <summary>
    ///   Syncs the whole game-wide player vars.
    /// </summary>
    public void ServerSyncPlayerVars(int peerId, NetworkPlayerVars vars)
    {
        if (!NetworkManager.Instance.IsAuthoritative)
            return;

        var packed = new PackedBytesBuffer();
        vars.NetworkSerialize(packed);
        Rpc(nameof(SyncPlayerVars), peerId, packed.Data);
    }

    public void ServerSyncPlayerVars(int peerId)
    {
        if (!NetworkManager.Instance.IsAuthoritative)
            return;

        if (MultiplayerGameWorld.PlayerVars.TryGetValue(peerId, out NetworkPlayerVars vars))
            ServerSyncPlayerVars(peerId, vars);
    }

    public override bool IsGameOver()
    {
        return NetworkManager.Instance.ElapsedGameTimeMinutes >= NetworkManager.Instance.Settings?.SessionLength;
    }

    public bool TryGetPlayer(int peerId, out TPlayer player)
    {
        player = default(TPlayer)!;

        if (!MultiplayerGameWorld.PlayerVars.TryGetValue(peerId, out NetworkPlayerVars vars))
            return false;

        if (!MultiplayerGameWorld.TryGetNetworkEntity(vars.EntityId, out INetworkEntity entity))
            return false;

        if (entity is not TPlayer casted)
            return false;

        player = casted;
        return true;
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

            LoadingScreen.Instance.Show(TranslationServer.Translate("REGISTERING_TO_SERVER"), MainGameState.Invalid);
            RpcId(NetworkManager.DEFAULT_SERVER_ID, nameof(ServerRequestRegistration));
        }
        else if (NetworkManager.Instance.IsAuthoritative)
        {
            OnReady();
        }
    }

    protected override void GameOver()
    {
        gameOver = true;
    }

    /// <summary>
    ///   Network updates goes here.
    /// </summary>
    protected abstract void NetworkTick(float delta);

    protected virtual void UpdateEntityState(int peerId, INetworkEntity entity)
    {
        if (!entity.EntityNode.IsInsideTree() || NetworkManager.Instance.IsClient)
            return;

        var buffer = new PackedBytesBuffer();
        entity.NetworkSerialize(buffer);

        if (buffer.Length > 0)
            RpcUnreliableId(peerId, nameof(NotifyEntityStateUpdate), entity.NetworkEntityId, buffer.Data);
    }

    /// <summary>
    ///   Registers incoming player to the server.
    /// </summary>
    protected virtual void RegisterPlayer(int peerId)
    {
        if (MultiplayerGameWorld.PlayerVars.ContainsKey(peerId) || NetworkManager.Instance.IsClient)
            return;

        // Register to the game world
        MultiplayerGameWorld.PlayerVars.Add(peerId, new NetworkPlayerVars());

        var species = CreateNewSpeciesForPlayer(peerId);
        MultiplayerGameWorld.UpdateSpecies(peerId, species);

        foreach (var player in NetworkManager.Instance.ConnectedPlayers)
        {
            if (player.Key == peerId || player.Value.Status != NetworkPlayerStatus.Active)
                continue;

            if (MultiplayerGameWorld.Species.TryGetValue((uint)player.Key, out Species otherSpecies))
            {
                // Send other players' species to the incoming player
                var otherSpeciesBuffer = new PackedBytesBuffer();
                otherSpecies.NetworkSerialize(otherSpeciesBuffer);
                RpcId(peerId, nameof(SyncSpecies), player.Key, otherSpeciesBuffer.Data);
            }
        }

        // Send the incoming player's species to all others
        var speciesBuffer = new PackedBytesBuffer();
        species.NetworkSerialize(speciesBuffer);
        Rpc(nameof(SyncSpecies), peerId, speciesBuffer.Data);

        SpawnPlayer(peerId);
    }

    /// <summary>
    ///   Unregisters outgoing player from the server.
    /// </summary>
    protected virtual void UnregisterPlayer(int peerId)
    {
        if (!NetworkManager.Instance.IsAuthoritative)
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
        if (entity is NetworkCharacter character && character.PeerId == NetworkManager.Instance.PeerId)
            OnLocalPlayerSpawned((TPlayer)entity);
    }

    /// <summary>
    ///   Called client-side when entity with the given id needs to be destroyed.
    /// </summary>
    protected virtual void OnNetEntityDestroy(uint entityId)
    {
        if (!MultiplayerGameWorld.TryGetNetworkEntity(entityId, out INetworkEntity entity))
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
        if (attached && !entity.EntityNode.IsInsideTree())
        {
            rootOfDynamicallySpawned.AddChild(entity.EntityNode);
        }
        else if (!attached && entity.EntityNode.IsInsideTree())
        {
            rootOfDynamicallySpawned.RemoveChild(entity.EntityNode);
        }
    }

    protected abstract Species CreateNewSpeciesForPlayer(int peerId);

    [PuppetSync]
    protected abstract void SyncSpecies(int peerId, byte[] serialized);

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

    /// <summary>
    ///   Replicates the given server-side entity to the specified target peer.
    /// </summary>
    private void RemoteSpawnEntity(int targetPeerId, INetworkEntity entity)
    {
        if (!NetworkManager.Instance.IsAuthoritative)
            return;

        var spawnData = new PackedBytesBuffer();
        entity.PackSpawnState(spawnData);
        RpcId(targetPeerId, nameof(NotifyEntitySpawn), entity.NetworkEntityId, entity.ResourcePath, spawnData.Data);
    }

    private void OnNetworkTick(object sender, float delta)
    {
        if (NetworkManager.Instance.IsAuthoritative)
            NetworkUpdateGameState(delta);

        NetworkTick(delta);
    }

    private void NetworkUpdateGameState(float delta)
    {
        for (int i = MultiplayerGameWorld.EntityIDs.Count - 1; i >= 0; --i)
        {
            var id = MultiplayerGameWorld.EntityIDs[i];

            var entity = MultiplayerGameWorld.Entities[id].Value;
            if (entity == null || !entity.EntityNode.IsInsideTree())
                continue;

            foreach (var player in NetworkManager.Instance.ConnectedPlayers)
            {
                if (player.Value.Status != NetworkPlayerStatus.Active ||
                    player.Key == NetworkManager.DEFAULT_SERVER_ID)
                {
                    continue;
                }

                UpdateEntityState(player.Key, entity);
            }
        }
    }

    private void OnNetEntitySpawned(INetworkEntity spawned)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        MultiplayerGameWorld.RegisterNetworkEntity(spawned);

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

        MultiplayerGameWorld.UnregisterNetworkEntity(id);

        foreach (var player in NetworkManager.Instance.ConnectedPlayers)
        {
            if (player.Key == NetworkManager.DEFAULT_SERVER_ID || player.Value.Status != NetworkPlayerStatus.Active)
                continue;

            RpcId(player.Key, nameof(DestroySpawnedEntity), id);
        }
    }

    private void OnPeerDisconnected(int peerId)
    {
        if (NetworkManager.Instance.IsAuthoritative)
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

        if (NetworkManager.Instance.IsAuthoritative)
        {
            RegisterPlayer(NetworkManager.Instance.PeerId);
            Rpc(nameof(NotifyServerReady));
        }
    }

    [Puppet]
    private void NotifyServerReady()
    {
        TransitionManager.Instance.AddSequence(ScreenFade.FadeType.FadeIn, 0.5f, null, false, false);
        LoadingScreen.Instance.Show(TranslationServer.Translate("REGISTERING_TO_SERVER"), MainGameState.Invalid);
        RpcId(NetworkManager.DEFAULT_SERVER_ID, nameof(ServerRequestRegistration));
    }

    [PuppetSync]
    private void SyncPlayerVar(int peerId, string key, object value)
    {
        if (!MultiplayerGameWorld.PlayerVars.ContainsKey(peerId))
            MultiplayerGameWorld.PlayerVars[peerId] = new NetworkPlayerVars();

        MultiplayerGameWorld.PlayerVars[peerId].SetVar(key, value);
    }

    [Puppet]
    private void SyncPlayerVars(int peerId, byte[] packedData)
    {
        var buffer = new PackedBytesBuffer(packedData);
        var vars = new NetworkPlayerVars();
        vars.NetworkDeserialize(buffer);
        MultiplayerGameWorld.PlayerVars[peerId] = vars;
    }

    [PuppetSync]
    private void DestroyPlayerVars(int peerId)
    {
        MultiplayerGameWorld.PlayerVars.Remove(peerId);
    }

    [Puppet]
    private void NotifyEntitySpawn(uint entityId, string resourcePath, byte[]? data)
    {
        if (MultiplayerGameWorld.Entities.ContainsKey(entityId))
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
        MultiplayerGameWorld.RegisterNetworkEntity(entityId, spawned);
        OnNetEntitySpawned(entityId, spawned);
    }

    [Puppet]
    private void DestroySpawnedEntity(uint entityId)
    {
        OnNetEntityDestroy(entityId);
        MultiplayerGameWorld.UnregisterNetworkEntity(entityId);
    }

    [PuppetSync]
    private void NotifyEntityStateUpdate(uint id, byte[] data)
    {
        if (!MultiplayerGameWorld.TryGetNetworkEntity(id, out INetworkEntity entity))
        {
            RpcId(NetworkManager.DEFAULT_SERVER_ID, nameof(RequestEntitySpawn), id);
            return;
        }

        if (!entity.EntityNode.IsInsideTree())
            return;

        entity.NetworkDeserialize(new PackedBytesBuffer(data));
    }

    [Puppet]
    private void NotifyEntitiesDownload(int entitiesCount)
    {
        incomingEntitiesCount = entitiesCount;
        NetworkManager.Instance.Print("Downloading entities");
    }

    [Remote]
    private void ServerRequestRegistration()
    {
        if (NetworkManager.Instance.IsClient)
            return;

        var sender = GetTree().GetRpcSenderId();

        // Upload player states
        foreach (var state in MultiplayerGameWorld.PlayerVars)
        {
            var packed = new PackedBytesBuffer();
            state.Value.NetworkSerialize(packed);
            RpcId(sender, nameof(SyncPlayerVars), state.Key, packed.Data);
        }

        RegisterPlayer(sender);

        RpcId(sender, nameof(NotifyEntitiesDownload), MultiplayerGameWorld.EntityCount);

        // Upload entities to the client
        foreach (var entity in MultiplayerGameWorld.Entities.Values)
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

        if (MultiplayerGameWorld.TryGetNetworkEntity(entityId, out INetworkEntity entity))
            RemoteSpawnEntity(sender, entity);
    }
}
