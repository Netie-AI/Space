using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using NetieSpace.Helpers;
using NetieSpace.Services;
using NetieSpace.Services.Licensing;

namespace NetieSpace.Views;

public partial class ApiSetupWindow : Window
{
    private readonly AiService _ai;
    private readonly Action? _onSaved;

    // In-memory copies for delete/add without ever showing full secrets in TextBoxes
    private string _gemini = "";
    private string _groq = "";
    private string _seaLion = "";
    private string _openRouter = "";

    public ApiSetupWindow(AiService ai, Action? onSaved = null)
    {
        InitializeComponent();
        _ai = ai;
        _onSaved = onSaved;

        ReloadMaskedFromEnv();
        RefreshStatus();

        LicenseStatusText.Text = LicenseStore.IsLicensedOffline()
            ? "This device is licensed. You’re on the Netie path — BYO keys below are optional until Ult ships."
            : "Not licensed yet on this device. Get a Netie plan first for the full product; keys below are the free FreeRoute/BYO path.";

        GlassHelper.ApplyGlass(this, 0.90);
        _ = RefreshVaultStatusAsync();
        _ = RefreshStripeStatusAsync();
    }

    private async System.Threading.Tasks.Task RefreshStripeStatusAsync()
    {
        try
        {
            var api = new LicenseApiClient(LicenseActivation.ResolveApiBase());
            var st = await api.GetStripeStatusAsync().ConfigureAwait(true);
            if (!st.Known)
                return;
            LicenseStatusText.Text = (LicenseStatusText.Text ?? "") +
                (st.StripeConfigured
                    ? " Worker Stripe is configured -- Get Netie subscription should open Checkout."
                    : " Worker Stripe secrets are not set yet (stripe_not_configured). See docs/HUMAN_STRIPE_CHECKOUT.md.");
        }
        catch
        {
            // Probe must never block Setup.
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        AppleMotion.Materialize(RootBorder, AppleMotion.ResponseMs);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ReloadMaskedFromEnv()
    {
        var keys = EnvLoader.ReadUserApiKeys();
        _gemini = keys.Gemini?.Trim() ?? "";
        _groq = keys.Groq?.Trim() ?? "";
        _seaLion = keys.SeaLion?.Trim() ?? "";
        _openRouter = keys.OpenRouter?.Trim() ?? "";
        ApplyMaskUi();
    }

    private void ApplyMaskUi()
    {
        SetSlotUi(GeminiMaskText, GeminiDeleteBtn, GeminiAddBtn, GeminiAddRow, _gemini);
        SetSlotUi(GroqMaskText, GroqDeleteBtn, GroqAddBtn, GroqAddRow, _groq);
        SetSlotUi(SeaLionMaskText, SeaLionDeleteBtn, SeaLionAddBtn, SeaLionAddRow, _seaLion);
        SetSlotUi(OpenRouterMaskText, OpenRouterDeleteBtn, OpenRouterAddBtn, OpenRouterAddRow, _openRouter);
    }

    private static void SetSlotUi(
        System.Windows.Controls.TextBlock mask,
        System.Windows.Controls.Button deleteBtn,
        System.Windows.Controls.Button addBtn,
        System.Windows.Controls.DockPanel addRow,
        string value)
    {
        var has = !string.IsNullOrWhiteSpace(value);
        mask.Text = has ? EnvLoader.MaskSecret(value) : "Not set";
        mask.Foreground = has
            ? (System.Windows.Media.Brush)Application.Current.FindResource("TextBrush")
            : (System.Windows.Media.Brush)Application.Current.FindResource("MutedBrush");
        deleteBtn.IsEnabled = has;
        addBtn.IsEnabled = true;
        addBtn.Content = has ? "Replace" : "Add new";
    }

    private void PersistKeys()
    {
        EnvLoader.SaveUserApiKeys(_groq, _openRouter, _gemini, _seaLion);
        _ai.ReloadProviders();
        ApplyMaskUi();
        RefreshStatus();
        _onSaved?.Invoke();
    }

    private void RefreshStatus()
    {
        StatusText.Text = _ai.IsConfigured
            ? $"Active provider: {_ai.ActiveProvider}"
            : "No BYO key yet — preview + notes still work.";
    }

    private async Task RefreshVaultStatusAsync()
    {
        try
        {
            VaultStatusText.Text = "Checking OpenVault…";
            var ok = await OpenVaultKeySync.IsHealthyAsync().ConfigureAwait(true);
            VaultStatusText.Text = ok
                ? $"OpenVault online · {OpenVaultKeySync.OpenVaultBaseUrl}"
                : $"OpenVault offline ({OpenVaultKeySync.OpenVaultBaseUrl}) — using local masked cache only.";
        }
        catch (Exception ex)
        {
            VaultStatusText.Text = $"OpenVault check failed: {ex.Message}";
        }
    }

    // ── Per-slot delete / add ─────────────────────────────────────────────────

    private void GeminiDelete_Click(object sender, RoutedEventArgs e)
    {
        _gemini = "";
        GeminiAddRow.Visibility = Visibility.Collapsed;
        GeminiNewBox.Password = "";
        PersistKeys();
        StatusText.Text = "Gemini key deleted.";
    }

    private void GeminiAdd_Click(object sender, RoutedEventArgs e)
    {
        GeminiAddRow.Visibility = Visibility.Visible;
        GeminiNewBox.Password = "";
        GeminiNewBox.Focus();
    }

    private void GeminiSaveNew_Click(object sender, RoutedEventArgs e)
    {
        var v = GeminiNewBox.Password?.Trim() ?? "";
        if (v.Length < 8)
        {
            StatusText.Text = "Paste a full Gemini key, then Save.";
            return;
        }
        _gemini = v;
        GeminiNewBox.Password = "";
        GeminiAddRow.Visibility = Visibility.Collapsed;
        PersistKeys();
        StatusText.Text = "Gemini key saved (masked).";
    }

    private void GroqDelete_Click(object sender, RoutedEventArgs e)
    {
        _groq = "";
        GroqAddRow.Visibility = Visibility.Collapsed;
        GroqNewBox.Password = "";
        PersistKeys();
        StatusText.Text = "Groq key deleted.";
    }

    private void GroqAdd_Click(object sender, RoutedEventArgs e)
    {
        GroqAddRow.Visibility = Visibility.Visible;
        GroqNewBox.Password = "";
        GroqNewBox.Focus();
    }

    private void GroqSaveNew_Click(object sender, RoutedEventArgs e)
    {
        var v = GroqNewBox.Password?.Trim() ?? "";
        if (v.Length < 8)
        {
            StatusText.Text = "Paste a full Groq key, then Save.";
            return;
        }
        _groq = v;
        GroqNewBox.Password = "";
        GroqAddRow.Visibility = Visibility.Collapsed;
        PersistKeys();
        StatusText.Text = "Groq key saved (masked).";
    }

    private void SeaLionDelete_Click(object sender, RoutedEventArgs e)
    {
        _seaLion = "";
        SeaLionAddRow.Visibility = Visibility.Collapsed;
        SeaLionNewBox.Password = "";
        PersistKeys();
        StatusText.Text = "SEA-LION key deleted.";
    }

    private void SeaLionAdd_Click(object sender, RoutedEventArgs e)
    {
        SeaLionAddRow.Visibility = Visibility.Visible;
        SeaLionNewBox.Password = "";
        SeaLionNewBox.Focus();
    }

    private void SeaLionSaveNew_Click(object sender, RoutedEventArgs e)
    {
        var v = SeaLionNewBox.Password?.Trim() ?? "";
        if (v.Length < 8)
        {
            StatusText.Text = "Paste a full SEA-LION key, then Save.";
            return;
        }
        _seaLion = v;
        SeaLionNewBox.Password = "";
        SeaLionAddRow.Visibility = Visibility.Collapsed;
        PersistKeys();
        StatusText.Text = "SEA-LION key saved (masked).";
    }

    private void OpenRouterDelete_Click(object sender, RoutedEventArgs e)
    {
        _openRouter = "";
        OpenRouterAddRow.Visibility = Visibility.Collapsed;
        OpenRouterNewBox.Password = "";
        PersistKeys();
        StatusText.Text = "OpenRouter key deleted.";
    }

    private void OpenRouterAdd_Click(object sender, RoutedEventArgs e)
    {
        OpenRouterAddRow.Visibility = Visibility.Visible;
        OpenRouterNewBox.Password = "";
        OpenRouterNewBox.Focus();
    }

    private void OpenRouterSaveNew_Click(object sender, RoutedEventArgs e)
    {
        var v = OpenRouterNewBox.Password?.Trim() ?? "";
        if (v.Length < 8)
        {
            StatusText.Text = "Paste a full OpenRouter key, then Save.";
            return;
        }
        _openRouter = v;
        OpenRouterNewBox.Password = "";
        OpenRouterAddRow.Visibility = Visibility.Collapsed;
        PersistKeys();
        StatusText.Text = "OpenRouter key saved (masked).";
    }

    private async void BuyNetieButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Starting checkout…";
        try
        {
            var session = LicenseStore.LoadSession();
            var api = new LicenseApiClient(LicenseActivation.ResolveApiBase());
            var checkout = await api.CreateCheckoutAsync(session?.Token, session?.Email).ConfigureAwait(true);
            if (checkout.Success && !string.IsNullOrWhiteSpace(checkout.CheckoutUrl))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = checkout.CheckoutUrl,
                    UseShellExecute = true
                });
                StatusText.Text = "Opened Stripe Checkout. After pay: Setup -> Activate this laptop.";
                return;
            }

