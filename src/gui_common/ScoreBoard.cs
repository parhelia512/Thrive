using System.Collections.Generic;
using Godot;

/// <summary>
///   Lists players and their gameplay attributes. Code is pretty much self-contained.
/// </summary>
public class ScoreBoard : CenterContainer
{
    [Export]
    public NodePath ListPath = null!;

    [Export]
    public PackedScene NetworkedPlayerLabelScene = null!;

    public Dictionary<int, NetworkedPlayerLabel> playerLabels = new();

    private VBoxContainer list = null!;

    public override void _Ready()
    {
        list = GetNode<VBoxContainer>(ListPath);

        NetworkManager.Instance.Connect(
            nameof(NetworkManager.RegistrationToServerResultReceived), this, nameof(OnPlayerRegistered));

        GetTree().Connect("network_peer_disconnected", this, nameof(OnPlayerDisconnected));

        foreach (var player in NetworkManager.Instance.PlayerList)
        {
            RegisterPlayer(player.Key, player.Value.Name);
        }
    }

    private void RegisterPlayer(int id, string name)
    {
        var label = (NetworkedPlayerLabel)NetworkedPlayerLabelScene.Instance();
        label.ID = id;
        label.PlayerName = name;
        list.AddChild(label);
        playerLabels.Add(id, label);
    }

    private void UnRegisterPlayer(int id)
    {
        if (playerLabels.TryGetValue(id, out NetworkedPlayerLabel label))
        {
            label.QueueFree();
            playerLabels.Remove(id);
        }
    }

    private void OnPlayerRegistered(int peerId, NetworkManager.RegistrationToServerResult result)
    {
        if (result == NetworkManager.RegistrationToServerResult.Success)
            RegisterPlayer(peerId, NetworkManager.Instance.GetPlayerState(peerId)!.Name);
    }

    private void OnPlayerDisconnected(int peerId)
    {
        UnRegisterPlayer(peerId);
    }
}
