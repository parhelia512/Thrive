using System.Collections.Generic;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   Base stage for the stages where the player controls a single creature
/// </summary>
/// <typeparam name="TPlayer">The type of the player object</typeparam>
public abstract class MultiplayerStageBase<TPlayer> : StageBase<TPlayer>
    where TPlayer : class, IEntity
{
    private float networkTick;

    private readonly Dictionary<int, EntityReference<TPlayer>> players = new();
    private readonly Dictionary<int, float> respawnTimers = new();

    public IReadOnlyDictionary<int, EntityReference<TPlayer>> Players => players;

    public override void _Ready()
    {
        base._Ready();

        GetTree().Connect("network_peer_disconnected", this, nameof(OnPeerDisconnected));
        GetTree().Connect("server_disconnected", this, nameof(OnServerDisconnected));

        NetworkManager.Instance.Connect(nameof(NetworkManager.PlayerJoined), this, nameof(SpawnPlayer));
        NetworkManager.Instance.Connect(nameof(NetworkManager.PlayerLeft), this, nameof(DeSpawnPlayer));
        NetworkManager.Instance.Connect(nameof(NetworkManager.Kicked), this, nameof(OnKicked));
    }

    public override void _Process(float delta)
    {
        networkTick += delta;

        if (!GetTree().HasNetworkPeer() || !IsNetworkMaster())
            return;

        if (NetworkManager.Instance.GameInSession && networkTick > NetworkManager.Instance.TickRateDelay)
        {
            NetworkUpdateGameState(delta);
            NetworkHandleRespawns(delta);
            networkTick = 0;
        }
    }

    public override void OnFinishLoading(Save save)
    {
    }

    protected override void SetupStage()
    {
        base.SetupStage();

        // Spawn already existing players in the game
        foreach (var player in NetworkManager.Instance.PlayerList)
        {
            if (player.Value.Status == NetPlayerStatus.InGame)
                SpawnPlayer(player.Key);
        }
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

            var diff = respawnTimers[player.Key] - (delta + NetworkManager.Instance.TickRateDelay);
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
            OnPlayerSpawn(peerId, out TPlayer spawned);
            players.Add(peerId, new EntityReference<TPlayer>(spawned));
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
                OnPlayerDeSpawn(peer.Value);

            players.Remove(peerId);
        }
    }

    protected abstract void OnPlayerSpawn(int peerId, out TPlayer spawned);
    protected abstract void OnPlayerDeSpawn(TPlayer removed);

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

    private void OnPeerDisconnected(int peerId)
    {
        DeSpawnPlayer(peerId);
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
