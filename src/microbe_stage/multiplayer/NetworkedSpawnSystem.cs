using System;
using Godot;
using Newtonsoft.Json;

[JsonObject(IsReference = true)]
public class NetworkedSpawnSystem : ISpawnSystem
{
    /// <summary>
    ///   Root node to parent all spawned things to
    /// </summary>
    private Node worldRoot;

    public NetworkedSpawnSystem(Node root)
    {
        worldRoot = root;
    }

    public void AddEntityToTrack(ISpawned entity)
    {
        throw new NotImplementedException();
    }

    public void Clear()
    {
        throw new NotImplementedException();
    }

    public void DespawnAll()
    {
        throw new NotImplementedException();
    }

    public void Init()
    {
        // TODO: setup spawn points, pregenerate world entities
    }

    public bool IsUnderEntityLimitForReproducing()
    {
        throw new NotImplementedException();
    }

    public void Process(float delta, Vector3 playerPosition)
    {
        throw new NotImplementedException();
    }
}
