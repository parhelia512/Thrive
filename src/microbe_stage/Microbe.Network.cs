using System;
using System.Globalization;
using System.Linq;
using Godot;

/// <summary>
///   The networking part of Microbe class for state synchronizations.
/// </summary>
public partial class Microbe
{
    private MeshInstance? tagBox;

    private float lastHitpoints;

    private string? cloudSystemPath;

    public MultiplayerGameWorld? MultiplayerGameWorld => GameWorld as MultiplayerGameWorld;

    public override string ResourcePath => "res://src/microbe_stage/Microbe.tscn";

    public Action<int>? OnNetworkDeathFinished { get; set; }

    public Action<int, int, string>? OnKilledByAnotherPlayer { get; set; }

    public override void SetupNetworkCharacter()
    {
        if (!IsInsideTree())
            return;

        base.SetupNetworkCharacter();

        Name = PeerId.ToString(CultureInfo.CurrentCulture);

        if (PeerId != NetworkManager.Instance.PeerId)
            InitNameTag();

        Compounds.LockInputAndOutput = NetworkManager.Instance.IsClient;

        // Kind of hackish I guess??
        if (cloudSystemPath != null)
            cloudSystem ??= GetNode<CompoundCloudSystem>(cloudSystemPath);
    }

    public override void NetworkSerialize(PackedBytesBuffer buffer)
    {
        // TODO: Find a way to compress this further, look into delta encoding

        base.NetworkSerialize(buffer);

        buffer.Write((byte)Compounds.UsefulCompounds.Count());
        foreach (var compound in Compounds.UsefulCompounds)
            buffer.Write((byte)SimulationParameters.Instance.CompoundToIndex(compound));

        buffer.Write(Compounds.Capacity);

        buffer.Write((byte)Compounds.Compounds.Count);
        foreach (var compound in compounds.Compounds)
        {
            buffer.Write((byte)SimulationParameters.Instance.CompoundToIndex(compound.Key));
            buffer.Write(compound.Value);
        }

        requiredCompoundsForBaseReproduction.TryGetValue(ammonia, out float ammoniaAmount);
        requiredCompoundsForBaseReproduction.TryGetValue(phosphates, out float phosphatesAmount);
        buffer.Write(ammoniaAmount);
        buffer.Write(phosphatesAmount);

        buffer.Write(Hitpoints);
        buffer.Write((byte)State);
        buffer.Write((byte)PhagocytosisStep);
        buffer.Write(DigestedAmount);

        var bools = new bool[2] { HostileEngulfer.Value != null, Dead };
        buffer.Write(bools.ToByte());

        if (HostileEngulfer.Value != null)
            buffer.Write(HostileEngulfer.Value.NetworkEntityId);
    }

    public override void NetworkDeserialize(PackedBytesBuffer buffer)
    {
        base.NetworkDeserialize(buffer);

        Compounds.ClearUseful();
        var usefulCompoundsCount = buffer.ReadByte();
        for (int i = 0; i < usefulCompoundsCount; ++i)
        {
            Compounds.SetUseful(SimulationParameters.Instance.IndexToCompound(buffer.ReadByte()));
        }

        Compounds.Capacity = buffer.ReadSingle();

        var compoundsCount = buffer.ReadByte();
        for (int i = 0; i < compoundsCount; ++i)
        {
            var compound = SimulationParameters.Instance.IndexToCompound(buffer.ReadByte());
            Compounds.Compounds[compound] = buffer.ReadSingle();
        }

        requiredCompoundsForBaseReproduction[ammonia] = buffer.ReadSingle();
        requiredCompoundsForBaseReproduction[phosphates] = buffer.ReadSingle();

        Hitpoints = buffer.ReadSingle();
        State = (MicrobeState)buffer.ReadByte();
        PhagocytosisStep = (PhagocytosisPhase)buffer.ReadByte();
        DigestedAmount = buffer.ReadSingle();

        var bools = buffer.ReadByte();

        if (bools.ToBoolean(0) && MultiplayerGameWorld!.TryGetNetworkEntity(
                buffer.ReadUInt32(), out INetworkEntity entity) && entity is Microbe engulfer)
        {
            // TODO: Very broken
            engulfer.IngestEngulfable(this);
        }
        else
        {
            HostileEngulfer.Value?.EjectEngulfable(this);
        }

        // TODO: Dead won't sync properly again -_-
        Dead = bools.ToBoolean(1);

        if (Hitpoints < lastHitpoints)
            Flash(1.0f, new Color(1, 0, 0, 0.5f), 1);

        lastHitpoints = Hitpoints;

        if (tagBox != null)
            tagBox.Visible = !Dead;
    }

    public override void PackSpawnState(PackedBytesBuffer buffer)
    {
        base.PackSpawnState(buffer);

        buffer.Write(randomSeed);
        buffer.Write(cloudSystem!.GetPath());

        // Sending 2-byte unsigned int... means our max deserialized organelle count will be 65535
        buffer.Write((ushort)organelles!.Count);
        foreach (var organelle in organelles!)
        {
            var packed = new PackedBytesBuffer();
            organelle.NetworkSerialize(packed);
            buffer.Write(packed);
        }

        buffer.Write(allOrganellesDivided);
    }

    public override void OnRemoteSpawn(PackedBytesBuffer buffer, GameProperties currentGame)
    {
        base.OnRemoteSpawn(buffer, currentGame);

        randomSeed = buffer.ReadInt32();
        cloudSystemPath = buffer.ReadString();

        AddToGroup(Constants.AI_TAG_MICROBE);
        AddToGroup(Constants.PROCESS_GROUP);
        AddToGroup(Constants.RUNNABLE_MICROBE_GROUP);

        Init(null!, null!, currentGame, true);

        var world = (MultiplayerGameWorld)currentGame.GameWorld;
        ApplySpecies(world.GetSpecies((uint)PeerId));

        organelles?.Clear();
        var organellesCount = buffer.ReadUInt16();
        for (int i = 0; i < organellesCount; ++i)
        {
            if (organelles == null)
                break;

            var packed = buffer.ReadBuffer();
            var organelle = new PlacedOrganelle();
            organelle.NetworkDeserialize(packed);
            organelles.Add(organelle);
        }

        allOrganellesDivided = buffer.ReadBoolean();
    }

    private void InitNameTag()
    {
        if (!NetworkManager.Instance.IsNetworked)
            return;

        tagBox = GetNode<MeshInstance>("TagBox");

        var tagBoxMesh = (QuadMesh)tagBox.Mesh;
        var tagBoxMaterial = (SpatialMaterial)tagBox.MaterialOverride;

        var tag = tagBox.GetChild<Label3D>(0);

        tagBox.Visible = true;
        tag.Text = NetworkManager.Instance.ConnectedPlayers[PeerId].Name;

        tagBoxMesh.Size = tag.Font.GetStringSize(tag.Text) * tag.PixelSize * 1.2f;
        tagBoxMaterial.RenderPriority = RenderPriority + 1;
        tag.RenderPriority = tagBoxMaterial.RenderPriority + 1;

        // TODO: offset tag above the membrane (Z-axis)
    }
}
