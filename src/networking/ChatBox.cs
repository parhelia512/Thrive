using System.Text;
using Godot;
using Nito.Collections;

public class ChatBox : VBoxContainer
{
    [Export]
    public NodePath MessagesPath = null!;

    [Export]
    public NodePath LineEditPath = null!;

    [Export]
    public NodePath SendButtonPath = null!;

    private CustomRichTextLabel chatDisplay = null!;
    private LineEdit lineEdit = null!;
    private Button sendButton = null!;

    private Deque<string> chatHistory = new();

    private bool controlsHoveredOver;

    [Export]
    public bool ReleaseLineEditFocusAfterMessageSent { get; set; } = true;

    public override void _Ready()
    {
        chatDisplay = GetNode<CustomRichTextLabel>(MessagesPath);
        lineEdit = GetNode<LineEdit>(LineEditPath);
        sendButton = GetNode<Button>(SendButtonPath);

        NetworkManager.Instance.Connect(nameof(NetworkManager.ChatReceived), this, nameof(OnMessageReceived));

        OnMessageChanged(string.Empty);
    }

    public void ClearMessages()
    {
        chatHistory.Clear();
        DisplayChat();
    }

    private void DisplayChat()
    {
        var builder = new StringBuilder(100);

        foreach (var message in chatHistory)
        {
            builder.AppendLine(message);
        }

        chatDisplay.ExtendedBbcode = builder.ToString();
    }

    private void ParseChat(string chat)
    {
        if (chat == "/clear")
        {
            ClearMessages();
        }
        else if (chat.BeginsWith("/updaterate_d "))
        {
            var args = chat.Split(" ");

            if (float.TryParse(args[1], out float result))
            {
                var oldTickRate = NetworkManager.Instance.UpdateRateDelay;
                NetworkManager.Instance.UpdateRateDelay = result;
                NetworkManager.Instance.BroadcastChat($"Update rate delay has been changed to {result} by the host");
            }
        }
        else
        {
            NetworkManager.Instance.BroadcastChat(chat);
        }
    }

    private void OnMessageReceived(string message)
    {
        if (chatHistory.Count > Constants.CHAT_HISTORY_RANGE)
            chatHistory.RemoveFromFront();

        chatHistory.AddToBack(message);
        DisplayChat();
    }

    private void OnMessageEntered(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        ParseChat(message);
        lineEdit.Text = string.Empty;

        if (ReleaseLineEditFocusAfterMessageSent)
            lineEdit.ReleaseFocus();

        OnMessageChanged(lineEdit.Text);
    }

    private void OnMessageChanged(string newText)
    {
        sendButton.Disabled = string.IsNullOrEmpty(newText);
    }

    private void OnSendPressed()
    {
        OnMessageEntered(lineEdit.Text);
    }

    public void OnClickedOffTextBox()
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
