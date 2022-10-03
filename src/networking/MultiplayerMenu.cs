using System.Collections.Generic;
using System.Linq;
using Godot;

public class MultiplayerMenu : CenterContainer
{
    [Export]
    public NodePath NameBoxPath = null!;

    [Export]
    public NodePath AddressBoxPath = null!;

    [Export]
    public NodePath ConnectButtonPath = null!;

    [Export]
    public NodePath CreateServerButtonPath = null!;

    [Export]
    public NodePath LobbyPlayerListPath = null!;

    [Export]
    public NodePath ServerNamePath = null!;

    [Export]
    public NodePath PeerCountPath = null!;

    [Export]
    public NodePath StartButtonPath = null!;

    [Export]
    public NodePath KickDialogPath = null!;

    [Export]
    public NodePath KickReasonLineEditPath = null!;

    [Export]
    public NodePath KickedDialogPath = null!;

    [Export]
    public NodePath ChatBoxPath = null!;

    [Export]
    public PackedScene LobbyItemScene = null!;

    private LineEdit nameBox = null!;
    private LineEdit addressBox = null!;
    private CustomConfirmationDialog generalDialog = null!;
    private CustomConfirmationDialog connectingDialog = null!;
    private Button connectButton = null!;
    private Button createServerButton = null!;
    private VBoxContainer lobbyPlayerList = null!;
    private Label peerCount = null!;
    private Button startButton = null!;
    private CustomConfirmationDialog kickDialog = null!;
    private LineEdit kickReasonLineEdit = null!;
    private CustomConfirmationDialog kickedDialog = null!;
    private Label serverName = null!;
    private ChatBox chatBox = null!;

    private Control? primaryMenu;
    private Control? lobbyMenu;

    private Menus currentMenu = Menus.Main;

    private Dictionary<int, LobbyMemberGUI> party = new();

    private int idToKick;

    private float ellipsisAnimTimer = 1.0f;
    private string[] ellipsisAnimSequence = { " .", " ..", " ..." };
    private int ellipsisAnimStep;

    [Signal]
    public delegate void OnClosed();

    public enum Menus
    {
        Main,
        Lobby,
    }

    public Menus CurrentMenu
    {
        get => currentMenu;
        set
        {
            currentMenu = value;

            if (primaryMenu != null && lobbyMenu != null)
                UpdateMenu();
        }
    }

    public override void _Ready()
    {
        nameBox = GetNode<LineEdit>(NameBoxPath);
        addressBox = GetNode<LineEdit>(AddressBoxPath);
        connectButton = GetNode<Button>(ConnectButtonPath);
        createServerButton = GetNode<Button>(CreateServerButtonPath);
        lobbyPlayerList = GetNode<VBoxContainer>(LobbyPlayerListPath);
        peerCount = GetNode<Label>(PeerCountPath);
        startButton = GetNode<Button>(StartButtonPath);
        kickReasonLineEdit = GetNode<LineEdit>(KickReasonLineEditPath);
        kickDialog = GetNode<CustomConfirmationDialog>(KickDialogPath);
        kickedDialog = GetNode<CustomConfirmationDialog>(KickedDialogPath);
        serverName = GetNode<Label>(ServerNamePath);
        chatBox = GetNode<ChatBox>(ChatBoxPath);

        generalDialog = GetNode<CustomConfirmationDialog>("GeneralDialog");
        connectingDialog = GetNode<CustomConfirmationDialog>("ConnectingDialog");
        primaryMenu = GetNode<Control>("PrimaryMenu");
        lobbyMenu = GetNode<Control>("Lobby");

        GetTree().Connect("connected_to_server", this, nameof(OnConnectedToServer));
        GetTree().Connect("server_disconnected", this, nameof(OnServerDisconnected));

        NetworkManager.Instance.Connect(nameof(NetworkManager.ConnectionFailed), this, nameof(OnConnectionFailed));
        NetworkManager.Instance.Connect(nameof(NetworkManager.ConnectedPeersChanged), this, nameof(UpdateLobby));
        NetworkManager.Instance.Connect(nameof(NetworkManager.Kicked), this, nameof(OnKicked));
        NetworkManager.Instance.Connect(nameof(NetworkManager.ReadyForSessionReceived), this, nameof(UpdateReadyStatus));

        UpdateMenu();
        ValidateFields();
    }

    public override void _Process(float delta)
    {
        if (connectingDialog.Visible)
        {
            // 3 trailing dots loading animation (. .. ...)
            ellipsisAnimTimer += delta;
            if (ellipsisAnimTimer >= 1.0f)
            {
                ellipsisAnimStep = (ellipsisAnimStep + 1) % ellipsisAnimSequence.Length;
                ellipsisAnimTimer = 0;
            }

            connectingDialog.WindowTitle = TranslationServer.Translate("CONNECTING") + " (" +
                Mathf.RoundToInt(NetworkManager.Instance.TimePassedConnecting) + "s)";

            connectingDialog.DialogText = "Establishing connection to: " + addressBox.Text +
                ellipsisAnimSequence[ellipsisAnimStep];
        }
    }

    private void UpdateMenu()
    {
        if (primaryMenu == null || lobbyMenu == null)
            throw new SceneTreeAttachRequired();

        switch (currentMenu)
        {
            case Menus.Main:
                primaryMenu.Show();
                lobbyMenu.Hide();
                break;
            case Menus.Lobby:
                primaryMenu.Hide();
                lobbyMenu.Show();
                UpdateLobby();
                break;
        }
    }

