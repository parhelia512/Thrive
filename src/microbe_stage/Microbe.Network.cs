using Godot;

/// <summary>
///   The networking part of Microbe class for multiplayer.
/// </summary>
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
            tag.Text = network.PlayerList[peerId].Name;

            tagBoxMesh.Size = tag.Font.GetStringSize(tag.Text) * tag.PixelSize * 1.2f;
            tagBoxMaterial.RenderPriority = RenderPriority + 1;
            tag.RenderPriority = tagBoxMaterial.RenderPriority + 1;

            // TODO: offset tag above the membrane (Z-axis)
        }
    }

    public void Send()
    {
        if (!GetTree().HasNetworkPeer() || IsNetworkMaster())
            return;

        RpcUnreliableId(1, nameof(NetworkStateReceived), MovementDirection, LookAtPoint, Dead);
    }

    public void Sync()
    {
        if (!GetTree().HasNetworkPeer() || !IsNetworkMaster())
            return;

        foreach (var player in NetworkManager.Instance.PlayerList)
        {
            if (IsNetworkMaster() && player.Key == GetTree().GetNetworkUniqueId())
                continue;

            if (player.Value.CurrentEnvironment == PlayerState.Environment.InGame)
                RpcUnreliableId(player.Key, nameof(NetworkSync), GlobalTransform.origin, Rotation, Dead);
        }
    }

    [Puppet]
    private void NetworkSync(Vector3 position, Vector3 rotation, bool dead)
    {
        // TODO: maybe pass in a structured object

        Rotation = rotation;
        movementTween?.InterpolateProperty(this, "global_transform", null, new Transform(GlobalTransform.basis, position), 0.1f);
        movementTween?.Start();

        if (dead)
            Kill();
    }

    [Master]
    private void NetworkStateReceived(Vector3 movementDirection, Vector3 lookAtPoint, bool dead)
    {
        // TODO: maybe pass in a structured object

        MovementDirection = movementDirection;
        LookAtPoint = lookAtPoint;
        Dead = dead;
    }
}
