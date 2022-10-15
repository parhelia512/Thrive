using System.Collections.Generic;
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
    where TPlayer : class, IEntity
{
    private float networkTick;

    private readonly Dictionary<int, EntityReference<TPlayer>> players = new();
    private readonly Dictionary<int, float> respawnTimers = new();
    private Dictionary<int, Species> playerSpeciesList = new();

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
    }

    public override void _Process(float delta)
    {
        networkTick += delta;

        if (!GetTree().HasNetworkPeer() || !IsNetworkMaster())
            return;

        if (!NetworkManager.Instance.IsDedicated && NetworkManager.Instance.Player!.Status != NetPlayerStatus.InGame)
            return;

        if (NetworkManager.Instance.GameInSession && networkTick > NetworkManager.Instance.TickRateDelay)
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

    protected abstract void NetworkUpdateGameState(float delta);

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
                Rpc(nameof(SpawnPlayer), player.Key);
            }
        }
    }

    [RemoteSync]
    protected void SpawnPlayer(int peerId)
    {
        if (!players.ContainsKey(peerId))
        {
            if (!OnPlayerSpawn(peerId, out TPlayer? spawned))
                return;

            players.Add(peerId, new EntityReference<TPlayer>(spawned!));
            respawnTimers[peerId] = Constants.PLAYER_RESPAWN_TIME;

            if (peerId == GetTree().GetNetworkUniqueId())
                Player = spawned;
        }
    }

    [RemoteSync]
    protected void DeSpawnPlayer(int peerId)
    {
        if (players.TryGetValue(peerId, out EntityReference<TPlayer> peer))
        {
            if (peer.Value != null)
            {
                if (!OnPlayerDeSpawn(peer.Value))
                    return;
            }

            players.Remove(peerId);
        }
    }

    /// <summary>
    ///   Returns true if successfully spawned.
    /// </summary>
    protected abstract bool OnPlayerSpawn(int peerId, out TPlayer? spawned);

    /// <summary>
    ///   Returns true if successfully despawned.
    protected abstract bool OnPlayerDeSpawn(TPlayer removed);

    [RemoteSync]
    protected override void SpawnPlayer()
    {
        Rpc(nameof(SpawnPlayer), GetTree().GetNetworkUniqueId());
    }

    protected override void AutoSave()
    {
    }

    protected override void PerformQuickSave()
    {
    }

    private void RegisterPlayer(int peerId)
    {
        if (!GetTree().IsNetworkServer())
            return;

        if (!playerSpeciesList.TryGetValue(peerId, out Species species))
        {
            species = GameWorld.CreateMutatedSpecies(GameWorld.PlayerSpecies);
            playerSpeciesList[peerId] = species;
            Rpc(nameof(SyncPlayerSpeciesList), ThriveJsonConverter.Instance.SerializeObject(playerSpeciesList));
        }

        foreach (var player in NetworkManager.Instance.PlayerList)
        {
            if (player.Value.Status == NetPlayerStatus.InGame)
            {
                RpcId(player.Key, nameof(SpawnPlayer), peerId);
                RpcId(peerId, nameof(SpawnPlayer), player.Key);
            }
        }
    }

    private void UnRegisterPlayer(int peerId)
    {
        if (!GetTree().IsNetworkServer())
            return;

        if (playerSpeciesList.Remove(peerId))
            Rpc(nameof(SyncPlayerSpeciesList), ThriveJsonConverter.Instance.SerializeObject(playerSpeciesList));

        foreach (var player in NetworkManager.Instance.PlayerList)
        {
            if (player.Value.Status == NetPlayerStatus.InGame)
                RpcId(player.Key, nameof(DeSpawnPlayer), peerId);
        }
    }

    [Puppet]
    private void SyncPlayerSpeciesList(string data)
    {
        playerSpeciesList = ThriveJsonConverter.Instance.DeserializeObject<Dictionary<int, Species>>(data)!;
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
