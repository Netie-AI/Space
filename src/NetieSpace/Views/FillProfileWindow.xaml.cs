using System.Windows;
using System.Windows.Controls;
using NetieSpace.Helpers;
using NetieSpace.Services;

namespace NetieSpace.Views;

public partial class FillProfileWindow : Window
{
    public FillProfileWindow()
    {
        InitializeComponent();
        GlassHelper.ApplyRoundedDialog(this, 0.92, 16);
        var cur = FillProfileStore.Load();
        NameBox.Text = cur.FullName;
        EmailBox.Text = cur.Email;
        PhoneBox.Text = cur.Phone;
        AddressBox.Text = cur.Address;
        DobBox.Text = cur.DateOfBirth;
        NationalityBox.Text = cur.Nationality;
        SelectFormat(string.IsNullOrWhiteSpace(cur.DateFormat) ? "dd-MMM-yyyy" : cur.DateFormat);
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public static bool ShowDialogFor(Window? owner)
    {
        var dlg = new FillProfileWindow();
        if (owner is not null)
            dlg.Owner = owner;
        return dlg.ShowDialog() == true;
    }

    public static void ShowStandalone()
    {
        var win = new FillProfileWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
        win.ShowDialog();
    }

    private void SelectFormat(string tag)
    {
        foreach (var item in DateFormatBox.Items)
        {
            if (item is ComboBoxItem cbi && string.Equals(cbi.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                DateFormatBox.SelectedItem = cbi;
                return;
            }
        }
        DateFormatBox.SelectedIndex = 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var fmt = (DateFormatBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "dd-MMM-yyyy";
        FillProfileStore.Save(new FillProfileStore.Profile(
            NameBox.Text ?? "",
            EmailBox.Text ?? "",
            PhoneBox.Text ?? "",
            AddressBox.Text ?? "",
            DobBox.Text ?? "",
            NationalityBox.Text ?? "",
            fmt,
            DateTime.Now.ToString("dd/MM/yyyy")));
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
