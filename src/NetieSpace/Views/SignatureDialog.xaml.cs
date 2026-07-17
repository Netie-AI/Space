using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NetieSpace.Helpers;
using NetieSpace.Services;

namespace NetieSpace.Views;

public enum SignatureDialogAction
{
    Cancel,
    Stamp,
    SavePng
}

public partial class SignatureDialog : Window
{
    public SignatureDialogAction Action { get; private set; } = SignatureDialogAction.Cancel;
    public string SignatureName { get; private set; } = "";
    public string FontFamilyName { get; private set; } = "Segoe UI";
    public string? SavedPngPath { get; private set; }

    public SignatureDialog()
    {
        InitializeComponent();
        GlassHelper.ApplyRoundedDialog(this, 0.92, 18);

        var fonts = PdfEditService.GetAvailableSignatureFonts();
        FontList.ItemsSource = fonts;
        FontList.SelectedIndex = 0;
        FontFamilyName = fonts[0];
        ApplyPreviewFont();
    }

    public static SignatureDialog? Show(Window owner, string? defaultName = null)
    {
        var dlg = new SignatureDialog { Owner = owner };
        if (!string.IsNullOrWhiteSpace(defaultName))
            dlg.NameBox.Text = defaultName;
        return dlg.ShowDialog() == true ? dlg : null;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Action = SignatureDialogAction.Cancel;
        DialogResult = false;
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void FontList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontList.SelectedItem is string font)
            FontFamilyName = font;
        ApplyPreviewFont();
        UpdatePreview();
    }

    private void ApplyPreviewFont()
    {
        try
        {
            PreviewText.FontFamily = new FontFamily(FontFamilyName);
        }
        catch
        {
            PreviewText.FontFamily = new FontFamily("Segoe UI");
        }
    }

    private void UpdatePreview()
    {
        var name = NameBox.Text?.Trim() ?? "";
        PreviewText.Text = string.IsNullOrEmpty(name) ? "Your name" : name;
        PreviewText.Opacity = string.IsNullOrEmpty(name) ? 0.35 : 1.0;
    }

    private bool CaptureName()
    {
        SignatureName = NameBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(SignatureName))
        {
            MessageBox.Show(this, "Type a name to sign.", "Sign", MessageBoxButton.OK, MessageBoxImage.Information);
            NameBox.Focus();
            return false;
        }

        if (FontList.SelectedItem is string font)
            FontFamilyName = font;
        return true;
    }

    private void StampButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CaptureName())
            return;
        Action = SignatureDialogAction.Stamp;
        DialogResult = true;
    }

    private void SavePngButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CaptureName())
            return;

        var safe = string.Concat(SignatureName.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '_'))
            .Trim();
        if (string.IsNullOrWhiteSpace(safe))
            safe = "signature";

        var dlg = new SaveFileDialog
        {
            Title = "Save signature PNG",
            Filter = "PNG image|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = $"{safe}_signature.png",
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            PdfEditService.RenderTypedSignaturePng(SignatureName, FontFamilyName, dlg.FileName);
            SavedPngPath = dlg.FileName;
            Action = SignatureDialogAction.SavePng;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save signature PNG", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
