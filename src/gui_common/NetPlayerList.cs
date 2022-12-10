using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
///   Lists (networked) players and their gameplay attributes. Code is pretty much self-contained.
/// </summary>
public class NetPlayerList : VBoxContainer
{
    [Export]
    public NodePath ListPath = null!;

    [Export]
    public NodePath PlayerCountPath = null!;

    [Export]
    public NodePath KickPlayerDialogPath = null!;

    [Export]
    public PackedScene NetPlayerLogScene = null!;

    private Dictionary<int, NetPlayerLog> playerLogs = new();

    private List<int>? sortedKeys;

    private VBoxContainer list = null!;
    private Label playerCount = null!;
    private KickPlayerDialog kickDialog = null!;

    public override void _Ready()
    {
        list = GetNode<VBoxContainer>(ListPath);
        playerCount = GetNode<Label>(PlayerCountPath);
        kickDialog = GetNode<KickPlayerDialog>(KickPlayerDialogPath);

        NetworkManager.Instance.Connect(
            nameof(NetworkManager.RegistrationResultReceived), this, nameof(OnPlayerRegistered));

        GetTree().Connect("network_peer_disconnected", this, nameof(OnPlayerDisconnected));

        RefreshPlayers();
    }

    public void RefreshPlayers()
    {
        list.FreeChildren();
        playerLogs.Clear();

        foreach (var player in NetworkManager.Instance.ConnectedPlayers)
        {
            RegisterPlayer(player.Key, player.Value.Name);
        }
    }

    public void SortHighestScoreFirst()
    {
        sortedKeys = NetworkManager.Instance.ConnectedPlayers
            .OrderByDescending(p =>
            {
                p.Value.Ints.TryGetValue("score", out int score);
                return score;
            })
            .Select(p => p.Key)
            .ToList();

        for (int i = 0; i < sortedKeys.Count; i++)
        {
            if (!playerLogs.TryGetValue(sortedKeys[i], out NetPlayerLog log))
                continue;

            list.MoveChild(log, i);
        }
    }

    public NetPlayerLog GetFirst()
    {
        if (sortedKeys == null)
            sortedKeys = playerLogs.Select(p => p.Key).ToList();

        return playerLogs[sortedKeys.First()];
    }

    public NetPlayerLog? GetPlayer(int id)
    {
        playerLogs.TryGetValue(id, out NetPlayerLog log);
        return log;
    }

    private void RegisterPlayer(int id, string name)
    {
        if (playerLogs.ContainsKey(id))
            return;

        var log = (NetPlayerLog)NetPlayerLogScene.Instance();
        log.ID = id;
        log.PlayerName = name;

        log.Connect(nameof(NetPlayerLog.KickRequested), this, nameof(OnKickButtonPressed));

        list.AddChild(log);
        playerLogs.Add(id, log);

        playerCount.Text = $"{NetworkManager.Instance.ConnectedPlayers.Count}/" +
            $"{NetworkManager.Instance.Settings?.MaxPlayers}";
    }

    private void UnRegisterPlayer(int id)
    {
        if (playerLogs.TryGetValue(id, out NetPlayerLog log))
        {
            log.QueueFree();
            playerLogs.Remove(id);
        }
    }

    private void OnPlayerRegistered(int peerId, NetworkManager.RegistrationResult result)
    {
        if (result == NetworkManager.RegistrationResult.Success)
            RegisterPlayer(peerId, NetworkManager.Instance.GetPlayerInfo(peerId)!.Name);
    }

    private void OnPlayerDisconnected(int peerId)
    {
        UnRegisterPlayer(peerId);
    }

    private void OnKickButtonPressed(int peerId)
    {
        kickDialog.RequestKick(peerId);
    }
}
