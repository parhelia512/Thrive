using System;
using System.Collections.Generic;

public class MultiplayerGameWorld : GameWorld
{
    private readonly Dictionary<uint, EntityReference<INetEntity>> entities = new();

    private readonly List<uint> entityIds = new();

    private uint entityIdCounter;

    public MultiplayerGameWorld(WorldGenerationSettings settings) : base(settings)
    {
    }

    public MultiplayerGameWorld(PatchMap map) : base()
    {
        PlayerSpecies = CreatePlayerSpecies();

        if (!PlayerSpecies.PlayerSpecies)
            throw new Exception("PlayerSpecies flag for being player species is not set");

        Map = map;

        // Apply initial populations
        Map.UpdateGlobalPopulations();
    }

    public MultiplayerGameWorld() : base()
    {
    }

    /// <summary>
    ///   Dictionary of players that has joined the game.
    /// </summary>
    public Dictionary<int, NetPlayerState> Players { get; } = new();

    public IReadOnlyDictionary<uint, EntityReference<INetEntity>> Entities => entities;

    public IReadOnlyList<uint> EntityIDs => entityIds;

    public int EntityCount => entities.Count;

    public void Clear()
    {
        Players.Clear();
        entities.Clear();
    }

    public void RegisterEntity(uint id, INetEntity entity)
    {
        entity.NetEntityId = id;
        entities[id] = new EntityReference<INetEntity>(entity);

        if (!entityIds.Contains(id))
            entityIds.Add(id);
    }

    public uint RegisterEntity(INetEntity entity)
    {
        RegisterEntity(++entityIdCounter, entity);
        return entityIdCounter;
    }

    public void UnregisterEntity(uint id)
    {
        entities.Remove(id);
        entityIds.Remove(id);
    }

    public INetEntity? GetEntity(uint id)
    {
        entities.TryGetValue(id, out EntityReference<INetEntity> entity);
        return entity?.Value;
    }

    public void UpdateSpecies(uint id, Species species)
    {
        worldSpecies[id] = species;
    }
}
