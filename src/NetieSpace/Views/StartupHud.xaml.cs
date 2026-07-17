using System.Windows;
using NetieSpace.Helpers;

namespace NetieSpace.Views;

public partial class StartupHud : Window
{
    public StartupHud(string provider)
    {
        InitializeComponent();
        AppIconHelper.ApplyWindowIcon(this);
        ProviderText.Text = $"AI · {provider}";
        GlassHelper.ApplyRoundedDialog(this, 0.92, 14);

        Left = SystemParameters.WorkArea.Width - Width - 24;
        Top = SystemParameters.WorkArea.Height - Height - 24;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AnimationHelper.AnimateOpen(Root, 280);
        await Task.Delay(2800);
        await AnimationHelper.AnimateCloseAsync(Root, 200);
        Close();
    }
}
