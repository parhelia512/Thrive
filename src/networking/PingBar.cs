using Godot;

/// <summary>
///   A UI element for displaying a peer's network delay (lag).
/// </summary>
public class PingBar : TextureRect
{
    private Texture level1 = null!;
    private Texture level2 = null!;
    private Texture level3 = null!;
    private Texture level4 = null!;

    public override void _Ready()
    {
        level1 = GD.Load<Texture>("res://assets/textures/gui/bevel/pingBar1.png");
        level2 = GD.Load<Texture>("res://assets/textures/gui/bevel/pingBar2.png");
        level3 = GD.Load<Texture>("res://assets/textures/gui/bevel/pingBar3.png");
        level4 = GD.Load<Texture>("res://assets/textures/gui/bevel/pingBar4.png");

        NetworkManager.Instance.Connect(nameof(NetworkManager.LatencyUpdated), this, nameof(UpdateLevel));

        UpdateLevel(NetworkManager.Instance.LocalPlayer?.Latency ?? 0);
    }

    private void UpdateLevel(int miliseconds)
    {
        if (miliseconds >= 0 && miliseconds <= 100)
        {
            Texture = level4;
        }
        else if (miliseconds > 100 && miliseconds <= 150)
        {
            Texture = level3;
        }
        else if (miliseconds > 150 && miliseconds <= 300)
        {
            Texture = level2;
        }
        else if (miliseconds > 300)
        {
            Texture = level1;
        }

        HintTooltip = TranslationServer.Translate("PING_VALUE_MILISECONDS").FormatSafe(miliseconds);
    }
}
