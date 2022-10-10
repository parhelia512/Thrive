/// <summary>
///   The networking part of MicrobeStage for multiplayer.
/// </summary>
public partial class MicrobeStage : StageBase<Microbe>
{
    // WIP

    /*
    protected override void NetworkUpdateGameState()
    {
        foreach (var peer in peers)
        {
            if (IsNetworkMaster())
            {
                peer.Value.Value?.Sync();
            }
            else
            {
                peer.Value.Value?.Send();
            }
        }

        if (peers.Any(p => p.Value.Value?.Dead == true))
        {
            peers = peers
                .Where(p => p.Value.Value?.Dead == false)
                .ToDictionary(p => p.Key, p => p.Value);
        }
    }

    protected override void OnPeerSpawnRequested(int peerId)
    {
        var microbe = (Microbe)SpawnHelpers.SpawnMicrobe(GameWorld.PlayerSpecies, new Vector3(0, 0, 0),
            rootOfDynamicallySpawned, SpawnHelpers.LoadMicrobeScene(), false, Clouds, spawner, CurrentGame!);
        microbe.Name = peerId.ToString(CultureInfo.CurrentCulture);
        microbe.SetupPlayerClient(peerId);
        peers.Add(peerId, new EntityReference<Microbe>(microbe));
    }

    protected abstract void OnPeerDespawn(TPlayer peerObject)
    {
        peerObject.DestroyDetachAndQueueFree();
    }
    */
}