    private void UpdateLobby()
    {
        var network = NetworkManager.Instance;
        if (!network.Connected)
            return;

        lobbyPlayerList.FreeChildren();
        party.Clear();

        // For self
        if (!network.IsDedicated)
            CreateMember(GetTree().GetNetworkUniqueId(), network.State!.Name);

        // For other peers
        foreach (var peer in network.ConnectedPeers)
            CreateMember(peer.Key, peer.Value.Name);

        peerCount.Text = 1 + network.ConnectedPeers.Count + " / " + Constants.MULTIPLAYER_DEFAULT_MAX_PLAYERS;
        serverName.Text = network.ServerName;

        UpdateStartButton();
    }

    private void CreateMember(int peerId, string name)
    {
        var member = LobbyItemScene.Instance<LobbyMemberGUI>();
        member.ID = peerId;
        member.PlayerName = name + (peerId == NetworkManager.DEFAULT_SERVER_ID ? " (Host)" : string.Empty);
        member.Current = peerId == GetTree().GetNetworkUniqueId();

        member.Connect(nameof(LobbyMemberGUI.Kicked), this, nameof(OnLobbyMemberKicked));

        lobbyPlayerList.AddChild(member);
        party.Add(peerId, member);
    }

    private void ValidateFields()
    {
        connectButton.Disabled = string.IsNullOrEmpty(addressBox.Text);
    }

    private void UpdateStartButton()
    {
        var network = NetworkManager.Instance;

        if (GetTree().IsNetworkServer())
        {
            startButton.Text = TranslationServer.Translate("START");
            startButton.Disabled = network.ConnectedPeers.Any(p => !p.Value.CurrentStatus.HasFlag(PlayerState.Status.ReadyForSession));
            startButton.ToggleMode = false;
        }
        else
        {
            startButton.Text = network.GameInSession ? TranslationServer.Translate("JOIN")
                :
                TranslationServer.Translate("READY");
            startButton.Disabled = false;
            startButton.ToggleMode = !network.GameInSession;
        }
    }

    private void UpdateReadyStatus(int peerId, bool ready)
    {
        if (party.TryGetValue(peerId, out LobbyMemberGUI list))
            list.Ready = ready;

        UpdateStartButton();
    }

    private void OnConnectPressed()
    {
        GUICommon.Instance.PlayButtonPressSound();

        connectingDialog.PopupCenteredShrink();

        var name = string.IsNullOrEmpty(nameBox.Text) ? Settings.Instance.ActiveUsername : nameBox.Text;

        var error = NetworkManager.Instance.ConnectToServer(addressBox.Text, name);
        if (error != Error.Ok)
        {
            connectingDialog.Hide();
            generalDialog.WindowTitle = TranslationServer.Translate("CONNECTION_FAILED");
            generalDialog.DialogText = "Failed to establish connection: " + error;
            generalDialog.PopupCenteredShrink();
        }
    }

    private void OnConnectingCancelled()
    {
        NetworkManager.Instance.Disconnect();
    }

    private void OnCreatePressed()
    {
        GUICommon.Instance.PlayButtonPressSound();

        var name = string.IsNullOrEmpty(nameBox.Text) ? Settings.Instance.ActiveUsername : nameBox.Text;

        var error = NetworkManager.Instance.CreateServer(addressBox.Text, name);
        if (error != Error.Ok)
        {
            connectingDialog.Hide();
            generalDialog.WindowTitle = TranslationServer.Translate("HOSTING_FAILED");
            generalDialog.DialogText = "Failed to create server: " + error;
            generalDialog.PopupCenteredShrink();
        }
        else
        {
            CurrentMenu = Menus.Lobby;
        }
    }

    private void OnBackPressed()
    {
        GUICommon.Instance.PlayButtonPressSound();
        EmitSignal(nameof(OnClosed));
    }

    private void OnDisconnectPressed()
    {
        GUICommon.Instance.PlayButtonPressSound();
        NetworkManager.Instance.Disconnect();
        CurrentMenu = Menus.Main;

        chatBox.ClearMessages();
    }

    private void OnConnectedToServer()
    {
        connectingDialog.Hide();
        CurrentMenu = Menus.Lobby;
    }

    private void OnConnectionFailed(string reason)
    {
        connectingDialog.Hide();
        generalDialog.WindowTitle = TranslationServer.Translate("CONNECTION_FAILED");
        generalDialog.DialogText = "Failed to establish connection: " + reason;
        generalDialog.PopupCenteredShrink();
    }

    private void OnServerDisconnected()
    {
        chatBox.ClearMessages();
        CurrentMenu = Menus.Main;
    }

    private void OnLobbyMemberKicked(int id)
    {
        kickDialog.PopupCenteredShrink();
        idToKick = id;
    }

    private void OnKickConfirmed()
    {
        if (idToKick <= 1)
        {
            GD.Print("[Client] Attempting to kick host/server, this is not allowed");
            return;
        }

        NetworkManager.Instance.Kick(idToKick, kickReasonLineEdit.Text);
    }

    private void OnKickCancelled()
    {
        idToKick = 0;
    }

    private void OnKicked(string reason)
    {
        kickedDialog.DialogText = "You have been kicked. Reason for kick: " + (string.IsNullOrEmpty(reason) ? "not specified" : reason);
        kickedDialog.PopupCenteredShrink();
        CurrentMenu = Menus.Main;
    }

    private void OnStartPressed()
    {
        NetworkManager.Instance.StartGameSession();
    }

    private void OnReadyToggled(bool active)
    {
        NetworkManager.Instance.SetPlayerStatus(
            active ? PlayerState.Status.Lobby | PlayerState.Status.ReadyForSession : PlayerState.Status.Lobby);
    }
}
