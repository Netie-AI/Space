using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using NetieSpace.Helpers;
using NetieSpace.Services.Licensing;

namespace NetieSpace.Views;

public partial class LoginWindow : Window
{
    private readonly LicenseApiClient _api;
    private bool _busy;

    public bool Entitled { get; private set; }

    public LoginWindow(string apiBaseUrl, string devicePepper)
    {
        InitializeComponent();
        _api = new LicenseApiClient(apiBaseUrl);
        _ = devicePepper;
        GlassHelper.ApplyGlass(this, 0.92);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        AnimationHelper.AnimateOpen(RootBorder, 260);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowPayFirstButtons()
    {
        BuyButton.Visibility = Visibility.Visible;
        ActivateAfterPayButton.Visibility = Visibility.Visible;
    }

    private async void BuyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        BuyButton.IsEnabled = false;
        StatusText.Text = "Starting checkout…";
        try
        {
            var session = LicenseStore.LoadSession();
            var checkout = await _api.CreateCheckoutAsync(session?.Token, session?.Email);
            if (checkout.Success && !string.IsNullOrWhiteSpace(checkout.CheckoutUrl))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = checkout.CheckoutUrl,
                    UseShellExecute = true
                });
                StatusText.Text = "Opened Stripe Checkout. After pay: Activate this laptop.";
                return;
            }

            StatusText.Text = checkout.AlreadyPaid
                ? (checkout.Error ?? "Already paid. Activate this laptop.")
                : (checkout.Error ?? "Checkout is not live yet (stripe_not_configured).");
            if (checkout.AlreadyPaid)
                ShowPayFirstButtons();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Checkout failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            BuyButton.IsEnabled = true;
        }
    }

    private async void ActivateAfterPayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        ActivateAfterPayButton.IsEnabled = false;
        StatusText.Text = "Activating this laptop…";
        try
        {
            var (ok, message, _) = await LicenseActivation.ActivateCurrentSessionAsync();
            StatusText.Text = message;
            if (!ok) return;

            Entitled = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Activate failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            ActivateAfterPayButton.IsEnabled = true;
        }
    }

    private void OfflineButton_Click(object sender, RoutedEventArgs e)
    {
        if (LicenseStore.IsLicensedOffline())
        {
            Entitled = true;
            DialogResult = true;
            Close();
            return;
        }

        StatusText.Text = "No offline license on this PC yet — sign in once to activate.";
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e) =>
        await RunAuthAsync(register: false);

    private async void RegisterButton_Click(object sender, RoutedEventArgs e) =>
        await RunAuthAsync(register: true);

    private async Task RunAuthAsync(bool register)
    {
        if (_busy) return;
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;
        if (email.Length < 3 || password.Length < 8)
        {
            StatusText.Text = "Enter email and a password (8+ characters).";
            return;
        }

        _busy = true;
        SignInButton.IsEnabled = false;
        RegisterButton.IsEnabled = false;
        StatusText.Text = register ? "Creating account…" : "Signing in…";

        try
        {
            var auth = register
                ? await _api.RegisterAsync(email, password)
                : await _api.LoginAsync(email, password);

            if (!auth.Success || string.IsNullOrWhiteSpace(auth.Token))
            {
                StatusText.Text = auth.Error ?? "Auth failed";
                return;
            }

            LicenseStore.SaveSession(auth.Token, auth.Email ?? email);
            StatusText.Text = "Activating this laptop…";

            var (ok, message, payFirst) = await LicenseActivation.ActivateCurrentSessionAsync(
                auth.LicenseKey);
            StatusText.Text = message;
            if (!ok)
            {
                if (payFirst)
                    ShowPayFirstButtons();
                return;
            }

            Entitled = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "LoginWindow");
            StatusText.Text = $"Network error: {ex.Message}";
        }
        finally
        {
            _busy = false;
            SignInButton.IsEnabled = true;
            RegisterButton.IsEnabled = true;
        }
    }
}
