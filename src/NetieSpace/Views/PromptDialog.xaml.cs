using System.Windows;
using NetieSpace.Helpers;

namespace NetieSpace.Views;
public partial class PromptDialog : Window
{
    public string Result { get; private set; } = "";

    public PromptDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        GlassHelper.ApplyRoundedDialog(this, 0.92, 16);
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = defaultValue;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public static string? Ask(Window owner, string title, string prompt, string defaultValue = "")
    {
        var dlg = new PromptDialog(title, prompt, defaultValue) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = InputBox.Text?.Trim() ?? "";
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
