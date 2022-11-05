using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   The networking part of Microbe class for state synchronizations.
/// </summary>
public partial class Microbe
{
    [JsonProperty]
    private Tween? networkTweener;

    private MeshInstance? tagBox;

    /// <summary>
    ///   If set, will assume <see cref="SceneTree.HasNetworkPeer"/> is true and subsequently do network
    ///   operations with the given value.
    /// </summary>
    [JsonProperty]
    public int? PeerId { get; private set; }

    public string ResourcePath => "res://src/microbe_stage/Microbe.tscn";

    public uint NetEntityId { get; set; }

    public bool Synchronize { get; set; } = true;

    public Action<int>? OnNetworkedDeathCompletes { get; set; }

    public Action<int, int, string>? OnKilledByPeer { get; set; }

    public void NetworkTick(float delta)
    {
        if (tagBox != null)
            tagBox.Visible = PhagocytosisStep == PhagocytosisPhase.None;
    }

    public void OnNetworkSync(Dictionary<string, string> data)
    {
        // TODO: these badly needs optimizing

        if (int.TryParse(data[nameof(randomSeed)], out int parsedRandomSeed) && parsedRandomSeed != randomSeed)
        {
            randomSeed = parsedRandomSeed;
            random = new Random(parsedRandomSeed);
        }

        var rotation = (Vector3)GD.Str2Var(data[nameof(GlobalRotation)]);
        var position = (Vector3)GD.Str2Var(data[nameof(GlobalTranslation)]);

        var parsedUsefulCompounds = JsonConvert.DeserializeObject<List<string>>(
            data[nameof(Compounds.UsefulCompounds)]);

        var parsedCompounds = JsonConvert.DeserializeObject<Dictionary<string, float>>(
            data[nameof(Compounds.Compounds)]);

        Rotation = rotation;
        networkTweener?.InterpolateProperty(this, "global_translation", null, position, 0.1f);
        networkTweener?.Start();

        Compounds.ClearUseful();
        foreach (var useful in parsedUsefulCompounds!)
        {
            Compounds.SetUseful(SimulationParameters.Instance.GetCompound(useful));
        }

        if (float.TryParse(data[nameof(Compounds.Capacity)], out float parsedCompoundsCap))
        {
            Compounds.Capacity = parsedCompoundsCap;
        }

        foreach (var entry in parsedCompounds!)
        {
            var compound = SimulationParameters.Instance.GetCompound(entry.Key);
            Compounds.Compounds[compound] = entry.Value;
        }

        if (Enum.TryParse(data[nameof(State)], out MicrobeState parsedMicrobeState))
            State = parsedMicrobeState;

        if (Enum.TryParse(data[nameof(PhagocytosisStep)], out PhagocytosisPhase parsedEngulfStep))
        {
            if (data.TryGetValue(nameof(HostileEngulfer), out string engulferPath))
            {
                var engulfer = GetNode<Microbe>(engulferPath);

                switch (parsedEngulfStep)
                {
                    case PhagocytosisPhase.Ingestion:
                        engulfer.IngestEngulfable(this);
                        break;
                    case PhagocytosisPhase.Exocytosis:
                        engulfer.EjectEngulfable(this);
                        break;
                }
            }
            else
            {
                HostileEngulfer.Value?.EjectEngulfable(this);
            }

            PhagocytosisStep = parsedEngulfStep;
        }

        if (bool.TryParse(data[nameof(Dead)], out bool parsedDead) && parsedDead)
            Kill();

        if (float.TryParse(data[nameof(Hitpoints)], out float parsedHealth))
            Hitpoints = parsedHealth;

        if (float.TryParse(data[nameof(DigestedAmount)], out float parsedDigestedAmount))
            DigestedAmount = parsedDigestedAmount;

        Membrane.Tint = (Color)GD.Str2Var(data[nameof(Membrane.Tint)]);
    }

    public void OnNetworkInput(Dictionary<string, string> data)
    {
        MovementDirection = (Vector3)GD.Str2Var(data[nameof(MovementDirection)]);
        LookAtPoint = (Vector3)GD.Str2Var(data[nameof(LookAtPoint)]);

        data.TryGetValue(nameof(WantsToEngulf), out string wantsToEngulf);
        data.TryGetValue(nameof(queuedSlimeSecretionTime), out string queuedSlimeSecretionTimeInput);
        data.TryGetValue(nameof(queuedToxinToEmit), out string queuedToxinToEmitInput);

        if (bool.TryParse(wantsToEngulf, out bool parsedEngulf))
            WantsToEngulf = parsedEngulf;

        if (float.TryParse(queuedSlimeSecretionTimeInput, out float parsedQueuedSlimeSecretionTime))
            queuedSlimeSecretionTime = parsedQueuedSlimeSecretionTime;

        queuedToxinToEmit = !string.IsNullOrEmpty(queuedToxinToEmitInput) ?
            SimulationParameters.Instance.GetCompound(queuedToxinToEmitInput) : null;
    }

