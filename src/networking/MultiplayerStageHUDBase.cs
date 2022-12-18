using Godot;
using Object = Godot.Object;

public abstract class MultiplayerStageHUDBase<TStage> : StageHUDBase<TStage>
    where TStage : Object, IStage
{
    [Export]
    public NodePath ChatBoxPath = null!;

    [Export]
    public NodePath InfoScreenPath = null!;

    [Export]
    public NodePath ScoreBoardPath = null!;

    protected AnimationPlayer chatBoxAnimationPlayer = null!;

    protected ChatBox chatBox = null!;
    protected Control infoScreen = null!;
    protected NetPlayerList scoreBoard = null!;

    // The values of the following variable is the opposite of the expected value.
    // I.e. its value is true when its respective panel is collapsed.
    private bool chatBoxActive = true;

    public override void _Ready()
    {
        base._Ready();

        chatBoxAnimationPlayer = GetNode<AnimationPlayer>(ChatBoxAnimationPlayerPath);
        chatBox = GetNode<ChatBox>(ChatBoxPath);
        infoScreen = GetNode<Control>(InfoScreenPath);
        scoreBoard = GetNode<NetPlayerList>(ScoreBoardPath);
    }

    public override void Init(TStage containedInStage)
    {
        base.Init(containedInStage);

        ChatButtonPressed(true);
    }

    public virtual void ToggleInfoScreen()
    {
        infoScreen.Visible = !infoScreen.Visible;
    }

    public void SortScoreBoard()
    {
        scoreBoard.SortHighestScoreFirst();
    }

    public NetPlayerLog GetFirstOnTheScoreBoard()
    {
        return scoreBoard.GetFirst();
    }

    public void OnChatFocused()
    {
        bottomLeftBar.ChatPressed = true;
        ChatButtonPressed(true);
    }

    private void ChatButtonPressed(bool wantedState)
    {
        if (chatBoxActive == !wantedState)
            return;

        if (!chatBoxActive)
        {
            chatBoxActive = true;
            chatBoxAnimationPlayer.Play("Close");
        }
        else
        {
            chatBoxActive = false;
            chatBoxAnimationPlayer.Play("Open");
        }
    }
}
