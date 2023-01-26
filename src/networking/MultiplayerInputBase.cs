using System;
using System.Collections.Generic;
using Godot;

/// <summary>
///   Handles sending client inputs to the server.
/// </summary>
public abstract class MultiplayerInputBase : PlayerInputBase
{
    protected NetworkInputVars lastSampledInput;

    private Dictionary<int, PeerInputs> peersInputs = new();

    /// <summary>
    ///   Contain inputs for the local player, see <see cref="NetworkCharacter.IsLocal"/>.
    /// </summary>
    public PeerInputs LocalInputs
    {
        get
        {
            peersInputs.TryGetValue(NetworkManager.Instance.PeerId, out var peerInput);
            return peerInput;
        }
    }

    public IReadOnlyDictionary<int, PeerInputs> PeersInputs => peersInputs;

    protected IMultiplayerStage MultiplayerStage => stage as IMultiplayerStage ??
        throw new InvalidOperationException("Stage hasn't been set");

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

    public override void _Ready()
    {
        base._Ready();

        if (NetworkManager.Instance.IsServer)
        {
            NetworkManager.Instance.Connect(nameof(NetworkManager.PlayerJoined), this, nameof(OnPlayerJoined));
            NetworkManager.Instance.Connect(nameof(NetworkManager.PlayerLeft), this, nameof(OnPlayerLeft));
        }
        else if (NetworkManager.Instance.IsClient)
        {
            OnPlayerJoined(NetworkManager.Instance.PeerId);
        }
    }

    public override void _PhysicsProcess(float delta)
    {
        base._PhysicsProcess(delta);

        if (NetworkManager.Instance.LocalPlayer?.Status == NetworkPlayerStatus.Active)
            DebugOverlays.Instance.ReportUnackedInputs(LocalInputs.Buffer.Count);

        if (NetworkManager.Instance.IsServer)
            ProcessIncomingInputs();

        var sampled = SampleInput();
        if (!ShouldApplyInput(sampled))
            return;

        sampled.Id = ++lastSampledInput.Id;
        lastSampledInput = sampled;

        if (NetworkManager.Instance.IsClient)
            LocalInputs.Buffer.Enqueue(sampled);

        // For client, this is client-side prediction
        ProcessInput(NetworkManager.Instance.PeerId, sampled);
    }

    /// <summary>
    ///   Returns whether the local player's sampled input should be applied (and sent to the server).
    /// </summary>
    protected virtual bool ShouldApplyInput(NetworkInputVars sampled)
    {
        return NetworkManager.Instance.LocalPlayer?.Status == NetworkPlayerStatus.Active &&
            sampled != lastSampledInput;
    }

    protected abstract NetworkInputVars SampleInput();

    /// <summary>
    ///   Exclusively server-side.
    /// </summary>
    protected virtual void ProcessIncomingInputs()
    {
        foreach (var peerInputs in peersInputs)
        {
            while (peerInputs.Value.Buffer.Count > 0)
            {
                var input = peerInputs.Value.Buffer.Dequeue();

                peerInputs.Value.LastAckedInputId = input.Id;

                ProcessInput(peerInputs.Key, input);
            }
        }
    }

    /// <summary>
    ///   Shared by both server and client.
    /// </summary>
    protected virtual void ProcessInput(int peerId, NetworkInputVars input)
    {
        if (!MultiplayerStage.TryGetPlayer(peerId, out NetworkCharacter character))
            return;

        if (NetworkManager.Instance.GetPlayerInfo(peerId)?.Status != NetworkPlayerStatus.Active)
            return;

        character.ApplyInput(input);
    }

    private void OnNetworkTick(object sender, float delta)
    {
        if (!NetworkManager.Instance.IsClient)
            return;

        var packet = new PackedBytesBuffer();

        while (LocalInputs.Buffer.Count > 0)
        {
            // Batch buffered inputs into one packet
            var input = LocalInputs.Buffer.Dequeue();
            input.NetworkSerialize(packet);
        }

        // We don't reliably send because another will be resent each network tick anyway
        RpcUnreliableId(NetworkManager.DEFAULT_SERVER_ID, nameof(InputReceived), packet.Data);
    }

    private void OnPlayerJoined(int peerId)
    {
        peersInputs[peerId] = new PeerInputs();
    }

    private void OnPlayerLeft(int peerId)
    {
        peersInputs.Remove(peerId);
    }

    [Remote]
    private void InputReceived(byte[] data)
    {
        if (!NetworkManager.Instance.IsServer)
            return;

        var sender = GetTree().GetRpcSenderId();

        if (!peersInputs.ContainsKey(sender))
            OnPlayerJoined(sender);

        var packet = new PackedBytesBuffer(data);

        while (packet.Position < packet.Length)
        {
            var input = default(NetworkInputVars);
            input.NetworkDeserialize(packet);

            // TODO: input validation i.e. cheat preventions
            peersInputs[sender].Buffer.Enqueue(input);
        }
    }

    public class PeerInputs
    {
        public ushort LastAckedInputId { get; set; }
        public Queue<NetworkInputVars> Buffer { get; set; } = new();
    }
}
