using System.Windows;
using System.Windows.Input;
using NetieSpace.Helpers;
using NetieSpace.Services.Licensing;

namespace NetieSpace.Views;

public partial class LoginWindow : Window
{
    private readonly LicenseApiClient _api;
    private readonly string _pepper;
    private bool _busy;

    public bool Entitled { get; private set; }

    public LoginWindow(string apiBaseUrl, string devicePepper)
    {
        InitializeComponent();
        _api = new LicenseApiClient(apiBaseUrl);
        _pepper = devicePepper;
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

            var deviceHash = DeviceFingerprint.ComputeHash(_pepper);
            var activate = await _api.ActivateAsync(
                auth.Token,
                deviceHash,
                DeviceFingerprint.DeviceLabel(),
                auth.LicenseKey);

            if (!activate.Success || string.IsNullOrWhiteSpace(activate.EntitlementToken))
            {
                StatusText.Text = activate.Error ?? "Activate failed";
                return;
            }

            LicenseStore.SaveEntitlement(new LicenseEntitlement
            {
                Email = auth.Email ?? email,
                LicenseKey = auth.LicenseKey ?? "",
                LicenseId = activate.Entitlement is not null && activate.Entitlement.TryGetValue("license_id", out var lid)
                    ? lid.GetString() ?? ""
                    : "",
                DeviceHash = deviceHash,
                ActivatedAt = DateTime.UtcNow.ToString("O"),
                EntitlementToken = activate.EntitlementToken,
                PepperHint = _pepper
            });

            Entitled = true;
            StatusText.Text = "Activated — you can use Netie offline (except AI / re-login).";
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
