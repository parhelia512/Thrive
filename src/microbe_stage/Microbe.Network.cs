using System;
using System.Collections.Generic;
using Godot;

/// <summary>
///   The networking part of Microbe class for multiplayer.
/// </summary>
public partial class Microbe
{
    private MeshInstance tagBox = null!;

    private Tween? movementTween;

    public Action<int>? OnNetworkedDeathCompletes { get; set; }

    public void SetupNetworked(int peerId)
    {
        if (!GetTree().HasNetworkPeer())
            return;

        tagBox = GetNode<MeshInstance>("TagBox");

        movementTween = new Tween();
        AddChild(movementTween);

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
    }

    public void Sync(IReadOnlyDictionary<int, EntityReference<Microbe>> peers)
    {
        foreach (var peer in peers)
        {
            if (peer.Key == GetTree().GetNetworkUniqueId())
                continue;

            RpcUnreliableId(peer.Key, nameof(NetworkSyncMovement), GlobalTransform.origin, Rotation);
        }
    }

    public void SendMovementDirection(Vector3 direction)
    {
        RpcUnreliable(nameof(NetworkMovementDirectionReceived), direction);
    }

    public void SendLookAtPoint(Vector3 lookAtPoint)
    {
        RpcUnreliable(nameof(NetworkLookAtPointReceived), lookAtPoint);
    }

    [Puppet]
    private void NetworkSyncMovement(Vector3 position, Vector3 rotation)
    {
        Rotation = rotation;
        movementTween?.InterpolateProperty(this, "global_transform", null, new Transform(GlobalTransform.basis, position), 0.1f);
        movementTween?.Start();
    }

    [Puppet]
    private void NetworkSyncHealth(float health)
    {
        Hitpoints = health;

        if (Hitpoints <= 0.0f)
        {
            Hitpoints = 0.0f;
            Kill();
        }
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
}