            if (checkout.AlreadyPaid)
            {
                StatusText.Text = checkout.Error ?? "Already paid. Activate this laptop.";
                return;
            }

            OpenBuyFallback(checkout.Error);
        }
        catch (Exception ex)
        {
            OpenBuyFallback(ex.Message);
        }
    }

    private async void ActivateLaptopButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Activating this laptop…";
        try
        {
            var (ok, message, _) = await LicenseActivation.ActivateCurrentSessionAsync().ConfigureAwait(true);
            StatusText.Text = message;
            LicenseStatusText.Text = ok
                ? "This device is licensed. You’re on the Netie path — BYO keys below are optional until Ult ships."
                : message;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void ManageBillingButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Opening billing portal…";
        try
        {
            var session = LicenseStore.LoadSession();
            if (string.IsNullOrWhiteSpace(session?.Token))
            {
                StatusText.Text = "Sign in first (tray -> Account), then Manage billing.";
                return;
            }

            var api = new LicenseApiClient(LicenseActivation.ResolveApiBase());
            var portal = await api.CreatePortalAsync(session.Token).ConfigureAwait(true);
            if (portal.Success && !string.IsNullOrWhiteSpace(portal.PortalUrl))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = portal.PortalUrl,
                    UseShellExecute = true
                });
                StatusText.Text = "Opened Stripe Customer Portal.";
                return;
            }

            StatusText.Text = portal.Error ?? "Billing portal unavailable. Pay once first.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void OpenBuyFallback(string? reason)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://netie.space",
                UseShellExecute = true
            });
        }
        catch { /* ignore */ }

        var detail = string.IsNullOrWhiteSpace(reason)
            ? "Checkout is not live yet (stripe_not_configured)."
            : reason;
        StatusText.Text = detail;
        MessageBox.Show(
            detail + "\n\n" +
            "1. Tray -> Account / Sign in (register if new).\n" +
            "2. Setup -> Get Netie subscription again.\n" +
            "3. If still blocked: set Worker secrets in docs/HUMAN_STRIPE_CHECKOUT.md.\n" +
            "4. After pay: tray -> Account -> Activate.\n\n" +
            "Video Export clicks (Playwright cannot open this WPF app): docs/HUMAN_PILLAR1_TEST.md.",
            "Netie subscription",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OpenGeminiSite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://aistudio.google.com/apikey",
                UseShellExecute = true
            });
            StatusText.Text = "Opened Google AI Studio — create a key, then Add new under Gemini.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void ConnectOpenVault_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Connecting to OpenVault…";
        VaultStatusText.Text = "Syncing…";
        try
        {
            var result = await OpenVaultKeySync.SyncApiKeysAsync().ConfigureAwait(true);
            VaultStatusText.Text = result.Message;
            ReloadMaskedFromEnv();
            StatusText.Text = result.Synced
                ? $"Synced {result.KeysPulled} key(s) from OpenVault (masked)."
                : result.Message;
            _onSaved?.Invoke();
        }
        catch (Exception ex)
        {
            VaultStatusText.Text = ex.Message;
            StatusText.Text = $"OpenVault: {ex.Message}";
        }
    }

    private void OpenLocalVault_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PasswordVaultWindow.ShowVault();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Local vault: {ex.Message}";
        }
    }
}
