using System.Collections.Generic;
using Godot;
using Newtonsoft.Json;

[JsonObject(IsReference = true)]
[JSONAlwaysDynamicType]
[UseThriveSerializer]
public class MultiplayerGameWorld : GameWorld
{
    [JsonIgnore]
    private readonly Dictionary<uint, EntityReference<INetEntity>> entities = new();

    private uint entityIdCounter;

    public MultiplayerGameWorld(WorldGenerationSettings settings) : base(settings)
    {
    }

    [JsonConstructor]
    public MultiplayerGameWorld() : base()
    {
    }

    /// <summary>
    ///   Dictionary of players that has joined the game.
    /// </summary>
    [JsonIgnore]
    public Dictionary<int, NetPlayerState> Players { get; } = new();

    public IReadOnlyDictionary<uint, EntityReference<INetEntity>> Entities => entities;

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
    }

    public uint RegisterEntity(INetEntity entity)
    {
        RegisterEntity(++entityIdCounter, entity);
        return entityIdCounter;
    }

    public void UnregisterEntity(uint entityId)
    {
        entities.Remove(entityId);
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
