using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NetieSpace.Helpers;
using NetieSpace.Services;

namespace NetieSpace.Views;

public partial class PasswordVaultWindow : Window
{
    private readonly PasswordVaultService _vault = new();
    private string? _editingId;

    public PasswordVaultWindow()
    {
        InitializeComponent();
        GlassHelper.ApplyGlass(this, 0.90);
        RefreshList();
        ClearForm();
    }

    public static void ShowVault()
    {
        var win = new PasswordVaultWindow();
        win.Show();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        AnimationHelper.AnimateOpen(RootBorder, 240);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void RefreshList()
    {
        EntryList.ItemsSource = _vault.List();
        StatusText.Text = $"{EntryList.Items.Count} saved";
    }

    private void ClearForm()
    {
        _editingId = null;
        TitleBox.Text = "";
        UserBox.Text = "";
        PassBox.Text = "";
        UrlBox.Text = "";
        NotesBox.Text = "";
        ApplyPlaceholders();
    }

    private void ApplyPlaceholders()
    {
        // Tag holds placeholder hint; leave empty for glass fields
    }

    private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EntryList.SelectedItem is not VaultEntry entry)
            return;
        _editingId = entry.Id;
        TitleBox.Text = entry.Title;
        UserBox.Text = entry.Username;
        PassBox.Text = entry.Password;
        UrlBox.Text = entry.Url;
        NotesBox.Text = entry.Notes;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        EntryList.SelectedIndex = -1;
        ClearForm();
        StatusText.Text = "New entry";
        TitleBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            StatusText.Text = "Title required";
            return;
        }

        _vault.Upsert(new VaultEntry
        {
            Id = _editingId ?? "",
            Title = TitleBox.Text.Trim(),
            Username = UserBox.Text.Trim(),
            Password = PassBox.Text,
            Url = UrlBox.Text.Trim(),
            Notes = NotesBox.Text.Trim()
        });
        RefreshList();
        StatusText.Text = "Saved · encrypted on this PC";
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_editingId))
        {
            StatusText.Text = "Select an entry first";
            return;
        }
        _vault.Delete(_editingId);
        ClearForm();
        RefreshList();
        StatusText.Text = "Deleted";
    }

    private void CopyPass_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PassBox.Text))
        {
            StatusText.Text = "No password";
            return;
        }
        Clipboard.SetText(PassBox.Text);
        StatusText.Text = "Password copied";
    }
}
