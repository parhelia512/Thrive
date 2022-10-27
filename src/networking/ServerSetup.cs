using System.IO;
using Godot;
using Newtonsoft.Json;

public class ServerSetup : CustomDialog
{
    [Export]
    public NodePath NamePath = null!;

    [Export]
    public NodePath MaxPlayerPath = null!;

    [Export]
    public NodePath GameModePath = null!;

    [Export]
    public NodePath UseUPNPPath = null!;

    private LineEdit name = null!;
    private SpinBox maxPlayers = null!;
    private OptionButton gameMode = null!;
    private CustomCheckBox useUPNP = null!;

    private ServerSettings? settings;
    private string playerName = "unnamed";
    private string address = string.Empty;
    private int port;

    [Signal]
    public delegate void Confirmed(string settings);

    public override void _Ready()
    {
        name = GetNode<LineEdit>(NamePath);
        maxPlayers = GetNode<SpinBox>(MaxPlayerPath);
        gameMode = GetNode<OptionButton>(GameModePath);
        useUPNP = GetNode<CustomCheckBox>(UseUPNPPath);
    }

    public void Open(string playerName, string address, int port)
    {
        this.playerName = playerName;
        this.address = address;
        this.port = port;

        ResetForm();

        this.PopupCenteredShrink();
    }

    private void Cancel()
    {
        GUICommon.Instance.PlayButtonPressSound();
        Hide();
    }

    private void Confirm()
    {
        GUICommon.Instance.PlayButtonPressSound();

        if (settings == null)
        {
            GD.PrintErr("Server setup confirmed with settings being null");
            return;
        }

        ReadControlsToSettings();

        var serialized = new StringWriter();
        JsonSerializer.Create().Serialize(serialized, settings);

        Hide();

        EmitSignal(nameof(Confirmed), serialized.ToString());
    }

    private void ResetForm()
    {
        settings = new ServerSettings
        {
            Name = $"{playerName}'s server",
            Address = address,
            Port = port,
            MaxPlayers = Constants.MULTIPLAYER_DEFAULT_MAX_PLAYERS,
            UseUPNP = false,
        };

        ApplySettingsToControls();
    }

    private void ApplySettingsToControls()
    {
        name.Text = settings!.Name;
        maxPlayers.Value = settings.MaxPlayers;
        gameMode.Selected = (int)settings.SelectedGameMode;
        useUPNP.Pressed = settings.UseUPNP;
    }

    private void ReadControlsToSettings()
    {
        settings!.Name = name.Text;
        settings.MaxPlayers = (int)maxPlayers.Value;
        settings.SelectedGameMode = (MultiplayerGameState)gameMode.Selected;
        settings.UseUPNP = useUPNP.Pressed;
    }
}
