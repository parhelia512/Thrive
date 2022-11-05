using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using Newtonsoft.Json;

public class MultiplayerGUI : CenterContainer
{
    [Export]
    public NodePath NameBoxPath = null!;

    [Export]
    public NodePath AddressBoxPath = null!;

    [Export]
    public NodePath PortBoxPath = null!;

    [Export]
    public NodePath ConnectButtonPath = null!;

    [Export]
    public NodePath CreateServerButtonPath = null!;

    [Export]
    public NodePath LobbyPlayerListPath = null!;

    [Export]
    public NodePath ServerNamePath = null!;

    [Export]
    public NodePath ServerAttributesPath = null!;

    [Export]
    public NodePath PeerCountPath = null!;

    [Export]
    public NodePath StartButtonPath = null!;

    [Export]
    public NodePath KickDialogPath = null!;

    [Export]
    public NodePath KickedDialogPath = null!;

    [Export]
    public NodePath ChatBoxPath = null!;

    [Export]
    public PackedScene NetworkedPlayerLabelScene = null!;

    private readonly string[] ellipsisAnimationSequence = { " ", " .", " ..", " ..." };

    private LineEdit nameBox = null!;
    private LineEdit addressBox = null!;
    private LineEdit portBox = null!;
    private CustomConfirmationDialog generalDialog = null!;
    private CustomConfirmationDialog loadingDialog = null!;
    private Button connectButton = null!;
    private Button createServerButton = null!;
    private VBoxContainer list = null!;
    private ServerSetup serverSetup = null!;
    private Label peerCount = null!;
    private Button startButton = null!;
    private KickPlayerDialog kickDialog = null!;
    private CustomConfirmationDialog kickedDialog = null!;
    private Label serverName = null!;
    private Label serverAttributes = null!;
    private ChatBox chatBox = null!;

    private Control? primaryMenu;
    private Control? lobbyMenu;

    private Submenu currentMenu = Submenu.Main;

    private Dictionary<int, NetPlayerLog> playerLabels = new();

    private string loadingDialogTitle = string.Empty;
    private string loadingDialogText = string.Empty;

    private float ellipsisAnimationTimer = 1.0f;
    private int ellipsisAnimationStep;

    private ConnectionJob currentJobStatus = ConnectionJob.None;

    [Signal]
    public delegate void OnClosed();

    public enum Submenu
    {
        Main,
        Lobby,
    }

    private enum ConnectionJob
    {
        None,
        Connecting,
        SettingUpUPNP,
        PortForwarding,
    }

    public Submenu CurrentMenu
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
        portBox = GetNode<LineEdit>(PortBoxPath);
        connectButton = GetNode<Button>(ConnectButtonPath);
        createServerButton = GetNode<Button>(CreateServerButtonPath);
        list = GetNode<VBoxContainer>(LobbyPlayerListPath);
        peerCount = GetNode<Label>(PeerCountPath);
        startButton = GetNode<Button>(StartButtonPath);
        kickDialog = GetNode<KickPlayerDialog>(KickDialogPath);
        kickedDialog = GetNode<CustomConfirmationDialog>(KickedDialogPath);
        serverName = GetNode<Label>(ServerNamePath);
        serverAttributes = GetNode<Label>(ServerAttributesPath);
        chatBox = GetNode<ChatBox>(ChatBoxPath);

        generalDialog = GetNode<CustomConfirmationDialog>("GeneralDialog");
        loadingDialog = GetNode<CustomConfirmationDialog>("LoadingDialog");
        primaryMenu = GetNode<Control>("PrimaryMenu");
        lobbyMenu = GetNode<Control>("Lobby");
        serverSetup = GetNode<ServerSetup>("ServerSetup");

        GetTree().Connect("server_disconnected", this, nameof(OnServerDisconnected));

