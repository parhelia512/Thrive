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

    /// <summary>
    ///   If set, will assume <see cref="SceneTree.HasNetworkPeer"/> is true and subsequently do network
    ///   operations with the given value.
    /// </summary>
    [JsonProperty]
    public int? PeerId { get; private set; }

    public uint NetEntityId { get; set; }

    public bool Synchronize { get; set; } = true;

    public Action<int>? OnNetworkedDeathCompletes { get; set; }

    public void OnNetworkSync(Dictionary<string, string> data)
    {
        // TODO: these badly needs optimizing

        if (int.TryParse(data["randomSeed"], out int parsedRandomSeed) && parsedRandomSeed != randomSeed)
        {
            randomSeed = parsedRandomSeed;
            random = new Random(parsedRandomSeed);
        }

        var rotation = (Vector3)GD.Str2Var(data["rot"]);
        var position = (Vector3)GD.Str2Var(data["pos"]);

        Rotation = rotation;
        networkTweener?.InterpolateProperty(this, "global_translation", null, position, 0.1f);
        networkTweener?.Start();

        Compounds.ClearUseful();
        foreach (var useful in JsonConvert.DeserializeObject<List<string>>(data["usefulCompounds"])!)
        {
            Compounds.SetUseful(SimulationParameters.Instance.GetCompound(useful));
        }

        if (float.TryParse(data["compoundsCap"], out float parsedCompoundsCap))
            Compounds.Capacity = parsedCompoundsCap;

        foreach (var entry in JsonConvert.DeserializeObject<Dictionary<string, float>>(data["compounds"])!)
        {
            var compound = SimulationParameters.Instance.GetCompound(entry.Key);
            Compounds.Compounds[compound] = entry.Value;
        }

        if (float.TryParse(data["health"], out float parsedHealth))
            Hitpoints = parsedHealth;

        if (Enum.TryParse<MicrobeState>(data["microbeState"], out MicrobeState parsedMicrobeState))
            State = parsedMicrobeState;

        if (Enum.TryParse<PhagocytosisPhase>(data["engulfStep"], out PhagocytosisPhase parsedEngulfStep))
            phagocytosisStep = parsedEngulfStep;

        if (!string.IsNullOrEmpty(data["engulfer"]))
        {
            var engulfer = GetNode<Microbe>(data["engulfer"]);

            if (PhagocytosisStep == PhagocytosisPhase.None)
                engulfer.IngestEngulfable(this);
        }
        else
        {
            if (HostileEngulfer.Value != null)
                HostileEngulfer.Value.EjectEngulfable(this);
        }

        Membrane.Tint = (Color)GD.Str2Var(data["membraneTint"]);

        if (float.TryParse(data["digestedAmount"], out float parsedDigestedAmount))
            DigestedAmount = parsedDigestedAmount;
    }

    public void OnNetworkInput(Dictionary<string, string> data)
    {
        MovementDirection = (Vector3)GD.Str2Var(data["moveDirection"]);
        LookAtPoint = (Vector3)GD.Str2Var(data["lookAtPoint"]);

        if (Enum.TryParse<MicrobeState>(data["microbeState"], out MicrobeState parsedMicrobeState))
            State = parsedMicrobeState;
    }

    public Dictionary<string, string>? PackStates()
    {
        var vars = new Dictionary<string, string>
        {
            { "randomSeed", randomSeed.ToString(CultureInfo.CurrentCulture) },
            { "pos", GD.Var2Str(GlobalTranslation) },
            { "rot", GD.Var2Str(GlobalRotation) },
            { "usefulCompounds", JsonConvert.SerializeObject(Compounds.UsefulCompounds.Select(c => c.InternalName).ToList()) },
            { "compoundsCap", Compounds.Capacity.ToString() },
            { "compounds", JsonConvert.SerializeObject(Compounds.Compounds.ToDictionary(c => c.Key.InternalName, c => c.Value)) },
            { "health", Hitpoints.ToString(CultureInfo.CurrentCulture) },
            { "microbeState", State.ToString() },
            { "engulfStep", PhagocytosisStep.ToString() },
            { "engulfer", HostileEngulfer.Value != null ? HostileEngulfer.Value.GetPath() : string.Empty },
            { "membraneTint", GD.Var2Str(Membrane.Tint) },
            { "digestedAmount", DigestedAmount.ToString(CultureInfo.CurrentCulture) }
        };

        // TODO: Death sync broken, so does engulfing

        return vars;
    }

    public Dictionary<string, string>? PackInputs()
    {
        var vars = new Dictionary<string, string>
        {
            { "moveDirection", GD.Var2Str(MovementDirection) },
            { "lookAtPoint", GD.Var2Str(LookAtPoint) },
            { "microbeState", State.ToString() },
        };

        return vars;
    }

    public void OnReplicated()
    {
        SetupNetworking();
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

        var tagBox = GetNode<MeshInstance>("TagBox");

        var tagBoxMesh = (QuadMesh)tagBox.Mesh;
        var tagBoxMaterial = (SpatialMaterial)tagBox.MaterialOverride;

        var tag = tagBox.GetChild<Label3D>(0);

        tagBox.Visible = true;
        tag.Text = NetworkManager.Instance.PlayerList[PeerId.Value].Name + ", Dead: " + Dead;

        tagBoxMesh.Size = tag.Font.GetStringSize(tag.Text) * tag.PixelSize * 1.2f;
        tagBoxMaterial.RenderPriority = RenderPriority + 1;
        tag.RenderPriority = tagBoxMaterial.RenderPriority + 1;

        // TODO: offset tag above the membrane (Z-axis)
    }
}
