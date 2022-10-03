using Godot;

public partial class Microbe
{
    private MeshInstance tagBox = null!;

    private float networkTick;

    private Tween? movementTween;

    public void SetupPlayerClient(int peerId)
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
            tag.Text = network.ConnectedPeers[peerId].Name;

            tagBoxMesh.Size = tag.Font.GetStringSize(tag.Text) * tag.PixelSize * 1.2f;
            tagBoxMaterial.RenderPriority = RenderPriority + 1;
            tag.RenderPriority = tagBoxMaterial.RenderPriority + 1;

            // TODO: offset tag above the membrane (Z-axis)
        }

        if (!IsNetworkMaster())
            SetPhysicsProcess(false);
    }

    public void NetworkSendMovementInputs(Vector3 movementDirection, Vector3 lookAtPoint)
    {
        RpcUnreliable(nameof(NetworkReceiveMovementInputs), movementDirection, lookAtPoint);
    }

    private void HandleNetworking(float delta)
    {
        if (!GetTree().HasNetworkPeer())
            return;

        if (IsNetworkMaster())
        {
            networkTick += delta;

            // Send network updates at 30 FPS
            if (networkTick > NetworkManager.Instance.TickRateDelay)
            {
                foreach (var peer in NetworkManager.Instance.ConnectedPeers)
                {
                    // TODO: fix desync during scene change
                    if (peer.Value.CurrentStatus == PlayerState.Status.InGame)
                        RpcUnreliableId(peer.Key, nameof(NetworkReceiveMovementState), GlobalTransform.origin, Rotation);
                }

                networkTick = 0;
            }
        }
    }

    [Puppet]
    private void NetworkReceiveMovementState(Vector3 position, Vector3 rotation)
    {
        Rotation = rotation;
        movementTween?.InterpolateProperty(this, "global_transform", null, new Transform(GlobalTransform.basis, position), 0.1f);
        movementTween?.Start();
    }

    [Master]
    private void NetworkReceiveMovementInputs(Vector3 movementDirection, Vector3 lookAtPoint)
    {
        MovementDirection = movementDirection;
        LookAtPoint = lookAtPoint;
    }
}
