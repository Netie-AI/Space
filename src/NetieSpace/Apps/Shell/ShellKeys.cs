namespace NetieSpace.Apps.Shell;

/// <summary>What a shell-wide key chord asks for.</summary>
public enum ShellCommand { None, Palette, OpenFile, Back, Help }

/// <summary>The keyboard map of the app window, as data so it is tested and so the help sheet cannot disagree with it.</summary>
public static class ShellKeys
{
    /// <param name="key">The WPF <c>Key</c> name: "K", "P", "O", "Left", "F1".</param>
    public static ShellCommand Resolve(string key, bool ctrl, bool alt, bool shift)
    {
        if (shift)
            return ShellCommand.None;
        if (ctrl && !alt)
            return key switch
            {
                "K" or "P" => ShellCommand.Palette,
                "O" => ShellCommand.OpenFile,
                _ => ShellCommand.None,
            };
        if (alt && !ctrl)
            return key is "Left" or "BrowserBack" ? ShellCommand.Back : ShellCommand.None;
        if (!ctrl && !alt)
            return key == "F1" ? ShellCommand.Help : ShellCommand.None;
        return ShellCommand.None;
    }

    /// <summary>The help sheet (F1): one line per chord, in the order they are shown.</summary>
    public static IReadOnlyList<(string Chord, string What)> HelpLines { get; } = new[]
    {
        ("Ctrl+K or Ctrl+P", "Search every tool and recent file. Type 2 or 3 letters, Enter runs it."),
        ("Ctrl+O", "Open a file and see the tools that suit it"),
        ("Alt+Left", "Go back to all tools"),
        ("Tab / Shift+Tab", "Move between buttons and fields"),
        ("Up / Down", "Move in the search list; Enter opens, Esc closes"),
        ("F1", "Show this help"),
        ("Drop a file anywhere", "Netie shows the tools that suit it"),
    };
}
