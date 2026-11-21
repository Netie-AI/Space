using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell.Panels.Pdf;

/// <summary>
/// "Preview" on the result card: the finished PDF in the Quick Look peek window (NetieSpace.exe --preview PATH), so
/// the person sees the merged, organized or signed file at once. When the peek cannot start, the default app opens it.
/// </summary>
internal static class PeekLauncher
{
    private const string Tag = "netie-peek-button";

    public static void Decorate(Panel buttons, string? output)
    {
        foreach (var old in buttons.Children.OfType<Button>().Where(b => Equals(b.Tag, Tag)).ToList())
            buttons.Children.Remove(old);
        if (output is null || PreviewPlan.KindOf(output) != PreviewKind.Pdf) return;
        var peek = new Button
        {
            Content = "Preview",
            Tag = Tag,
            ToolTip = "Look at the result in the peek viewer",
            MinHeight = 32,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(peek, "Preview the result in the peek viewer");
        peek.Click += (_, _) => Preview(output);
        buttons.Children.Insert(Math.Min(1, buttons.Children.Count), peek);
    }

    public static void Preview(string path)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) throw new InvalidOperationException("the app path is unknown");
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            psi.ArgumentList.Add("--preview");
            psi.ArgumentList.Add(path);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"peek preview failed ({path}): {ex.Message}; opening with the default app");
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex2) { AppLogger.Warn($"open failed ({path}): {ex2.Message}"); }
        }
    }
}
