using System.Collections.Generic;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   Base stage for the stages where the player controls a single creature
/// </summary>
/// <typeparam name="TPlayer">The type of the player object</typeparam>
[JsonObject(IsReference = true)]
[UseThriveSerializer]
public abstract class MultiplayerStageBase<TPlayer> : StageBase<TPlayer>
    where TPlayer : class
{
    private float networkTick;

    private readonly Dictionary<int, TPlayer> peers = new();

    public IReadOnlyDictionary<int, TPlayer> Peers => peers;

    public override void _Ready()
    {
        base._Ready();

        GetTree().Connect("network_peer_disconnected", this, nameof(OnPeerDisconnected));
        GetTree().Connect("server_disconnected", this, nameof(OnServerDisconnected));

        NetworkManager.Instance.Connect(nameof(NetworkManager.SpawnRequested), this, nameof(SpawnPeer));
        NetworkManager.Instance.Connect(nameof(NetworkManager.DespawnRequested), this, nameof(DespawnPeer));
        NetworkManager.Instance.Connect(nameof(NetworkManager.Kicked), this, nameof(OnKicked));
    }

    public override void _Process(float delta)
    {
        networkTick += delta;

        // Send network updates at 30 FPS
        if (NetworkManager.Instance.GameInSession && networkTick > NetworkManager.Instance.TickRateDelay)
        {
            NetworkUpdateGameState();
            networkTick = 0;
        }
    }

    public override void OnFinishLoading(Save save)
    {
    }

    protected override void SetupStage()
    {
        base.SetupStage();

        // Spawn already existing peers in the game.
        foreach (var peer in NetworkManager.Instance.PlayerList)
        {
            if (peer.Value.CurrentEnvironment == PlayerState.Environment.InGame)
                SpawnPeer(peer.Key);
        }
    }

    protected abstract void NetworkUpdateGameState();

    [Remote]
    protected void SpawnPeer(int peerId)
    {
        if (!peers.ContainsKey(peerId))
        {
            OnPeerSpawn(peerId, out TPlayer spawned);
            peers.Add(peerId, spawned);
        }
    }

    [Remote]
    protected void DespawnPeer(int peerId)
    {
        if (peers.TryGetValue(peerId, out TPlayer peer))
        {
            OnPeerDespawn(peer);
            peers.Remove(peerId);
        }
    }

    protected abstract void OnPeerSpawn(int peerId, out TPlayer spawned);
    protected abstract void OnPeerDespawn(TPlayer removed);

    protected override void AutoSave()
    {
    }

    protected override void PerformQuickSave()
    {
    }

    private void OnPeerDisconnected(int peerId)
    {
        DespawnPeer(peerId);
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
