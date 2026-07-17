using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NetieSpace.Models;

namespace NetieSpace.Controls;

public partial class ChatPanel : UserControl
{
    private readonly List<ChatMessage> _history = [];
    private string? _stagedSelection;

    public event Action<string>? SendRequested;
    public event Action<string>? LookupRequested;
    public event Action<string>? AddToChatRequested;

    public ChatPanel()
    {
        InitializeComponent();
        AddAssistantMessage("PDF: select text, Ctrl+C, then Look Up / Add to Chat (toolbar or right-click). Other files: highlight → right-click.");
    }

    public IReadOnlyList<ChatMessage> GetHistory() => _history;

    public void StageSelection(string text)
    {
        _stagedSelection = text.Trim();
        SelectionText.Text = $"Selected: \"{_stagedSelection}\"";
        SelectionBanner.Visibility = Visibility.Visible;
        // Do not raise AddToChatRequested here — PreviewWindow wires that back into StageSelection (recurse).
    }

    /// <summary>Stage parsed document text (first-layer extract) before AI send.</summary>
    public void StageDocumentContext(string fileName, string parsedText)
    {
        var preview = parsedText.Length > 400 ? parsedText[..400] + "…" : parsedText;
        _stagedSelection = parsedText;
        SelectionText.Text = $"File: {fileName} ({parsedText.Length:N0} chars parsed)";
        SelectionBanner.Visibility = Visibility.Visible;
        AddUserMessage($"[Attached file · parsed] {fileName}\n\n{preview}");
    }

    public void StageImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !System.IO.File.Exists(imagePath))
            return;

        _stagedSelection = $"[image] {imagePath}";
        SelectionText.Text = $"Image: {System.IO.Path.GetFileName(imagePath)}";
        SelectionBanner.Visibility = Visibility.Visible;

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 28, 12),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 310
        };
        var img = new System.Windows.Controls.Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(imagePath)),
            Stretch = Stretch.Uniform,
            MaxHeight = 160
        };
        border.Child = img;
        MessagesPanel.Children.Add(border);
        MessagesScroll.ScrollToEnd();
        _history.Add(new ChatMessage { Role = "user", Content = $"[Attached image] {imagePath}" });
    }

    public void AddUserMessage(string content)
    {
        _history.Add(new ChatMessage { Role = "user", Content = content });
        AppendBubble(content, isUser: true);
    }

    public void AddAssistantMessage(string content)
    {
        _history.Add(new ChatMessage { Role = "assistant", Content = content });
        AppendBubble(content, isUser: false);
    }

    public void SetBusy(bool busy)
    {
        SendButton.IsEnabled = !busy;
        InputBox.IsEnabled = !busy;
        SendButton.Content = busy ? "..." : "Send";
    }

    public void FocusInput()
    {
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text.Length;
    }

    private void AppendBubble(string content, bool isUser)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(isUser
                ? Color.FromArgb(140, 80, 110, 220)
                : Color.FromArgb(90, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14, 11, 14, 11),
            Margin = new Thickness(isUser ? 28 : 0, 0, isUser ? 0 : 28, 12),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 310
        };

        var text = new TextBlock
        {
            Text = content,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, SF Pro Text, Helvetica Neue"),
            Foreground = new SolidColorBrush(Color.FromRgb(242, 244, 248)),
            FontSize = 13.5,
            LineHeight = 19
        };

        border.Child = text;
        MessagesPanel.Children.Add(border);
        MessagesScroll.ScrollToEnd();
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => Submit();

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            Submit();
        }
    }

    private void Submit()
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        AddUserMessage(text);
        InputBox.Clear();
        SendRequested?.Invoke(text);
    }

    private void AskSelection_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_stagedSelection))
            return;

        LookupRequested?.Invoke(_stagedSelection);
    }
}
