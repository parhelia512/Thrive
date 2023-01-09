using System.Collections.Generic;
using Godot;

/// <summary>
///   Represents a networkable version of the compound cloud with a predefined form and is deterministic.
/// </summary>
public class CloudBlob : Spatial, INetworkEntity, ISpawned, ITimedLife
{
    private CompoundCloudSystem? clouds;
    private string? cloudsPath;

    private List<Chunk> chunks = new();

    public Compound Compound { get; private set; } = null!;

    public IReadOnlyList<Chunk> Chunks => chunks;

    public int DespawnRadiusSquared { get; set; }

    public float EntityWeight => 1.0f;

    public AliveMarker AliveMarker => new();

    public Spatial EntityNode => this;

    public string ResourcePath => "res://src/microbe_stage/multiplayer/microbial_arena/CloudBlob.tscn";

    public uint NetworkEntityId { get; set; }

    public float TimeToLiveRemaining { get; set; }

    public void Init(CompoundCloudSystem clouds, Compound compound, Vector3 position, float radius, float amount)
    {
        this.clouds = clouds;
        Compound = compound;
        Translation = position;

        int resolution = Settings.Instance.CloudResolution;

        // Circle drawing algorithm from https://www.redblobgames.com/grids/circle-drawing/
        // TODO: make the shape more "noisy" instead of a perfect circle

        var center = new Int2((int)position.x, (int)position.z);

        var top = Mathf.CeilToInt(center.y - radius);
        var bottom = Mathf.FloorToInt(center.y + radius);
        var left = Mathf.CeilToInt(center.x - radius);
        var right = Mathf.FloorToInt(center.x + radius);

        for (int y = top; y <= bottom; ++y)
        {
            for (int x = left; x <= right; ++x)
            {
                var dx = center.x - x;
                var dy = center.y - y;
                var distanceSqr = dx * dx + dy * dy;

                if (distanceSqr <= radius * radius)
                    chunks.Add(new Chunk(new Vector3(x + resolution, 0, y + resolution), amount));
            }
        }
    }

    public override void _Ready()
    {
        // Kind of hackish I guess??
        clouds ??= GetNode<CompoundCloudSystem>(cloudsPath);
        cloudsPath ??= clouds.GetPath();

        foreach (var chunk in Chunks)
        {
            clouds.AddCloud(Compound, chunk.Amount, chunk.Position);
        }
    }

    public void NetworkSerialize(PackedBytesBuffer buffer)
    {
        // For now just hope everything sync nicely by themselves on the client side.
        // Can we even feasibly replicate the amount in the clouds anyway? ...not unless clever tricks were employed.
    }

    public void NetworkDeserialize(PackedBytesBuffer buffer)
    {
    }

    public void PackSpawnState(PackedBytesBuffer buffer)
    {
        buffer.Write((byte)SimulationParameters.Instance.CompoundToIndex(Compound));

        buffer.Write((short)Chunks.Count);
        foreach (var chunk in Chunks)
        {
            chunk.Amount = clouds!.AmountAvailable(Compound, chunk.Position, 1.0f);

            var packed = new PackedBytesBuffer();
            chunk.NetworkSerialize(packed);
            buffer.Write(packed);
        }

        buffer.Write(GlobalTranslation.x);
        buffer.Write(GlobalTranslation.y);
        buffer.Write(GlobalTranslation.z);
        buffer.Write(cloudsPath!);
    }

    public void OnRemoteSpawn(PackedBytesBuffer buffer, GameProperties currentGame)
    {
        Compound = SimulationParameters.Instance.IndexToCompound(buffer.ReadByte());

        var chunksCount = buffer.ReadInt16();
        for (int i = 0; i < chunksCount; ++i)
        {
            var packed = buffer.ReadBuffer();
            chunks.Add(new Chunk(packed));
        }

        Translation = new Vector3(buffer.ReadSingle(), buffer.ReadSingle(), buffer.ReadSingle());
        cloudsPath = buffer.ReadString();
    }

    public void OnTimeOver()
    {
        this.DestroyDetachAndQueueFree();
    }

    public void OnDestroyed()
    {
        AliveMarker.Alive = false;
    }

    private void OnTreeExiting()
    {
        foreach (var chunk in Chunks)
        {
            clouds?.TakeCompound(Compound, chunk.Position, 1.0f);
        }
    }

    public class Chunk : INetworkSerializable
    {
        public Vector3 Position;
        public float Amount;

        public Chunk(PackedBytesBuffer buffer)
        {
            NetworkDeserialize(buffer);
        }

        public Chunk(Vector3 position, float amount)
        {
            Position = position;
            Amount = amount;
        }

        public void NetworkSerialize(PackedBytesBuffer buffer)
        {
            buffer.Write(Position.x);
            buffer.Write(Position.z);
            buffer.Write(Amount);
        }

        public void NetworkDeserialize(PackedBytesBuffer buffer)
        {
            Position = new Vector3(buffer.ReadSingle(), Position.y, buffer.ReadSingle());
            Amount = buffer.ReadSingle();
        }
    }
}
