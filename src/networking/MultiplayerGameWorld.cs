using System;
using System.Collections.Generic;

/// <summary>
///   <inheritdoc />. Contains the necessary informations for a multiplayer game mode.
/// </summary>
public class MultiplayerGameWorld : GameWorld
{
    private readonly Dictionary<uint, EntityReference<INetworkEntity>> entities = new();

    private readonly List<uint> entityIds = new();

    private uint entityIdCounter;

    public MultiplayerGameWorld(WorldGenerationSettings settings) : base(settings)
    {
    }

    public MultiplayerGameWorld(PatchMap map)
    {
        PlayerSpecies = CreatePlayerSpecies();

        if (!PlayerSpecies.PlayerSpecies)
            throw new Exception("PlayerSpecies flag for being player species is not set");

        Map = map;

        // Apply initial populations
        Map.UpdateGlobalPopulations();
    }

    /// <summary>
    ///   Stores variables of registered players in relation to the current game world.
    /// </summary>
    public Dictionary<int, NetworkPlayerVars> PlayerVars { get; set; } = new();

    /// <summary>
    ///   Stores references to all networked entities.
    /// </summary>
    public IReadOnlyDictionary<uint, EntityReference<INetworkEntity>> Entities => entities;

    public IReadOnlyList<uint> EntityIDs => entityIds;

    public int EntityCount => entities.Count;

    public void ClearMultiplayer()
    {
        PlayerVars.Clear();
        entities.Clear();
    }

    public void RegisterNetworkEntity(uint id, INetworkEntity entity)
    {
        entity.NetworkEntityId = id;
        entities[id] = new EntityReference<INetworkEntity>(entity);

        if (!entityIds.Contains(id))
            entityIds.Add(id);
    }

    /// <summary>
    ///   Registers the given entity to the game world.
    /// </summary>
    /// <returns>The entity's assigned ID.</returns>
    public uint RegisterNetworkEntity(INetworkEntity entity)
    {
        RegisterNetworkEntity(++entityIdCounter, entity);
        return entityIdCounter;
    }

    public void UnregisterNetworkEntity(uint id)
    {
        entities.Remove(id);
        entityIds.Remove(id);
    }

    public bool TryGetNetworkEntity(uint id, out INetworkEntity entity)
    {
        if (entities.TryGetValue(id, out EntityReference<INetworkEntity> entityReference) &&
            entityReference.Value != null)
        {
            entity = entityReference.Value;
            return true;
        }

        entity = null!;
        return false;
    }

    public void UpdateSpecies(uint peerId, Species species)
    {
        worldSpecies[peerId] = species;
    }

    public void UpdateSpecies(int peerId, Species species)
    {
        UpdateSpecies((uint)peerId, species);
    }
}
