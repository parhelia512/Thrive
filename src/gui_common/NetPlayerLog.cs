using System.Text;
using Godot;

public class NetPlayerLog : PanelContainer
{
    [Export]
    public NodePath NamePath = null!;

    [Export]
    public NodePath KickButtonPath = null!;

    [Export]
    public NodePath CrossPath = null!;

    [Export]
    public NodePath SpacerPath = null!;

    private CustomRichTextLabel? nameLabel;
    private Label killsLabel = null!;
    private Label deathsLabel = null!;
    private Button kickButton = null!;
    private TextureRect cross = null!;
    private Control spacer = null!;

    private string playerName = string.Empty;
    private bool highlight;

    [Signal]
    public delegate void KickRequested(int id);

    public int ID { get; set; } = -1;

    public string PlayerName
    {
        get => playerName;
        set
        {
            playerName = value;

            if (nameLabel != null)
                UpdateName();
        }
    }

    public bool Highlight
    {
        get => highlight;
        set
        {
            if (highlight == value)
                return;

            highlight = value;
            UpdateReadyState();
        }
    }

    public override void _Ready()
    {
        nameLabel = GetNode<CustomRichTextLabel>(NamePath);
        kickButton = GetNode<Button>(KickButtonPath);
        cross = GetNode<TextureRect>(CrossPath);
        spacer = GetNode<Control>(SpacerPath);

        killsLabel = GetNode<Label>("HBoxContainer/Kills");
        deathsLabel = GetNode<Label>("HBoxContainer/Deaths");

        NetworkManager.Instance.Connect(
            nameof(NetworkManager.PlayerStatusChanged), this, nameof(OnPlayerStatusChanged));

        UpdateName();
        UpdateKickButton();
        UpdateReadyState();
    }

    public override void _Process(float delta)
    {
        var info = NetworkManager.Instance.GetPlayerInfo(ID);
        if (info == null)
            return;

        info.Ints.TryGetValue("kills", out int kills);
        info.Ints.TryGetValue("deaths", out int deaths);

        killsLabel.Text = kills.ToString();
        deathsLabel.Text = deaths.ToString();
    }

    private void UpdateName()
    {
        if (nameLabel == null)
            throw new SceneTreeAttachRequired();

        var builder = new StringBuilder(50);

        builder.Append(PlayerName);

        if (ID == NetworkManager.DEFAULT_SERVER_ID)
        {
            builder.Append(' ');
            builder.Append("[color=#fe82ff][host][/color]");
        }

        var network = NetworkManager.Instance;

        var player = network.GetPlayerInfo(ID);
        if (player != null && player.Status != network.PlayerInfo?.Status)
        {
            builder.Append(' ');
            builder.Append($"[{player.GetStatusReadable()}]");
        }

        nameLabel.ExtendedBbcode = builder.ToString();
    }

    private void UpdateKickButton()
    {
        kickButton.Visible = NetworkManager.Instance.IsAuthoritative && ID != NetworkManager.Instance.PeerId;
        spacer.Visible = !kickButton.Visible;
    }

    private void UpdateReadyState()
    {
        var stylebox = GetStylebox("panel").Duplicate(true) as StyleBoxFlat;
        stylebox!.BgColor = Highlight ? new Color(0.07f, 0.51f, 0.84f, 0.39f) : new Color(Colors.Black, 0.39f);
        AddStyleboxOverride("panel", stylebox);
    }

    private void OnPlayerStatusChanged(int peerId, NetPlayerStatus status)
    {
        _ = peerId;
        _ = status;
        UpdateName();
    }

    private void OnKickPressed()
    {
        GUICommon.Instance.PlayButtonPressSound();
        EmitSignal(nameof(KickRequested), ID);
    }
}