        NetworkManager.Instance.Connect(
            nameof(NetworkManager.RegistrationToServerResultReceived), this, nameof(OnRegisteredToServer));
        NetworkManager.Instance.Connect(nameof(NetworkManager.ConnectionFailed), this, nameof(OnConnectionFailed));
        NetworkManager.Instance.Connect(nameof(NetworkManager.ServerStateUpdated), this, nameof(UpdateLobby));
        NetworkManager.Instance.Connect(nameof(NetworkManager.Kicked), this, nameof(OnKicked));
        NetworkManager.Instance.Connect(
            nameof(NetworkManager.ReadyForSessionReceived), this, nameof(UpdateReadyStatus));
        NetworkManager.Instance.Connect(
            nameof(NetworkManager.UPNPCallResultReceived), this, nameof(OnUPNPCallResultReceived));

        UpdateMenu();
        ResetFields();
        ValidateFields();
    }

    public override void _Process(float delta)
    {
        if (loadingDialog.Visible)
            UpdateLoadingDialog(delta);

        var network = NetworkManager.Instance;

        var builder = new StringBuilder(100);
        builder.Append(" - ");
        builder.Append(network.Settings?.GetGameModeReadable());
        builder.Append(network.GameInSession ?
            $" [In progress] [{network.FormattedGameTimeHumanized}]" : " [Preparing]");

        serverAttributes.Text = builder.ToString();
    }

    public void ShowKickedDialog(string reason)
    {
        kickedDialog.DialogText = "You have been kicked. Reason for kick: " + (string.IsNullOrEmpty(reason) ? "unspecified" : reason);
        kickedDialog.PopupCenteredShrink();
    }

    private void UpdateLobby()
    {
        var network = NetworkManager.Instance;
        if (!network.Connected)
            return;

        list.QueueFreeChildren();
        playerLabels.Clear();

        foreach (var peer in network.PlayerList)
            CreatePlayerLabel(peer.Key, peer.Value.Name);

        peerCount.Text = $"{network.PlayerList.Count} / {network.Settings?.MaxPlayers}";
        serverName.Text = network.Settings?.Name;

        UpdateStartButton();
    }

    private void CreatePlayerLabel(int peerId, string name)
    {
        var label = NetworkedPlayerLabelScene.Instance<NetPlayerLog>();
        label.ID = peerId;
        label.PlayerName = name;
        label.Highlight = NetworkManager.Instance.GetPlayerInfo(peerId)!.ReadyForSession;

        label.Connect(nameof(NetPlayerLog.KickRequested), this, nameof(OnKickButtonPressed));

        list.AddChild(label);
        playerLabels.Add(peerId, label);
    }

    private void ResetFields()
    {
        portBox.Text = Constants.MULTIPLAYER_DEFAULT_PORT.ToString(CultureInfo.CurrentCulture);
        addressBox.Text = Constants.LOCAL_HOST;
    }

    private void ValidateFields()
    {
        connectButton.Disabled = string.IsNullOrEmpty(addressBox.Text);
    }

    private void UpdateStartButton()
    {
        var network = NetworkManager.Instance;

        if (NetworkManager.Instance.IsAuthoritative)
        {
            startButton.Text = TranslationServer.Translate("START");
            startButton.Disabled = network.PlayerList.Any(
                p => p.Key != NetworkManager.DEFAULT_SERVER_ID && !p.Value.ReadyForSession);
            startButton.ToggleMode = false;
        }
        else if (NetworkManager.Instance.IsClient)
        {
            startButton.Text = network.GameInSession ? TranslationServer.Translate("JOIN")
                :
                TranslationServer.Translate("READY");
            startButton.Disabled = false;
            startButton.ToggleMode = !network.GameInSession;

            if (network.PlayerInfo != null)
                startButton.SetPressedNoSignal(network.PlayerInfo.ReadyForSession);
        }
    }

    private void UpdateReadyStatus(int peerId, bool ready)
    {
        if (playerLabels.TryGetValue(peerId, out NetPlayerLog list))
            list.Highlight = ready;

        UpdateStartButton();
    }

    private void ShowGeneralDialog(string title, string text)
    {
        generalDialog.WindowTitle = title;
        generalDialog.DialogText = text;
        generalDialog.PopupCenteredShrink();
    }

    private void ShowLoadingDialog(string title, string text, bool allowClosing = true)
    {
        loadingDialogTitle = title;
        loadingDialogText = text;
        loadingDialog.ShowCloseButton = allowClosing;
        loadingDialog.PopupCenteredShrink();
    }

    private void UpdateLoadingDialog(float delta)
    {
        // 1 whitespace and 3 trailing dots loading animation (  . .. ...)
        ellipsisAnimationTimer += delta;
        if (ellipsisAnimationTimer >= 1.0f)
        {
            ellipsisAnimationStep = (ellipsisAnimationStep + 1) % ellipsisAnimationSequence.Length;
            ellipsisAnimationTimer = 0;
        }

        loadingDialog.WindowTitle = loadingDialogTitle;
        loadingDialog.DialogText = loadingDialogText + ellipsisAnimationSequence[ellipsisAnimationStep];

        if (GetTree().NetworkPeer.GetConnectionStatus() == NetworkedMultiplayerPeer.ConnectionStatus.Connecting)
        {
            loadingDialog.WindowTitle += " (" + Mathf.RoundToInt(NetworkManager.Instance.TimePassedConnecting) + "s)";
        }
    }

    private void UpdateMenu()
    {
        if (primaryMenu == null || lobbyMenu == null)
            throw new SceneTreeAttachRequired();

        switch (currentMenu)
        {
            case Submenu.Main:
                primaryMenu.Show();
                lobbyMenu.Hide();
                break;
            case Submenu.Lobby:
                primaryMenu.Hide();
                lobbyMenu.Show();
                UpdateLobby();
                break;
        }
    }

    private void ReadNameAndPort(out string name, out int port)
    {
        name = string.IsNullOrEmpty(nameBox.Text) ? Settings.Instance.ActiveUsername : nameBox.Text;
        port = string.IsNullOrEmpty(portBox.Text) || !int.TryParse(portBox.Text, out int parsedPort) ?
            Constants.MULTIPLAYER_DEFAULT_PORT : parsedPort;
    }

    private void OnConnectPressed()
    {
        GUICommon.Instance.PlayButtonPressSound();

        ShowLoadingDialog(
            TranslationServer.Translate("CONNECTING"), "Establishing connection to " + addressBox.Text + ":" + portBox.Text);

        ReadNameAndPort(out string name, out int port);

        var error = NetworkManager.Instance.ConnectToServer(addressBox.Text, port, name);
        if (error != Error.Ok)
        {
            loadingDialog.Hide();
            ShowGeneralDialog(
                TranslationServer.Translate("CONNECTION_FAILED"), "Failed to establish connection: " + error);
            return;
        }

        currentJobStatus = ConnectionJob.Connecting;
    }

    private void OnCreatePressed()
    {
        GUICommon.Instance.PlayButtonPressSound();
        ReadNameAndPort(out string name, out int port);
        serverSetup.Open(name, addressBox.Text, port);
    }

    private void OnServerSetupConfirmed(string data)
    {
        ServerSettings parsedData;

        try
        {
            parsedData = JsonSerializer.Create()
                    .Deserialize<ServerSettings>(new JsonTextReader(new StringReader(data))) ??
                throw new Exception("deserialized value is null");
        }
        catch (Exception e)
        {
            ShowGeneralDialog(TranslationServer.Translate("HOSTING_FAILED"), "Failed to create server: " + e);
            GD.PrintErr("Can't setup server due to parse failure on data: ", e);
            return;
        }

        var playerName = string.IsNullOrEmpty(nameBox.Text) ? Settings.Instance.ActiveUsername : nameBox.Text;

        var error = NetworkManager.Instance.CreatePlayerHostedServer(playerName, parsedData);
        if (error != Error.Ok)
        {
            loadingDialog.Hide();
            ShowGeneralDialog(TranslationServer.Translate("HOSTING_FAILED"), "Failed to create server: " + error);
            return;
        }

        CurrentMenu = Submenu.Lobby;

        if (parsedData.UseUPNP)
        {
            ShowLoadingDialog(
                TranslationServer.Translate("UPNP_SETUP"), "[UPnP] Discovering devices", false);

            currentJobStatus = ConnectionJob.SettingUpUPNP;
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
        CurrentMenu = Submenu.Main;

        chatBox.ClearMessages();
    }

    private void OnLoadingCancelled()
    {
        switch (currentJobStatus)
        {
            case ConnectionJob.Connecting:
                NetworkManager.Instance.Print("Cancelling connection");
                NetworkManager.Instance.Disconnect();
                break;

            // TODO: handle upnp work cancellations, currently you can't cancel these
        }

        currentJobStatus = ConnectionJob.None;
    }

    private void OnRegisteredToServer(int peerId, NetworkManager.RegistrationToServerResult result)
    {
        if (peerId != GetTree().GetNetworkUniqueId())
            return;

        loadingDialog.Hide();

        if (result == NetworkManager.RegistrationToServerResult.ServerFull)
        {
            ShowGeneralDialog(TranslationServer.Translate("SERVER_FULL"), $"Server is full " +
                $"{NetworkManager.Instance.PlayerList.Count}/{NetworkManager.Instance.Settings?.MaxPlayers}");
            return;
        }

        CurrentMenu = Submenu.Lobby;

        currentJobStatus = ConnectionJob.None;

        NetworkManager.Instance.Print(
            "Connection to ", addressBox.Text, ":", portBox.Text, " succeeded," +
            " using network ID (", GetTree().GetNetworkUniqueId(), ")");
    }

    private void OnConnectionFailed(string reason)
    {
        if (!loadingDialog.Visible)
            return;

        loadingDialog.Hide();

        ShowGeneralDialog(
            TranslationServer.Translate("CONNECTION_FAILED"), "Failed to establish connection: " + reason);

        currentJobStatus = ConnectionJob.None;

        NetworkManager.Instance.PrintError(
            "Connection to ", addressBox.Text, ":", portBox.Text, " failed: ", reason);
    }

    private void OnServerDisconnected()
    {
        chatBox.ClearMessages();

        ShowGeneralDialog(
            TranslationServer.Translate("SERVER_DISCONNECTED"), "Connection was closed by the remote host");

        CurrentMenu = Submenu.Main;
    }

    private void OnKickButtonPressed(int peerId)
    {
        kickDialog.RequestKick(peerId);
    }

    private void OnKicked(string reason)
    {
        ShowKickedDialog(reason);
        CurrentMenu = Submenu.Main;
    }

    private void OnStartPressed()
    {
        NetworkManager.Instance.StartGame();
        startButton.Disabled = true;
    }

    private void OnReadyToggled(bool active)
    {
        NetworkManager.Instance.SetReadyForSessionStatus(active);
    }

    private void OnUPNPCallResultReceived(UPNP.UPNPResult result, NetworkManager.UPNPActionStep step)
    {
        switch (step)
        {
            case NetworkManager.UPNPActionStep.Setup:
            {
                if (result != UPNP.UPNPResult.Success)
                {
                    loadingDialog.Hide();

                    ShowGeneralDialog(TranslationServer.Translate("UPNP_SETUP"),
                        "[UPnP] An error occurred while trying to set up: " + result.ToString());

                    currentJobStatus = ConnectionJob.None;
                }
                else
                {
                    ShowLoadingDialog(TranslationServer.Translate("PORT_FORWARDING"),
                        "[UPnP] Attempting to forward port (" + portBox.Text + ")", false);

                    currentJobStatus = ConnectionJob.PortForwarding;
                }

                break;
            }

            case NetworkManager.UPNPActionStep.PortMapping:
            {
                loadingDialog.Hide();

                if (result != UPNP.UPNPResult.Success)
                {
                    ShowGeneralDialog(TranslationServer.Translate("PORT_FORWARDING"),
                        "[UPnP] Attempting to forward port failed: " + result.ToString());
                }

                currentJobStatus = ConnectionJob.None;

                break;
            }
        }
    }
}
