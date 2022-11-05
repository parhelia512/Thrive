using System.Collections.Generic;
using Godot;

public class ArenaMinimap : HBoxContainer
{
    private Panel map = null!;

    private float updateTimer;

    public float MapRadius { get; set; }

    public IReadOnlyList<Vector2>? SpawnCoordinates { get; set; }

    public Vector3? PlayerPosition { get; set; }

    public override void _Ready()
    {
        map = GetNode<Panel>("Map");
    }

    public override void _Process(float delta)
    {
        updateTimer -= delta;

        if (updateTimer <= 0)
        {
            map.Update();
            updateTimer = 1;
        }
    }

    private void OnMapDraw()
    {
        map.DrawSetTransform(map.RectSize * 0.5f, 0, Vector2.One);

        if (PlayerPosition.HasValue)
            DrawPoint(new Vector2(PlayerPosition.Value.x, PlayerPosition.Value.z), 1.5f, Colors.Yellow);

        if (SpawnCoordinates != null)
        {
            foreach (var point in SpawnCoordinates)
                DrawPoint(point, 1.0f, Colors.DarkGray);
        }
    }

    private void DrawPoint(Vector2 position, float size, Color colour)
    {
        map.DrawCircle((position / MapRadius) * (map.RectSize / 2), size, colour);
    }
}
