using System.Text;
using Godot;
using Nito.Collections;

public class ChatBox : VBoxContainer
{
    [Export]
    public NodePath MessagesPath = null!;

    [Export]
    public NodePath TextBoxPath = null!;

    [Export]
    public NodePath SendButtonPath = null!;

    private CustomRichTextLabel chatDisplay = null!;
    private LineEdit textBox = null!;
    private Button sendButton = null!;

    private Deque<string> chatHistory = new();

    public override void _Ready()
    {
        chatDisplay = GetNode<CustomRichTextLabel>(MessagesPath);
        textBox = GetNode<LineEdit>(TextBoxPath);
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
        textBox.Text = string.Empty;
        textBox.ReleaseFocus();

        OnMessageChanged(textBox.Text);
    }

    private void OnMessageChanged(string newText)
    {
        sendButton.Disabled = string.IsNullOrEmpty(newText);
    }

    private void OnSendPressed()
    {
        OnMessageEntered(textBox.Text);
    }
}
