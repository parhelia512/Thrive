using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   Base stage for the stages where the player controls a single creature, supports online gameplay.
/// </summary>
/// <typeparam name="TPlayer">The type of the player object</typeparam>
/// <remarks>
///   <para>
///     This must ALWAYS be attached to the scene tree during gameplay as this acts as an intermediary
///     for communications between the server and the client for in-game entities.
///   </para>
///   <para>
///     TODO: perhaps this can be combined into the normal StageBase to remove redundancies and
///     to make singleplayer to multiplayer seamless.
///   </para>
/// </remarks>
public abstract class MultiplayerStageBase<TPlayer> : StageBase<TPlayer>
    where TPlayer : class, INetPlayer
{
    public NetPlayerState PlayerState => GetPlayerState(NetworkManager.Instance.PeerId!.Value) ??
        throw new NullReferenceException("Player has not been set");

    public MultiplayerGameWorld MpGameWorld => (MultiplayerGameWorld)GameWorld;

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
    }

    public override void OnFinishLoading(Save save)
    {
    }

    public NetPlayerState? GetPlayerState(int peerId)
    {
        MpGameWorld.Players.TryGetValue(peerId, out NetPlayerState result);
        return result;
    }

    public void SyncPlayerStateToAllPlayers(int peerId, NetPlayerState? state)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        var serialized = state == null ? string.Empty : JsonConvert.SerializeObject(state);
        Rpc(nameof(SyncPlayerState), peerId, serialized);
    }

    protected override void SetupStage()
    {
        LoadingScreen.Instance.Show(StageLoadingMessage, MainGameState.Invalid, "Preparing...");
        TransitionManager.Instance.AddSequence(ScreenFade.FadeType.FadeIn, 0.5f, null, false, false);

        pauseMenu.GameProperties = CurrentGame ?? throw new InvalidOperationException("current game is not set");
    }

    protected override void OnGameStarted()
    {
        if (NetworkManager.Instance.IsAuthoritative)
            OnReady();
    }

    /// <summary>
    ///   Network updates goes here.
    /// </summary>
    protected abstract void NetworkTick(float delta);

    protected virtual void UpdateEntityState(float delta, int targetPeerId, INetEntity entity)
    {
        if (!entity.EntityNode.IsInsideTree() || NetworkManager.Instance.IsClient)
            return;

        entity.NetworkTick(delta);

        var states = entity.PackStates();

        if (states != null)
            RpcUnreliableId(targetPeerId, nameof(NotifyEntityStateUpdate), delta, entity.NetEntityId, states);
    }

    protected virtual void SendPlayerInputs()
    {
        if (Player?.EntityNode.IsInsideTree() == false || NetworkManager.Instance.IsAuthoritative)
            return;

        var inputs = Player?.PackInputs();

        if (inputs != null)
            RpcUnreliable(nameof(PlayerInputsReceived), NetworkManager.Instance.PeerId, inputs);
    }

    protected virtual void RegisterPlayer(int peerId)
    {
        if (MpGameWorld.Players.ContainsKey(peerId) || NetworkManager.Instance.IsClient)
            return;

        // Pretend that each separate Species instance across players are LUCA
        var species = new MicrobeSpecies((uint)peerId, "Primum", "thrivium");
        GameWorld.SetInitialSpeciesProperties(species);
        MpGameWorld.UpdateSpecies(species.ID, species);

        MpGameWorld.Players.Add(peerId, default);
        SyncPlayerStateToAllPlayers(peerId, MpGameWorld.Players[peerId]);

        SpawnPlayer(peerId);

        if (peerId != NetworkManager.Instance.PeerId)
        {
            foreach (var entry in MpGameWorld.Entities)
            {
                var entity = entry.Value.Value;
                if (entity == null)
                    continue;

                ReplicateEntity(peerId, entity, MpGameWorld.EntityCount);
            }
        }
    }

    protected virtual void UnregisterPlayer(int peerId)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        DespawnPlayer(peerId);

        MpGameWorld.Players.Remove(peerId);
        SyncPlayerStateToAllPlayers(peerId, null);
    }

    /// <summary>
    ///   Called client-side when entity with the given id has been replicated.
    /// </summary>
    protected virtual void OnNetEntityReplicated(uint id, INetEntity entity, int serverEntityCount)
    {
        rootOfDynamicallySpawned.AddChild(entity.EntityNode);

        if (entity is INetPlayer player && player.PeerId == NetworkManager.Instance.PeerId)
            OnOwnPlayerSpawned((TPlayer)entity);

        if (NetworkManager.Instance.PlayerInfo?.Status == NetPlayerStatus.Joining)
        {
            LoadingScreen.Instance.Show(StageLoadingMessage,
                MainGameState.Invalid, "Loading entities... " + MpGameWorld?.EntityCount + "/" +
                serverEntityCount);

            if (serverEntityCount > -1 && MpGameWorld?.EntityCount == serverEntityCount)
            {
                Rpc(nameof(RequestExcessEntitiesRemoval));
                Rpc(nameof(RequestServerSidePlayerStates));
                OnReady();
            }
        }
    }

    /// <summary>
    ///   Called client-side when entity with the given id needs to be destroyed.
    /// </summary>
    protected virtual void OnNetEntityDestroy(uint entityId)
    {
        var entity = MpGameWorld.GetEntity(entityId);
        if (entity == null)
            return;

        if (entityId == PlayerState.EntityID)
            OnOwnPlayerDespawn();

        entity.DestroyDetachAndQueueFree();
    }

    /// <summary>
    ///   If the the local entity we're controlling has been spawned.
    /// </summary>
    protected virtual void OnOwnPlayerSpawned(TPlayer player)
    {
        Player = player;
        spawnedPlayer = true;
    }

    /// <summary>
    ///   If the local entity we're controlling has been despawned.
    /// </summary>
    protected virtual void OnOwnPlayerDespawn()
    {
        Player = null;
    }

    protected void SpawnPlayer(int peerId)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        if (!HandlePlayerSpawn(peerId, out TPlayer? spawned))
            return;

        if (peerId == GetTree().GetNetworkUniqueId())
            OnOwnPlayerSpawned(spawned!);
    }

    protected void DespawnPlayer(int peerId)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        if (!MpGameWorld.Players.TryGetValue(peerId, out NetPlayerState state))
            return;

        var entity = MpGameWorld.GetEntity(state.EntityID);
        if (entity == null)
            return;

        if (!HandlePlayerDespawn((TPlayer)entity))
            return;

        if (peerId == GetTree().GetNetworkUniqueId())
            OnOwnPlayerDespawn();
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

        SpawnPlayer(NetworkManager.Instance.PeerId!.Value);
    }

    protected override void AutoSave()
    {
    }

    protected override void PerformQuickSave()
    {
    }

    protected virtual void SetEntityAsAttached(INetEntity entity, bool attached)
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

    /// <summary>
    ///   Gamemode specific score calculation.
    /// </summary>
    protected abstract int CalculateScore(int peerId);

    /// <summary>
    ///   Notifies a player's new score to all other peers.
    /// </summary>
    /// <param name="peerId">The player's peer id.</param>
    protected void NotifyScore(int peerId)
    {
        NetworkManager.Instance.SetPlayerInfoInts(peerId, "score", CalculateScore(peerId));
    }

    private void ReplicateEntity(int targetPeerId, INetEntity entity, int serverEntityCount = -1)
    {
        var replicableVars = entity.PackReplicableVars();
        var states = entity.PackStates();

        var data = new Dictionary<string, string>
        {
            { nameof(INetEntity.ResourcePath), entity.ResourcePath },
            { nameof(IEntity.EntityNode.Name), entity.EntityNode.Name },
        };

        if (replicableVars != null)
            data.Add("Vars", JsonConvert.SerializeObject(replicableVars));

        if (states != null)
            data.Add("States", JsonConvert.SerializeObject(states));

        RpcId(targetPeerId, nameof(NotifyEntityReplication), entity.NetEntityId, data, serverEntityCount);
    }

    private void OnNetworkTick(object sender, float delta)
    {
        if (NetworkManager.Instance.IsAuthoritative)
        {
            NetworkUpdateGameState(delta);
        }
        else if (NetworkManager.Instance.IsClient)
        {
            SendPlayerInputs();
        }

        NetworkTick(delta);
    }

    private void NetworkUpdateGameState(float delta)
    {
        for (int i = MpGameWorld.EntityIDs.Count - 1; i >= 0; --i)
        {
            var id = MpGameWorld.EntityIDs[i];

            var entity = MpGameWorld.Entities[id].Value;
            if (entity == null)
                continue;

            if (!entity.Synchronize || !entity.EntityNode.IsInsideTree())
                continue;

            foreach (var player in NetworkManager.Instance.PlayerList)
            {
                if (player.Value.Status != NetPlayerStatus.Active || player.Key == NetworkManager.DEFAULT_SERVER_ID)
                    continue;

                UpdateEntityState(delta, player.Key, entity);
            }
        }
    }

    private void OnNetEntitySpawned(INetEntity spawned)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        var id = MpGameWorld.RegisterEntity(spawned);

        foreach (var player in NetworkManager.Instance.PlayerList)
        {
            if (player.Key == GetTree().GetNetworkUniqueId() || player.Value.Status != NetPlayerStatus.Active)
                continue;

            ReplicateEntity(player.Key, spawned);
        }
    }

    private void OnNetEntityDespawned(uint id)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        MpGameWorld.UnregisterEntity(id);

        foreach (var player in NetworkManager.Instance.PlayerList)
        {
            if (player.Key == NetworkManager.DEFAULT_SERVER_ID || player.Value.Status != NetPlayerStatus.Active)
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
        menu.OpenMultiplayerMenu(MultiplayerGUI.Submenu.Main);
    }

    private void OnKicked(string reason)
    {
        var menu = SceneManager.Instance.ReturnToMenu();
        menu.OpenMultiplayerMenu(MultiplayerGUI.Submenu.Main);
        menu.ShowKickedDialog(reason);
    }

    private void OnReady()
    {
        TransitionManager.Instance.AddSequence(ScreenFade.FadeType.FadeOut, 0.5f, () =>
        {
            if (NetworkManager.Instance.IsAuthoritative)
                RegisterPlayer(NetworkManager.Instance.PeerId!.Value);

            NotifyGameReady();
            LoadingScreen.Instance.Hide();
            BaseHUD.OnEnterStageTransition(true, false);
        }, false, false);
    }

    [Puppet]
    private void SyncPlayerState(int peerId, string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            MpGameWorld.Players.Remove(peerId);
            return;
        }

        MpGameWorld.Players[peerId] = JsonConvert.DeserializeObject<NetPlayerState>(data);
    }

    [Puppet]
    private void NotifyEntityReplication(uint id, Dictionary<string, string> data, int serverEntityCount = -1)
    {
        data.TryGetValue(nameof(INetEntity.ResourcePath), out string resourcePath);
        data.TryGetValue(nameof(IEntity.EntityNode.Name), out string name);

        data.TryGetValue("Vars", out string vars);
        data.TryGetValue("States", out string states);

        Dictionary<string, string>? parsedVars = null;
        Dictionary<string, string>? parsedStates = null;

        if (!string.IsNullOrEmpty(vars))
            parsedVars = JsonConvert.DeserializeObject<Dictionary<string, string>>(vars);

        if (!string.IsNullOrEmpty(states))
            parsedStates = JsonConvert.DeserializeObject<Dictionary<string, string>>(states);

        var scene = GD.Load<PackedScene>(resourcePath);
        var replicated = scene.Instance<INetEntity>();

        replicated.EntityNode.Name = name;

        replicated.OnReplicated(parsedVars);
        MpGameWorld.RegisterEntity(id, replicated);
        OnNetEntityReplicated(id, replicated, serverEntityCount);

        if (parsedStates != null)
            replicated.OnNetworkSync(parsedStates);
    }

    [Puppet]
    private void DestroySpawnedEntity(uint entityId)
    {
        OnNetEntityDestroy(entityId);
        MpGameWorld.UnregisterEntity(entityId);
    }

    [Puppet]
    private void NotifyEntityStateUpdate(float delta, uint id, Dictionary<string, string> data)
    {
        var entity = MpGameWorld.GetEntity(id);
        if (entity == null)
        {
            // TODO: recreate entity
            return;
        }

        if (!entity.EntityNode.IsInsideTree())
            return;

        entity.NetworkTick(delta);
        entity.OnNetworkSync(data);
    }

    [Puppet]
    private void ReceivedExcessEntitiesRemoval(string ids)
    {
        var deserialized = JsonConvert.DeserializeObject<List<uint>>(ids);
        if (deserialized == null)
            return;

        var excess = MpGameWorld.Entities.Select(e => e.Key).Except(deserialized).ToList();

        foreach (var id in excess)
            DestroySpawnedEntity(id);
    }

    [Master]
    private void PlayerInputsReceived(int peerId, Dictionary<string, string> data)
    {
        if (peerId == NetworkManager.DEFAULT_SERVER_ID)
            return;

        var state = GetPlayerState(peerId);
        if (!state.HasValue)
            return;

        var entity = MpGameWorld.GetEntity(state.Value.EntityID);
        if (entity == null || entity is not INetPlayer player)
            return;

        player.OnNetworkInput(data);
    }

    [Master]
    private void RequestServerSidePlayerStates()
    {
        var sender = GetTree().GetRpcSenderId();

        foreach (var state in MpGameWorld.Players)
            RpcId(sender, nameof(SyncPlayerState), state.Key, JsonConvert.SerializeObject(state.Value));
    }

    [Master]
    private void RequestExcessEntitiesRemoval()
    {
        var sender = GetTree().GetRpcSenderId();

        RpcId(sender, nameof(ReceivedExcessEntitiesRemoval), JsonConvert.SerializeObject(MpGameWorld.EntityIDs));
    }
}
