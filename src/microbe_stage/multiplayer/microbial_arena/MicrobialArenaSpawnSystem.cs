using System;
using Godot;

[JSONAlwaysDynamicType]
public class MicrobialArenaSpawnSystem : ISpawnSystem
{
    /// <summary>
    ///   Root node to parent all spawned things to
    /// </summary>
    private Node worldRoot;

    private BiomeConditions conditions;
    private float radius;
    private int maxEntities;

    private Random random = null!;

    public MicrobialArenaSpawnSystem(Node root, BiomeConditions conditions, float radius, int maxEntities,
        Random random)
    {
        this.conditions = conditions;
        this.radius = radius;
        this.maxEntities = maxEntities;
        this.random = random;
        worldRoot = root;
    }

    public void AddEntityToTrack(ISpawned entity)
    {
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
        PopulateWorld();
    }

    public bool IsUnderEntityLimitForReproducing()
    {
        throw new NotImplementedException();
    }

    public void Process(float delta, Vector3 playerPosition)
    {
        throw new NotImplementedException();
    }

    public void PopulateWorld()
    {
        for (int i = 0; i < maxEntities; i++)
        {
            var r = radius * Mathf.Sqrt(random.NextFloat());
            var angle = random.NextFloat() * 2 * Mathf.Pi;

            var x = r * Mathf.Cos(angle);
            var y = r * Mathf.Sin(angle);

            SpawnHelpers.SpawnChunk(
                conditions.Chunks.Random(random), new Vector3(x, 0, y), worldRoot, SpawnHelpers.LoadChunkScene(), random);
        }
    }
}
