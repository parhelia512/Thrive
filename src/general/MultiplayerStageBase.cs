using System.Collections.Generic;
using System.Globalization;
using Godot;

/// <summary>
///   Base stage for the stages where the player controls a single creature, supports online gameplay.
/// </summary>
/// <typeparam name="TPlayer">The type of the player object</typeparam>
/// <remarks>
///   <para>
///     TODO: perhaps this can be combined into the normal StageBase to remove redundancies and
///     to make singleplayer to multiplayer seamless.
///   </para>
/// </remarks>
public abstract class MultiplayerStageBase<TPlayer> : StageBase<TPlayer>
    where TPlayer : class, INetEntity
{
    private float networkTick;

    private uint netEntitiesIdCounter;

    /// <summary>
    ///   Dictionary of players that has joined the game.
    /// </summary>
    private readonly Dictionary<int, EntityReference<TPlayer>> players = new();

    private readonly Dictionary<int, float> respawnTimers = new();
    private Dictionary<int, Species> playerSpeciesList = new();

    /// <summary>
    ///   Dictionary of players that has joined the game.
    /// </summary>
    public IReadOnlyDictionary<int, EntityReference<TPlayer>> Players => players;

    public IReadOnlyDictionary<int, Species> PlayerSpeciesList => playerSpeciesList;

    public override void _Ready()
    {
        base._Ready();

        GetTree().Connect("network_peer_disconnected", this, nameof(OnPeerDisconnected));
        GetTree().Connect("server_disconnected", this, nameof(OnServerDisconnected));

        NetworkManager.Instance.Connect(nameof(NetworkManager.PlayerJoined), this, nameof(RegisterPlayer));
        NetworkManager.Instance.Connect(nameof(NetworkManager.PlayerLeft), this, nameof(UnRegisterPlayer));
        NetworkManager.Instance.Connect(nameof(NetworkManager.Kicked), this, nameof(OnKicked));

        if (IsNetworkMaster())
        {
            SpawnHelpers.OnNetEntitySpawned = OnNetEntitySpawned;
            SpawnHelpers.OnNetEntityDespawned = OnNetEntityDespawned;
        }
    }

    public override void _Process(float delta)
    {
        networkTick += delta;

        if (!GetTree().HasNetworkPeer() || !GetTree().IsNetworkServer())
            return;

        var network = NetworkManager.Instance;

        if (!network.IsDedicated && network.Player!.Status != NetPlayerStatus.InGame)
            return;

        if (network.GameInSession && networkTick > network.UpdateRateDelay)
        {
            NetworkUpdateGameState(delta + networkTick);
            NetworkHandleRespawns(delta + networkTick);
            networkTick = 0;
        }
    }

    public override void OnFinishLoading(Save save)
    {
    }

    protected override void OnGameStarted()
    {
        if (!NetworkManager.Instance.IsDedicated && GetTree().IsNetworkServer())
            RegisterPlayer(GetTree().GetNetworkUniqueId());
    }

    protected virtual void NetworkUpdateGameState(float delta)
    {
        for (int i = 0; i < rootOfDynamicallySpawned.GetChildCount(); ++i)
        {
            var child = rootOfDynamicallySpawned.GetChild(i);

            if (child is not INetEntity netObject)
                return;

            foreach (var player in NetworkManager.Instance.PlayerList)
            {
                if (player.Key == GetTree().GetNetworkUniqueId() || player.Value.Status != NetPlayerStatus.InGame)
                    continue;

                netObject.NetworkSyncEveryFrame(player.Key);
            }
        }
    }

    protected virtual void NetworkHandleRespawns(float delta)
    {
        foreach (var player in NetworkManager.Instance.PlayerList)
        {
            if (player.Value.Status != NetPlayerStatus.InGame)
                continue;

            if (players.ContainsKey(player.Key) || !respawnTimers.ContainsKey(player.Key))
                continue;

            var diff = respawnTimers[player.Key] - delta;
            respawnTimers[player.Key] = diff;

            // Respawn the player once the timer is up
            if (respawnTimers[player.Key] <= 0)
            {
                SpawnPlayer(player.Key);
            }
        }
    }

    protected virtual void OnNetEntityReplicated(INetEntity entity)
    {
        rootOfDynamicallySpawned.AddChild(entity.EntityNode);

        if (int.TryParse(entity.EntityNode.Name, out int parsedId) && parsedId == GetTree().GetNetworkUniqueId())
            OnLocalPlayerSpawned((TPlayer)entity);
    }

    protected virtual void OnLocalPlayerSpawned(TPlayer player)
    {
        Player = player;
        spawnedPlayer = true;
    }

    protected void SpawnPlayer(int peerId)
    {
        if (!IsNetworkMaster())
            return;

        if (!players.ContainsKey(peerId))
        {
            if (!CreatePlayer(peerId, out TPlayer? spawned))
                return;

            if (peerId == GetTree().GetNetworkUniqueId())
                OnLocalPlayerSpawned(spawned!);

            spawned!.EntityNode.Name = peerId.ToString(CultureInfo.CurrentCulture);
            players.Add(peerId, new EntityReference<TPlayer>(spawned!));
            respawnTimers[peerId] = Constants.PLAYER_RESPAWN_TIME;
        }
    }

    protected void DespawnPlayer(int peerId)
    {
        if (!IsNetworkMaster())
            return;

        if (players.TryGetValue(peerId, out EntityReference<TPlayer> peer))
        {
            if (peer.Value != null)
            {
                var entityId = peer.Value.NetEntityId;

                if (!DestroyPlayer(peer.Value))
                    return;

                SpawnHelpers.OnNetEntityDespawned?.Invoke(entityId);
            }

            players.Remove(peerId);
        }
    }

    /// <summary>
    ///   Returns true if successfully spawned.
    /// </summary>
    protected abstract bool CreatePlayer(int peerId, out TPlayer? spawned);

    /// <summary>
    ///   Returns true if successfully despawned.
    protected abstract bool DestroyPlayer(TPlayer removed);

    [RemoteSync]
    protected override void SpawnPlayer()
    {
        SpawnPlayer(GetTree().GetNetworkUniqueId());
    }

    protected override void AutoSave()
    {
    }

    protected override void PerformQuickSave()
    {
    }

    private void RegisterPlayer(int peerId)
    {
        if (players.ContainsKey(peerId) || !GetTree().IsNetworkServer())
            return;

        if (!playerSpeciesList.TryGetValue(peerId, out Species species))
        {
            species = GameWorld.CreateMutatedSpecies(GameWorld.PlayerSpecies);
            playerSpeciesList[peerId] = species;
            Rpc(nameof(SyncPlayerSpeciesList), ThriveJsonConverter.Instance.SerializeObject(playerSpeciesList));
        }

        SpawnPlayer(peerId);
    }

    private void UnRegisterPlayer(int peerId)
    {
        if (!GetTree().IsNetworkServer())
            return;

        if (playerSpeciesList.Remove(peerId))
            Rpc(nameof(SyncPlayerSpeciesList), ThriveJsonConverter.Instance.SerializeObject(playerSpeciesList));

        respawnTimers.Remove(peerId);

        DespawnPlayer(peerId);
    }

    [Puppet]
    private void SyncPlayerSpeciesList(string data)
    {
        playerSpeciesList = ThriveJsonConverter.Instance.DeserializeObject<Dictionary<int, Species>>(data)!;
    }

    [Puppet]
    private void ReplicateSpawnedEntity(string data)
    {
        var deserialized = ThriveJsonConverter.Instance.DeserializeObject<INetEntity>(data);
        if (deserialized == null)
            return;

        OnNetEntityReplicated(deserialized);
    }

    [Puppet]
    private void DestroySpawnedEntity(int id)
    {
        for (int i = rootOfDynamicallySpawned.GetChildCount() - 1; i >= 0; --i)
        {
            var child = rootOfDynamicallySpawned.GetChild(i);

            if (child is INetEntity netEntity && netEntity.NetEntityId == id)
            {
                netEntity.DestroyDetachAndQueueFree();
                break;
            }
        }
    }

    private void OnNetEntitySpawned(INetEntity spawned)
    {
        spawned.NetEntityId = ++netEntitiesIdCounter;

        Rpc(nameof(ReplicateSpawnedEntity), ThriveJsonConverter.Instance.SerializeObject(spawned));
    }

    private void OnNetEntityDespawned(uint id)
    {
        Rpc(nameof(DestroySpawnedEntity), id);
    }

    private void OnPeerDisconnected(int peerId)
    {
        if (GetTree().IsNetworkServer())
            UnRegisterPlayer(peerId);
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
}
