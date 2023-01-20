using Godot;

public abstract class PlayerInputBase : NodeWithInput
{
    protected bool autoMove;

    /// <summary>
    ///   A reference to the stage is kept to get to the player object and also the cloud spawning.
    /// </summary>
    protected IStage? stage;

    public void Init(IStage containedInStage)
    {
        stage = containedInStage;
    }

    [RunOnKeyDown("g_hold_forward")]
    public void ToggleAutoMove()
    {
        autoMove = !autoMove;
    }
}
