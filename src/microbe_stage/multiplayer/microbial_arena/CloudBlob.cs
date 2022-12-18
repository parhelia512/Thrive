using System.Collections.Generic;
using System.Linq;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   A static predefined compound cloud with a circular form. Network synchronizable.
/// </summary>
public class CloudBlob : Spatial, INetEntity, ISpawned
{
    private List<Cell> content = new();

    public Compound Compound { get; private set; } = null!;

    public IReadOnlyList<Cell> Content => content;

    /// <summary>
    ///   Returns true if the sum of compound amount in all cloud cells is less than <see cref="MathUtils.EPSILON"/>.
    /// </summary>
    public bool Empty => content.Sum(c => c.Amount) <= MathUtils.EPSILON;

    public int DespawnRadiusSquared { get; set; }

    public float EntityWeight => 1.0f;

    public AliveMarker AliveMarker => new();

    public Spatial EntityNode => this;

    public string ResourcePath => "res://src/microbe_stage/multiplayer/microbial_arena/CloudBlob.tscn";

    public uint NetEntityId { get; set; }

    public bool Synchronize { get; set; } = false;

    public void Init(Compound compound, Vector3 position, float radius, float amount)
    {
        Compound = compound;
        Translation = position;

        int resolution = Settings.Instance.CloudResolution;

        // Circle drawing algorithm from https://www.redblobgames.com/grids/circle-drawing/

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
                    content.Add(new Cell(new Vector2(x + resolution, y + resolution), amount));
            }
        }
    }

    public void NetworkTick(float delta)
    {
    }

    public void OnDestroyed()
    {
        AliveMarker.Alive = false;
    }

    public void OnNetworkSync(Dictionary<string, string> data)
    {
        data.TryGetValue(nameof(GlobalTranslation), out string serializedCenter);
        GlobalTranslation = (Vector3)GD.Str2Var(serializedCenter);
    }

    public void OnReplicated(Dictionary<string, string> data, GameProperties currentGame)
    {
        data.TryGetValue(nameof(Compound), out string compoundInternalName);
        data.TryGetValue(nameof(Content), out string serializedContent);

        Compound = SimulationParameters.Instance.GetCompound(compoundInternalName);
        content = JsonConvert.DeserializeObject<List<Cell>>(serializedContent)!;
    }

    public Dictionary<string, string> PackReplicableVars()
    {
        var vars = new Dictionary<string, string>
        {
            { nameof(Compound), Compound.InternalName },
            { nameof(Content), JsonConvert.SerializeObject(content) },
        };

        return vars;
    }

    public Dictionary<string, string> PackStates()
    {
        var states = new Dictionary<string, string>
        {
            { nameof(GlobalTranslation), GD.Var2Str(GlobalTranslation) },
        };

        return states;
    }

    public class Cell
    {
        public Vector2 Position;
        public float Amount;

        public Cell(Vector2 position, float amount)
        {
            Position = position;
            Amount = amount;
        }
    }
}
