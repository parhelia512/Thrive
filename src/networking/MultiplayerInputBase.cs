using Godot;

/// <summary>
///   Multiplayer version of <see cref="PlayerInputBase"/>. Handles sending client inputs to the server.
/// </summary>
public abstract class MultiplayerInputBase<TStage, TInput> : PlayerInputBase<TStage>
    where TStage : Object, IMultiplayerStage
    where TInput : INetworkInputBatch, new()
{
    public override void _EnterTree()
    {
        base._EnterTree();

        NetworkManager.Instance.NetworkTick += OnNetworkTick;
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        NetworkManager.Instance.NetworkTick -= OnNetworkTick;
    }

    public override void _PhysicsProcess(float delta)
    {
        base._PhysicsProcess(delta);

        // Client prediction
        ApplyInput(NetworkManager.Instance.PeerId, SampleInput());
    }

    protected abstract TInput SampleInput();

    protected abstract void ApplyInput(int peerId, TInput input);

    private void OnNetworkTick(object sender, float delta)
    {
        if (NetworkManager.Instance.IsClient)
        {
            var packed = new PackedBytesBuffer();
            SampleInput().NetworkSerialize(packed);

            // We don't send reliably because another will be resent each network tick anyway
            RpcUnreliableId(NetworkManager.DEFAULT_SERVER_ID, nameof(InputReceived), packed.Data);
        }
    }

    [Remote]
    private void InputReceived(byte[] data)
    {
        if (!NetworkManager.Instance.IsAuthoritative)
            return;

        var packed = new PackedBytesBuffer(data);
        var input = new TInput();
        input.NetworkDeserialize(packed);

        ApplyInput(GetTree().GetRpcSenderId(), input);
    }
}
