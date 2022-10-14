using Godot;
using Object = Godot.Object;

public abstract class MultiplayerStageHUDBase<TStage> : StageHUDBase<TStage>
    where TStage : Object, IStage
{
    [Export]
    public NodePath ChatBoxPath = null!;

    [Export]
    public NodePath ScoreBoardPath = null!;

    protected AnimationPlayer chatBoxAnimationPlayer = null!;

    protected ChatBox chatBox = null!;
    protected ScoreBoard scoreBoard = null!;

    // The values of the following variable is the opposite of the expected value.
    // I.e. its value is true when its respective panel is collapsed.
    private bool chatBoxActive = true;

    public override void _Ready()
    {
        base._Ready();

        chatBoxAnimationPlayer = GetNode<AnimationPlayer>(ChatBoxAnimationPlayerPath);
        chatBox = GetNode<ChatBox>(ChatBoxPath);
        scoreBoard = GetNode<ScoreBoard>(ScoreBoardPath);
    }

    public override void Init(TStage containedInStage)
    {
        base.Init(containedInStage);

        bottomLeftBar.ShowPauseButton = chatBoxActive = !GetTree().HasNetworkPeer();
        bottomLeftBar.ShowChatButton = bottomLeftBar.ChatPressed = chatBox.Visible = GetTree().HasNetworkPeer();
        chatBoxAnimationPlayer.Play(GetTree().HasNetworkPeer() ? "Open" : "Close");
    }

    public void ToggleScoreBoard()
    {
        if (GetTree().HasNetworkPeer())
            scoreBoard.Visible = !scoreBoard.Visible;
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