    public Dictionary<string, string>? PackStates()
    {
        var states = new Dictionary<string, string>
        {
            { nameof(randomSeed), randomSeed.ToString(CultureInfo.CurrentCulture) },
            { nameof(GlobalTranslation), GD.Var2Str(GlobalTranslation) },
            { nameof(GlobalRotation), GD.Var2Str(GlobalRotation) },
            {
                nameof(Compounds.UsefulCompounds), JsonConvert.SerializeObject(
                    Compounds.UsefulCompounds.Select(c => c.InternalName).ToList())
            },
            { nameof(Compounds.Capacity), Compounds.Capacity.ToString() },
            {
                nameof(Compounds.Compounds), JsonConvert.SerializeObject(
                    Compounds.Compounds.ToDictionary(c => c.Key.InternalName, c => c.Value))
            },
            { nameof(Hitpoints), Hitpoints.ToString(CultureInfo.CurrentCulture) },
            { nameof(Dead), Dead.ToString(CultureInfo.CurrentCulture) },
            { nameof(State), State.ToString() },
            { nameof(PhagocytosisStep), PhagocytosisStep.ToString() },
            { nameof(Membrane.Tint), GD.Var2Str(Membrane.Tint) },
            { nameof(DigestedAmount), DigestedAmount.ToString(CultureInfo.CurrentCulture) },
        };

        if (HostileEngulfer.Value != null)
            states.Add(nameof(HostileEngulfer), HostileEngulfer.Value.GetPath());

        return states;
    }

    public Dictionary<string, string>? PackReplicableVars()
    {
        var vars = new Dictionary<string, string>
        {
            { nameof(PeerId), PeerId.ToString() },
            { nameof(CurrentGame), ThriveJsonConverter.Instance.SerializeObject(CurrentGame) },
            { nameof(Species), ThriveJsonConverter.Instance.SerializeObject(Species) },
        };

        return vars;
    }

    public Dictionary<string, string>? PackInputs()
    {
        var vars = new Dictionary<string, string>
        {
            { nameof(MovementDirection), GD.Var2Str(MovementDirection) },
            { nameof(LookAtPoint), GD.Var2Str(LookAtPoint) },
            { nameof(WantsToEngulf), WantsToEngulf.ToString(CultureInfo.CurrentCulture) },
            { nameof(queuedSlimeSecretionTime), queuedSlimeSecretionTime.ToString(CultureInfo.CurrentCulture) },
        };

        vars.Add(nameof(queuedToxinToEmit), queuedToxinToEmit?.InternalName ?? string.Empty);

        return vars;
    }

    public void OnReplicated(Dictionary<string, string>? data)
    {
        if (data == null)
            return;

        var currentGame = ThriveJsonConverter.Instance.DeserializeObject<GameProperties>(data[nameof(CurrentGame)]);
        var species = ThriveJsonConverter.Instance.DeserializeObject<MicrobeSpecies>(data[nameof(Species)]);

        if (currentGame != null && int.TryParse(data[nameof(PeerId)], out int peerId))
            Init(null!, null!, currentGame, true, peerId);

        if (species != null)
            ApplySpecies(species);
    }

    private void SetupNetworking()
    {
        if (!PeerId.HasValue)
            return;

        Name = PeerId.Value.ToString(CultureInfo.CurrentCulture);

        networkTweener?.DetachAndQueueFree();
        networkTweener = new Tween();
        AddChild(networkTweener);

        if (PeerId.Value != NetworkManager.Instance.PeerId!.Value)
            UpdateNameTag();
    }

    private void UpdateNameTag()
    {
        if (!PeerId.HasValue)
            return;

        tagBox = GetNode<MeshInstance>("TagBox");

        var tagBoxMesh = (QuadMesh)tagBox.Mesh;
        var tagBoxMaterial = (SpatialMaterial)tagBox.MaterialOverride;

        var tag = tagBox.GetChild<Label3D>(0);

        tagBox.Visible = true;
        tag.Text = NetworkManager.Instance.PlayerList[PeerId.Value].Name;

        tagBoxMesh.Size = tag.Font.GetStringSize(tag.Text) * tag.PixelSize * 1.2f;
        tagBoxMaterial.RenderPriority = RenderPriority + 1;
        tag.RenderPriority = tagBoxMaterial.RenderPriority + 1;

        // TODO: offset tag above the membrane (Z-axis)
    }
}
