using System;
using System.Globalization;
using System.Linq;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   The networking part of Microbe class for state synchronizations.
/// </summary>
public partial class Microbe
{
    private MeshInstance? tagBox;

    private float lastHitpoints;

    private string? cloudSystemPath;

    /// <summary>
    ///   If set, will assume <see cref="SceneTree.HasNetworkPeer"/> is true and subsequently do network
    ///   operations with the given value.
    /// </summary>
    [JsonProperty]
    public int? PeerId { get; private set; }

    public MultiplayerGameWorld? MultiplayerGameWorld => GameWorld as MultiplayerGameWorld;

    public override string ResourcePath => "res://src/microbe_stage/Microbe.tscn";

    public Action<int>? OnNetworkDeathFinished { get; set; }

    public Action<int, int, string>? OnKilledByAnotherPlayer { get; set; }

    public override void NetworkTick(float delta)
    {
        // TODO: Tag visibility handling
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
            Compounds.SetUseful(SimulationParameters.Instance.IndexToCompound(buffer.ReadByte()));

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

        // Kind of hackish I guess??
        if (cloudSystemPath != null)
            cloudSystem ??= GetNode<CompoundCloudSystem>(cloudSystemPath);
    }

    public override void PackSpawnState(PackedBytesBuffer buffer)
    {
        base.PackSpawnState(buffer);

        buffer.Write(randomSeed);
        buffer.Write(PeerId!.Value);
        buffer.Write(cloudSystem!.GetPath());

        // Sending 2-byte unsigned int... means our max deserialized organelle count is 65535
        buffer.Write((ushort)organelles!.Count);
        foreach (var organelle in organelles!)
        {
            var packed = new PackedBytesBuffer();
            organelle.NetworkSerialize(packed);
            buffer.Write(packed);
        }

        buffer.Write(allOrganellesDivided);
    }

    public override void OnNetworkSpawn(PackedBytesBuffer buffer, GameProperties currentGame)
    {
        base.OnNetworkSpawn(buffer, currentGame);

        randomSeed = buffer.ReadInt32();
        PeerId = buffer.ReadInt32();
        cloudSystemPath = buffer.ReadString();

        AddToGroup(Constants.AI_TAG_MICROBE);
        AddToGroup(Constants.PROCESS_GROUP);
        AddToGroup(Constants.RUNNABLE_MICROBE_GROUP);

        Init(null!, null!, currentGame, true, PeerId);

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

    public void PackInputs(PackedBytesBuffer buffer)
    {
        buffer.Write(MovementDirection.x);
        buffer.Write(MovementDirection.z);
        buffer.Write(LookAtPoint.x);
        buffer.Write(LookAtPoint.z);
        buffer.Write(queuedSlimeSecretionTime);

        var bools = new bool[1] { WantsToEngulf };
        buffer.Write(bools.ToByte());

        // TODO: Agent projectile shooting, preferably after proper client-to-server input implementation
    }

    public void OnNetworkInput(PackedBytesBuffer buffer)
    {
        MovementDirection.x = buffer.ReadSingle();
        MovementDirection.z = buffer.ReadSingle();
        LookAtPoint.x = buffer.ReadSingle();
        LookAtPoint.z = buffer.ReadSingle();
        queuedSlimeSecretionTime = buffer.ReadSingle();

        var bools = buffer.ReadByte();
        WantsToEngulf = bools.ToBoolean(0);
    }

    private void SetupNetworking()
    {
        if (!PeerId.HasValue)
            return;

        Name = PeerId.Value.ToString(CultureInfo.CurrentCulture);

        if (PeerId.Value != NetworkManager.Instance.PeerId!.Value)
            InitNameTag();

        Compounds.LockInputAndOutput = NetworkManager.Instance.IsClient;
    }

    private void InitNameTag()
    {
        if (!PeerId.HasValue)
            return;

        tagBox = GetNode<MeshInstance>("TagBox");

        var tagBoxMesh = (QuadMesh)tagBox.Mesh;
        var tagBoxMaterial = (SpatialMaterial)tagBox.MaterialOverride;

        var tag = tagBox.GetChild<Label3D>(0);

        tagBox.Visible = true;
        tag.Text = NetworkManager.Instance.ConnectedPlayers[PeerId.Value].Name;

        tagBoxMesh.Size = tag.Font.GetStringSize(tag.Text) * tag.PixelSize * 1.2f;
        tagBoxMaterial.RenderPriority = RenderPriority + 1;
        tag.RenderPriority = tagBoxMaterial.RenderPriority + 1;

        // TODO: offset tag above the membrane (Z-axis)
    }
}
