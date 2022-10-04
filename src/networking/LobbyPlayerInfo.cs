using Godot;

public class LobbyPlayerInfo : PanelContainer
{
    [Export]
    public NodePath NamePath = null!;

    [Export]
    public NodePath KickButtonPath = null!;

    private Label? nameLabel;
    private Button kickButton = null!;

    private string playerName = string.Empty;

    private bool ready;

    [Signal]
    public delegate void Kicked(int id);

    public int ID { get; set; }

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

    public bool Current { get; set; }

    public bool Ready
    {
        get => ready;
        set
        {
            if (ready == value)
                return;

            ready = value;
            UpdateReadyState();
        }
    }

    public override void _Ready()
    {
        nameLabel = GetNode<Label>(NamePath);
        kickButton = GetNode<Button>(KickButtonPath);

        UpdateName();
        UpdateKickButton();
        UpdateReadyState();
    }

    private void UpdateName()
    {
        if (nameLabel == null)
            throw new SceneTreeAttachRequired();

        nameLabel.Text = PlayerName;
    }

    private void UpdateKickButton()
    {
        kickButton.Visible = !Current && GetTree().IsNetworkServer();
    }

    private void UpdateReadyState()
    {
        var stylebox = GetStylebox("panel").Duplicate(true) as StyleBoxFlat;
        stylebox!.BgColor = Ready ? new Color(0.07f, 0.51f, 0.84f, 0.39f) : Colors.Black;
        AddStyleboxOverride("panel", stylebox);
    }

    private void OnKickPressed()
    {
        GUICommon.Instance.PlayButtonPressSound();
        EmitSignal(nameof(Kicked), ID);
    }
}
