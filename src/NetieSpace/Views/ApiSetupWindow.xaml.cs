using System.Windows;
using System.Windows.Input;
using NetieSpace.Helpers;
using NetieSpace.Services;

namespace NetieSpace.Views;

public partial class ApiSetupWindow : Window
{
    private readonly AiService _ai;
    private readonly Action? _onSaved;

    public ApiSetupWindow(AiService ai, Action? onSaved = null)
    {
        InitializeComponent();
        _ai = ai;
        _onSaved = onSaved;

        var (gemini, groq, openRouter, seaLion) = EnvLoader.ReadUserApiKeys();
        GeminiKeyBox.Text = gemini;
        GroqKeyBox.Text = groq;
        SeaLionKeyBox.Text = seaLion;
        OpenRouterKeyBox.Text = openRouter;
        StatusText.Text = _ai.IsConfigured
            ? $"Active provider: {_ai.ActiveProvider}"
            : "No API key yet — preview still works; local free summary is used.";

        BuyApiButton.IsEnabled = false;
        BuyApiButton.ToolTip = "Currently unavailable";
        GlassHelper.ApplyGlass(this, 0.90);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        AnimationHelper.AnimateOpen(RootBorder, 260);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        EnvLoader.SaveUserApiKeys(
            GroqKeyBox.Text,
            OpenRouterKeyBox.Text,
            GeminiKeyBox.Text,
            SeaLionKeyBox.Text);
        _ai.ReloadProviders();
        StatusText.Text = _ai.IsConfigured
            ? $"Saved. Active provider: {_ai.ActiveProvider}"
            : "Saved. No valid key detected — check the key and try again.";
        _onSaved?.Invoke();
    }

    private void BuyApiButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Buy API is currently unavailable.\n\nPaste a free Google AI Studio, Groq, or SEA-LION key above.",
            "Netie Space",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
