using System.Text;
using Godot;

public class ChatBox : VBoxContainer
{
    [Export]
    public NodePath MessagesPath = null!;

    [Export]
    public NodePath LineEditPath = null!;

    [Export]
    public NodePath SendButtonPath = null!;

    protected LineEdit lineEdit = null!;

    private CustomRichTextLabel chatDisplay = null!;
    private Button sendButton = null!;

    private bool controlsHoveredOver;

    [Signal]
    public delegate void Focused();

    [Export]
    public bool ReleaseLineEditFocusAfterMessageSent { get; set; } = true;

    [Export]
    public bool CaptureFocusWhenInvisible { get; set; } = true;

    public override void _EnterTree()
    {
        InputManager.RegisterReceiver(this);
        base._EnterTree();
    }

    public override void _ExitTree()
    {
        InputManager.UnregisterReceiver(this);
        base._ExitTree();
    }

    public override void _Ready()
    {
        chatDisplay = GetNode<CustomRichTextLabel>(MessagesPath);
        lineEdit = GetNode<LineEdit>(LineEditPath);
        sendButton = GetNode<Button>(SendButtonPath);

        NetworkManager.Instance.Connect(nameof(NetworkManager.ChatReceived), this, nameof(OnMessageReceived));

        OnMessageChanged(string.Empty);
        DisplayChat();
    }

    [RunOnKeyDown("g_focus_chat")]
    public bool Focus()
    {
        if (!CaptureFocusWhenInvisible && !Visible)
            return false;

        lineEdit.GrabFocus();
        return true;
    }

    protected virtual void OnFocusEntered()
    {
        EmitSignal(nameof(Focused));
    }

    protected virtual void OnMessageEntered(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        ParseChat(message);
        lineEdit.Text = string.Empty;

        if (ReleaseLineEditFocusAfterMessageSent)
            lineEdit.ReleaseFocus();

        OnMessageChanged(lineEdit.Text);
    }

    protected virtual void OnMessageChanged(string newText)
    {
        sendButton.Disabled = string.IsNullOrEmpty(newText);
    }

    private void DisplayChat()
    {
        var builder = new StringBuilder(100);

        foreach (var message in NetworkManager.Instance.ChatHistory)
        {
            builder.AppendLine(message);
        }

        chatDisplay.ExtendedBbcode = builder.ToString();
    }

    private void ParseChat(string chat)
    {
        if (chat == "/clear")
        {
            NetworkManager.Instance.ClearChatHistory();
            DisplayChat();
        }
        else if (chat.BeginsWith("/updateintv "))
        {
            var args = chat.Split(" ");

            if (float.TryParse(args[1], out float result))
            {
                var oldTickRate = NetworkManager.Instance.UpdateInterval;
                NetworkManager.Instance.UpdateInterval = result;
                NetworkManager.Instance.BroadcastChat($"Update interval has been changed to {result} by the host");
            }
        }
        else
        {
            NetworkManager.Instance.BroadcastChat(chat);
        }
    }

    private void OnMessageReceived()
    {
        DisplayChat();
    }

    private void OnSendPressed()
    {
        OnMessageEntered(lineEdit.Text);
    }

    private void OnClickedOffTextBox()
    {
        var focused = GetFocusOwner();

        // Ignore if the species name line edit wasn't focused or if one of our controls is hovered
        if (focused != lineEdit || controlsHoveredOver)
            return;

        lineEdit.ReleaseFocus();
    }

    private void OnControlMouseEntered()
    {
        controlsHoveredOver = true;
    }

    private void OnControlMouseExited()
    {
        controlsHoveredOver = false;
    }
}
