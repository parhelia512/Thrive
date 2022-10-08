using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

/// <summary>
///   The networking part of MicrobeStage for multiplayer.
/// </summary>
public partial class MicrobeStage : StageBase<Microbe>
{
    private Dictionary<int, Microbe> peers = new();

    private float networkTick;

    private void SetupNetworking()
    {
        GetTree().Connect("network_peer_disconnected", this, nameof(OnPeerDisconnected));
        GetTree().Connect("server_disconnected", this, nameof(OnServerDisconnected));

        NetworkManager.Instance.Connect(nameof(NetworkManager.SpawnRequested), this, nameof(SpawnPeer));
        NetworkManager.Instance.Connect(nameof(NetworkManager.DespawnRequested), this, nameof(RemovePeer));
    }

    private void HandleNetworking(float delta)
    {
        networkTick += delta;

        // Send network updates at 30 FPS
        if (networkTick > NetworkManager.Instance.TickRateDelay)
        {
            foreach (var peer in peers)
            {
                if (IsNetworkMaster())
                {
                    peer.Value.Sync();
                }
                else
                {
                    peer.Value.Send();
                }
            }

            if (peers.Any(p => p.Value.Dead))
            {
                peers = peers
                    .Where(p => !p.Value.Dead)
                    .ToDictionary(p => p.Key, p => p.Value);
            }

            networkTick = 0;
        }
    }

    [Remote]
    private void SpawnPeer(int peerId)
    {
        if (peers.ContainsKey(peerId))
            return;

        var microbe = (Microbe)SpawnHelpers.SpawnMicrobe(GameWorld.PlayerSpecies, new Vector3(0, 0, 0),
            rootOfDynamicallySpawned, SpawnHelpers.LoadMicrobeScene(), false, Clouds, spawner, CurrentGame!);
        microbe.Name = peerId.ToString(CultureInfo.CurrentCulture);
        microbe.SetupPlayerClient(peerId);
        peers.Add(peerId, microbe);
    }

    private void RemovePeer(int peerId)
    {
        if (peers.TryGetValue(peerId, out Microbe peer))
        {
            peer.DestroyDetachAndQueueFree();
            peers.Remove(peerId);
        }
    }

    private void OnPeerDisconnected(int peerId)
    {
        RemovePeer(peerId);
    }

    private void OnServerDisconnected()
    {
        SceneManager.Instance.ReturnToMenu();
    }
}
