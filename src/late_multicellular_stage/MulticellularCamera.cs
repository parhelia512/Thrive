using Godot;
using Newtonsoft.Json;

/// <summary>
///   Camera3D for handling a 3rd person camera in 3D space
///   TODO: implement the camera rotating logic
/// </summary>
public partial class MulticellularCamera : Node3D, IGodotEarlyNodeResolve
{
#pragma warning disable CA2213
    private Camera3D camera = null!;
    private AudioListener3D listener = null!;
    private Node3D offsetNode = null!;

    private SpringArm3D? arm;
#pragma warning restore CA2213

    private float armLength = 8;

    [JsonProperty]
    private float xRotation;

    [JsonProperty]
    private float yRotation;

    [Export]
    public float MinXRotation { get; set; } = -(float)MathUtils.FULL_CIRCLE * 0.25f;

    [Export]
    public float MaxXRotation { get; set; } = (float)MathUtils.FULL_CIRCLE * 0.25f;

    [Export]
    [JsonProperty]
    public float ArmLength
    {
        get => armLength;
        set
        {
            armLength = value;
            ApplyArmLength();
        }
    }

    /// <summary>
    ///   How fast the camera zooming is for controlling <see cref="ArmLength"/>
    /// </summary>
    [Export]
    [JsonProperty]
    public float ZoomSpeed { get; set; } = 1.4f;

    /// <summary>
    ///   The height at which the camera starts at
    /// </summary>
    [Export]
    [JsonProperty]
    public float MinArmLength { get; set; } = 1.0f;

    /// <summary>
    ///   Min height the camera can be scrolled to
    /// </summary>
    [Export]
    [JsonProperty]
    public float MaxArmLength { get; set; } = 14;

    [Export]
    [JsonProperty]
    public Vector3 FollowOffset { get; set; } = new(1.2f, 1.5f, 0.5f);

    [Export]
    [JsonProperty]
    public bool Current
    {
        get => camera.Current;
        set
        {
            camera.Current = value;

            if (value)
            {
                listener.MakeCurrent();
            }
            else if (!value && listener.IsCurrent())
            {
                listener.ClearCurrent();
            }
        }
    }

    /// <summary>
    ///   The pitch angle of the camera (in radians)
    /// </summary>
    [JsonIgnore]
    public float XRotation
    {
        get => xRotation;
        set => xRotation = Mathf.Clamp(value, MinXRotation, MaxXRotation);
    }

    /// <summary>
    ///   The yaw angle of the camera (in radians)
    /// </summary>
    [JsonIgnore]
    public float YRotation
    {
        get => yRotation;
        set => yRotation = value % (float)MathUtils.FULL_CIRCLE;
    }

    [JsonIgnore]
    public bool NodeReferencesResolved { get; set; }

    public Node3D? FollowedNode { get; set; }

    public override void _Ready()
    {
        ResolveNodeReferences();

        ApplyArmLength();

        // Apply initial position
        _PhysicsProcess(0);
    }

    public void ResolveNodeReferences()
    {
        if (NodeReferencesResolved)
            return;

        camera = GetNode<Camera3D>("CameraPosition/SpringArm3D/Camera3D");
        listener = GetNode<AudioListener3D>("CameraPosition/SpringArm3D/Camera3D/AudioListener3D");

        offsetNode = GetNode<Node3D>("CameraPosition");
        arm = GetNode<SpringArm3D>("CameraPosition/SpringArm3D");

        NodeReferencesResolved = true;
    }

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

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        if (FollowedNode == null)
            return;

        // Move ourselves (the base node) to be on top of the target, our offset node will then position the camera
        // correctly
        Transform = new Transform3D(Basis.Identity, FollowedNode.Position);

        // Horizontal (yaw) rotation along the y axis is applied to the offset node to make things work nicer
        var up = new Vector3(0, 1, 0);
        var yQuaternion = new Quaternion(up, YRotation);
        offsetNode.Position = yQuaternion * FollowOffset;

        var right = new Vector3(1, 0, 0);

        // Some part of Y-rotation is also applied here to make for non-janky camera turning effect
        var rotation = yQuaternion * new Quaternion(right, XRotation);

        arm!.Transform = new Transform3D(new Basis(rotation), Vector3.Zero);
    }

    [RunOnAxis(new[] { "g_zoom_in", "g_zoom_out" }, new[] { -1.0f, 1.0f }, UseDiscreteKeyInputs = true, Priority = -1)]
    public bool Zoom(float delta, float value)
    {
        if (!Current)
            return false;

        ArmLength = Mathf.Clamp(ArmLength + ZoomSpeed * value, MinArmLength, MaxArmLength);
        return true;
    }

    private void ApplyArmLength()
    {
        if (arm != null)
            arm.SpringLength = armLength;
    }
}
