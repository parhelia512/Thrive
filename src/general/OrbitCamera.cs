using Godot;

/// <summary>
///   Camera3D that rotates around a pivot.
/// </summary>
public partial class OrbitCamera : Node3D
{
    [Export]
    public float Distance = 5;

    [Export]
    public float MinCameraDistance = 3.0f;

    [Export]
    public float MaxCameraDistance = 100.0f;

    [Export]
    public float RotationSpeed = 0.6f;

    [Export]
    public float ZoomSpeed = 1.0f;

    [Export]
    public float InterpolateRotationSpeed = 5.0f;

    [Export]
    public float InterpolateZoomSpeed = 5.0f;

#pragma warning disable CA2213
    private Camera3D camera = null!;
#pragma warning restore CA2213

    private Vector3 rotation;
    private Vector2 moveSpeed;

    public override void _EnterTree()
    {
        base._EnterTree();
        InputManager.RegisterReceiver(this);
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        InputManager.UnregisterReceiver(this);
    }

    public override void _Ready()
    {
        camera = GetNode<Camera3D>("Camera3D");
    }

    [RunOnAxis(new[] { "g_zoom_in", "g_zoom_out" }, new[] { -1.0f, 1.0f }, UseDiscreteKeyInputs = true,
        OnlyUnhandled = false)]
    public void Zoom(float delta, float value)
    {
        Distance += ZoomSpeed * value * delta * 165;
    }

    public override void _Process(double delta)
    {
        rotation.X -= moveSpeed.Y * (float)delta * RotationSpeed;
        rotation.Y -= moveSpeed.X * (float)delta * RotationSpeed;
        rotation.X = rotation.X.Clamp(-Mathf.Pi / 2, Mathf.Pi / 2);
        moveSpeed = Vector2.Zero;

        Distance = Distance.Clamp(MinCameraDistance, MaxCameraDistance);

        camera.Position = camera.Position.Lerp(
            new Vector3(0, 0, Distance), InterpolateZoomSpeed * (float)delta);

        var currentRotation = new Quaternion(Transform.Basis);
        var targetRotation = new Quaternion(rotation.Normalized(), 0);
        var smoothRotation = currentRotation.Slerp(targetRotation, InterpolateRotationSpeed * (float)delta);
        Transform = new Transform3D(new Basis(smoothRotation), Position);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            moveSpeed = motion.Relative;
        }
    }
}
