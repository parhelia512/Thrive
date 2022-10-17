using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
///   The networking part of Microbe class for state synchronizations.
/// </summary>
public partial class Microbe
{
    private MeshInstance tagBox = null!;

    private Tween? networkTweener;

    public uint NetEntityId { get; set; }

    public Action<int>? OnNetworkedDeathCompletes { get; set; }

    public void SetupNetworked(int peerId)
    {
        if (!GetTree().HasNetworkPeer())
            return;

        tagBox = GetNode<MeshInstance>("TagBox");

        networkTweener = new Tween();
        AddChild(networkTweener);

        var network = NetworkManager.Instance;

        if (peerId != GetTree().GetNetworkUniqueId())
        {
            var tagBoxMesh = (QuadMesh)tagBox.Mesh;
            var tagBoxMaterial = (SpatialMaterial)tagBox.MaterialOverride;

            var tag = tagBox.GetChild<Label3D>(0);

            tagBox.Visible = true;
            tag.Text = network.PlayerList[peerId].Name;

            tagBoxMesh.Size = tag.Font.GetStringSize(tag.Text) * tag.PixelSize * 1.2f;
            tagBoxMaterial.RenderPriority = RenderPriority + 1;
            tag.RenderPriority = tagBoxMaterial.RenderPriority + 1;

            // TODO: offset tag above the membrane (Z-axis)
        }

        if (!IsNetworkMaster())
            Rpc(nameof(NetworkFetchRandom));
    }

    public void NetworkSyncEveryFrame(int peerId)
    {
        RpcUnreliableId(peerId, nameof(NetworkSyncMovement), GlobalTransform.origin, Rotation);

        RpcUnreliableId(peerId, nameof(NetworkSyncUsefulCompounds),
            Compounds.UsefulCompounds.Select(c => c.InternalName).ToList());

        RpcUnreliableId(peerId, nameof(NetworkSyncCompounds),
            Compounds.Compounds.ToDictionary(c => c.Key.InternalName, c => c.Value));

        RpcUnreliableId(peerId, nameof(NetworkSyncHealth), Hitpoints);
    }

    public void SendMovementDirection(Vector3 direction)
    {
        RpcUnreliable(nameof(NetworkMovementDirectionReceived), direction);
    }

    public void SendLookAtPoint(Vector3 lookAtPoint)
    {
        RpcUnreliable(nameof(NetworkLookAtPointReceived), lookAtPoint);
    }

    public void SendEngulfMode(bool wantsToEngulf)
    {
        Rpc(nameof(NetworkEngulfModeReceived), wantsToEngulf);
    }

    private void IngestEngulfable(string targetPath, float animationSpeed = 2.0f)
    {
        if (IsNetworkMaster())
        {
            Rpc(nameof(NetworkSyncEngulfment), true, targetPath);
        }
        else if (!GetTree().HasNetworkPeer())
        {
            IngestEngulfable(GetNode<IEngulfable>(targetPath), animationSpeed);
        }
    }

    private void EjectEngulfable(string targetPath, float animationSpeed = 2.0f)
    {
        if (IsNetworkMaster())
        {
            Rpc(nameof(NetworkSyncEngulfment), false, targetPath);
        }
        else if (!GetTree().HasNetworkPeer())
        {
            EjectEngulfable(GetNode<IEngulfable>(targetPath), animationSpeed);
        }
    }

    [Puppet]
    private void NetworkSyncMovement(Vector3 position, Vector3 rotation)
    {
        Rotation = rotation;
        networkTweener?.InterpolateProperty(this, "global_transform", null, new Transform(GlobalTransform.basis, position), 0.1f);
        networkTweener?.Start();
    }

    [Puppet]
    private void NetworkSyncHealth(float health)
    {
        Hitpoints = health;
    }

    [Puppet]
    private void NetworkSyncKill()
    {
        Kill();
    }

    [Puppet]
    private void NetworkSyncUsefulCompounds(List<string> usefulCompounds)
    {
        Compounds.ClearUseful();
        foreach (var useful in usefulCompounds)
            Compounds.SetUseful(SimulationParameters.Instance.GetCompound(useful));
    }

    [Puppet]
    private void NetworkSyncCompoundsCapacity(float capacity)
    {
        Compounds.Capacity = capacity;
    }

    [Puppet]
    private void NetworkSyncCompounds(Dictionary<string, float> compounds)
    {
        foreach (var entry in compounds)
        {
            var compound = SimulationParameters.Instance.GetCompound(entry.Key);
            Compounds.Compounds[compound] = entry.Value;
        }
    }

    [Puppet]
    private void NetworkSyncMicrobeState(Microbe.MicrobeState state)
    {
        State = state;
    }

    [Puppet]
    private void NetworkSyncPhagocytosisStep(PhagocytosisPhase phase)
    {
        PhagocytosisStep = phase;
    }

    [PuppetSync]
    private void NetworkSyncEngulfment(bool engulf, string engulfablePath)
    {
        var engulfable = GetNode<IEngulfable>(engulfablePath);

        if (engulf)
        {
            IngestEngulfable(engulfable);
        }
        else
        {
            EjectEngulfable(engulfable);
        }
    }

    [Puppet]
    private void NetworkSyncFlash(float duration, int priority, float r, float g, float b, float a)
    {
        Flash(duration, new Color(r, g, b, a), priority);
    }

    [Puppet]
    private void NetworkSyncAbortFlash()
    {
        AbortFlash();
    }

    [Puppet]
    private void NetworkSyncDigestedAmount(float amount)
    {
        DigestedAmount = amount;
    }

    [Puppet]
    private void NetworkReturnRandom(int seed)
    {
        randomSeed = seed;
        random = new Random(seed);
    }

    [Master]
    private void NetworkMovementDirectionReceived(Vector3 movementDirection)
    {
        MovementDirection = movementDirection;
    }

    [Master]
    private void NetworkLookAtPointReceived(Vector3 lookAtPoint)
    {
        LookAtPoint = lookAtPoint;
    }

    [Master]
    private void NetworkEngulfModeReceived(bool wantsToEngulf)
    {
        State = wantsToEngulf ? MicrobeState.Engulf : MicrobeState.Normal;
    }

    [Master]
    private void NetworkFetchRandom()
    {
        var sender = GetTree().GetRpcSenderId();
        RpcId(sender, nameof(NetworkReturnRandom), randomSeed);
    }
}
