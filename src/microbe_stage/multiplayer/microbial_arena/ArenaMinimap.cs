using Godot;

public class ArenaMinimap : HBoxContainer
{
    private Panel map = null!;

    private MicrobialArena? arena;

    private float updateTimer;
    private Vector2 mapHalfSize;

    public void Init(MicrobialArena arena)
    {
        this.arena = arena;
    }

    public override void _Ready()
    {
        map = GetNode<Panel>("Map");

        mapHalfSize = map.RectSize / 2;
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
        if (arena == null)
            return;

        map.DrawSetTransform(mapHalfSize, 0, Vector2.One);

/*
        foreach (var entity in arena.DynamicEntities)
        {
            if (entity is INetEntity netEntity)
            {
                var translation = netEntity.EntityNode.GlobalTranslation;
                var player = int.TryParse(netEntity.EntityNode.Name, out int parsed) && parsed == GetTree().GetNetworkUniqueId();
                var size = player ? 1.5f : 1.0f;
                var color = player ? Colors.White : Colors.Gray;

                map.DrawCircle(
                    (new Vector2(translation.x, translation.z) / mapHalfSize) * 3, size, color);
            }
        }
*/
    }
}
