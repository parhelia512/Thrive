using System.Collections.Generic;
using Godot;

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
    where TPlayer : class, INetEntity
{
    private Dictionary<uint, Node> entityParents = new();

    protected MultiplayerGameWorld MpGameWorld => (MultiplayerGameWorld)GameWorld;

    protected abstract string StageLoadingMessage { get; }

    public override void _EnterTree()
    {
        base._EnterTree();

        NetworkManager.Instance.NetworkTick += OnNetworkTick;
        NetworkManager.Instance.EntityReplicated += OnNetEntityReplicated;
        NetworkManager.Instance.EntityDestroy += OnNetEntityDestroy;
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        NetworkManager.Instance.NetworkTick -= OnNetworkTick;
        NetworkManager.Instance.EntityReplicated -= OnNetEntityReplicated;
        NetworkManager.Instance.EntityDestroy -= OnNetEntityDestroy;
    }

    public override void _Ready()
    {
        base._Ready();

        GetTree().Connect("network_peer_disconnected", this, nameof(OnPeerDisconnected));
        GetTree().Connect("server_disconnected", this, nameof(OnServerDisconnected));

        NetworkManager.Instance.Connect(nameof(NetworkManager.PlayerJoined), this, nameof(RegisterPlayer));
        NetworkManager.Instance.Connect(nameof(NetworkManager.PlayerLeft), this, nameof(UnregisterPlayer));
        NetworkManager.Instance.Connect(nameof(NetworkManager.Kicked), this, nameof(OnKicked));
    }

    public override void _Process(float delta)
    {
    }

    public override void OnFinishLoading(Save save)
    {
    }

    protected override void SetupStage()
    {
        LoadingScreen.Instance.Show(StageLoadingMessage, MainGameState.Invalid, "Preparing...");
        TransitionManager.Instance.AddSequence(ScreenFade.FadeType.FadeIn, 0.5f, null, false, false);
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

    protected virtual void UpdateEntityState(int peerId, INetEntity entity)
    {
        if (!entity.EntityNode.IsInsideTree())
            return;

        if (NetworkManager.Instance.IsAuthoritative)
        {
            if (peerId == NetworkManager.Instance.PeerId)
                return;

            var states = entity.PackStates();

            if (states != null)
                RpcUnreliableId(peerId, nameof(NotifyEntityStateUpdate), entity.NetEntityId, states);
        }
        else if (NetworkManager.Instance.IsPuppet)
        {
            var inputs = entity.PackInputs();

            if (inputs != null)
                RpcUnreliable(nameof(ReceivedEntityInput), entity.NetEntityId, inputs);
        }
    }

    protected virtual void OnNetEntityReplicated(object sender, EntityReplicatedEventArgs args)
    {
        rootOfDynamicallySpawned.AddChild(args.Entity.EntityNode);

        if (args.Entity is INetPlayer player && player.PeerId == NetworkManager.Instance.PeerId)
            OnOwnPlayerSpawned((TPlayer)args.Entity);

        args.Entity.OnReplicated();

        if (NetworkManager.Instance.PlayerInfo?.Status == NetPlayerStatus.Joining)
        {
            LoadingScreen.Instance.Show(StageLoadingMessage,
                MainGameState.Invalid, "Loading entities... " + MpGameWorld?.EntityCount + "/" +
                args.ServerEntityCount);

            if (args.ServerEntityCount > -1 && MpGameWorld?.EntityCount == args.ServerEntityCount)
                OnReady();
        }
    }

    /// <summary>
    ///   Called when entity with the given id needs to be destroyed.
    /// </summary>
    protected virtual void OnNetEntityDestroy(object sender, uint entityId)
    {
        var entity = MpGameWorld.GetEntity(entityId);
        if (entity == null)
            return;

        if (entityId == NetworkManager.Instance.PlayerState!.Value.EntityID)
            OnOwnPlayerDespawn();

        entity.DestroyDetachAndQueueFree();
        entityParents.Remove(entityId);
    }

    /// <summary>
    ///   If the entity we're controlling is spawned.
    /// </summary>
    protected virtual void OnOwnPlayerSpawned(TPlayer player)
    {
        Player = player;
        spawnedPlayer = true;
    }

    /// <summary>
    ///   If the entity we're controlling is despawned.
    /// </summary>
    protected virtual void OnOwnPlayerDespawn()
    {
        Player = null;
    }

    protected void SpawnPlayer(int peerId)
    {
        if (NetworkManager.Instance.IsPuppet)
            return;

        if (!HandlePlayerSpawn(peerId, out TPlayer? spawned))
            return;

        if (peerId == GetTree().GetNetworkUniqueId())
            OnOwnPlayerSpawned(spawned!);
    }

    protected void DespawnPlayer(int peerId)
    {
        if (NetworkManager.Instance.IsPuppet)
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
    ///   Returns true if successfully spawned.
    /// </summary>
    protected abstract bool HandlePlayerSpawn(int peerId, out TPlayer? spawned);

    /// <summary>
    ///   Returns true if successfully despawned.
    protected abstract bool HandlePlayerDespawn(TPlayer removed);

    [RemoteSync]
    protected override void SpawnPlayer()
    {
        if (HasPlayer)
            return;

        SpawnPlayer(GetTree().GetNetworkUniqueId());
    }

    protected override void AutoSave()
    {
    }

    protected override void PerformQuickSave()
    {
    }

    [PuppetSync]
    protected virtual void SetEntityAsAttached(uint id, bool attached)
    {
        if (!entityParents.ContainsKey(id))
            return;

        var entity = MpGameWorld.GetEntity(id);
        if (entity == null)
            return;

        var parent = entityParents[id];

        if (attached && !entity.EntityNode.IsInsideTree())
        {
            parent.AddChild(entity.EntityNode);
        }
        else if (!attached && entity.EntityNode.IsInsideTree())
        {
            parent.RemoveChild(entity.EntityNode);
        }
    }

    private void OnNetworkTick(object sender, float delta)
    {
        NetworkUpdateGameState(delta);
        NetworkTick(delta);
    }

    private void NetworkUpdateGameState(float delta)
    {
        foreach (var entry in MpGameWorld.Entities)
        {
            var entity = entry.Value.Value;
            if (entity == null)
                continue;

            if (!entity.Synchronize || !entity.EntityNode.IsInsideTree())
                continue;

            if (NetworkManager.Instance.IsAuthoritative)
                entityParents[entry.Key] = entity.EntityNode.GetParent();

            foreach (var player in NetworkManager.Instance.PlayerList)
            {
                if (player.Value.Status != NetPlayerStatus.Active)
                    continue;

                UpdateEntityState(player.Key, entity);
            }
        }
    }

    private void RegisterPlayer(int peerId)
    {
        if (MpGameWorld.Players.ContainsKey(peerId) || NetworkManager.Instance.IsPuppet)
            return;

        // Pretend that each separate Species instance across players are LUCA
        var species = new MicrobeSpecies((uint)peerId, "Primum", "thrivium");
        GameWorld.SetInitialSpeciesProperties(species);
        MpGameWorld.UpdateSpecies(species.ID, species);

        MpGameWorld.Players.Add(peerId, new NetPlayerState());
        NetworkManager.Instance.SyncPlayerStateToAllPlayers(peerId, MpGameWorld.Players[peerId]);

        SpawnPlayer(peerId);
    }

    private void UnregisterPlayer(int peerId)
    {
        if (NetworkManager.Instance.IsPuppet)
            return;

        DespawnPlayer(peerId);

        MpGameWorld.Players.Remove(peerId);
        NetworkManager.Instance.SyncPlayerStateToAllPlayers(peerId, null);
    }

    private void OnPeerDisconnected(int peerId)
    {
        if (NetworkManager.Instance.IsAuthoritative)
            UnregisterPlayer(peerId);
    }

    private void OnServerDisconnected()
    {
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
            if (!NetworkManager.Instance.IsDedicated)
                RegisterPlayer(GetTree().GetNetworkUniqueId());

            NotifyGameReady();
            LoadingScreen.Instance.Hide();
            BaseHUD.OnEnterStageTransition(true, false);
        }, false, false);
    }

    [Puppet]
    private void NotifyEntityStateUpdate(uint id, Dictionary<string, string> data)
    {
        var entity = MpGameWorld.GetEntity(id);
        if (entity == null)
        {
            // TODO: recreate entity
            return;
        }

        entityParents[id] = entity.EntityNode.GetParent();

        entity.OnNetworkSync(data);
    }

    [Master]
    private void ReceivedEntityInput(uint id, Dictionary<string, string> data)
    {
        var entity = MpGameWorld.GetEntity(id);
        if (entity == null)
            return;

        entity.OnNetworkInput(data);
    }
}
