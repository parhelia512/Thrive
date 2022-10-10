using Object = Godot.Object;

public abstract class PlayerInputBase<TStage> : NodeWithInput
    where TStage : Object, IStage
{
    protected bool autoMove;

    /// <summary>
    ///   A reference to the stage is kept to get to the player object and also the cloud spawning.
    /// </summary>
    protected TStage? stage;

    public void Init(TStage containedInStage)
    {
        stage = containedInStage;
    }

    [RunOnKeyDown("g_hold_forward")]
    public void ToggleAutoMove()
    {
        autoMove = !autoMove;
    }
}
