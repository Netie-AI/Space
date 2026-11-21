using NetieSpace.Apps.Md;

namespace NetieSpace.Tests;

/// <summary>Netie MD: Back and Forward, the colour toggle, its remembered choice and the headless arguments of the self-check driver.</summary>
public class MdNavigationTests : IDisposable
{
    private readonly string _dir = IoPath.Combine(IoPath.GetTempPath(), "netie-mdn-" + Guid.NewGuid().ToString("N"));

    public MdNavigationTests() => IoDirectory.CreateDirectory(_dir);

    public void Dispose() => IoDirectory.Delete(_dir, true);

    [Fact]
    public void Back_and_forward_walk_the_documents_in_order_and_return_the_scroll_they_left()
    {
        var h = new MdHistory();
        h.Visit("a.md", "");
        h.SaveScroll(120);
        h.Visit("b.md", "intro");
        h.SaveScroll(40);
        h.Visit("c.md", "");
        Assert.True(h.CanBack);
        Assert.False(h.CanForward);

        var back = h.GoBack()!;
        Assert.Equal(("b.md", "intro", 40d), (back.Path, back.Fragment, back.ScrollY));
        back = h.GoBack()!;
        Assert.Equal(("a.md", 120d), (back.Path, back.ScrollY));
        Assert.False(h.CanBack);
        Assert.Null(h.GoBack());

        Assert.Equal("b.md", h.GoForward()!.Path);
        Assert.Equal("c.md", h.GoForward()!.Path);
        Assert.Null(h.GoForward());
    }

    [Fact]
    public void Opening_a_new_file_after_going_back_drops_the_forward_stops()
    {
        var h = new MdHistory();
        h.Visit("a.md", "");
        h.Visit("b.md", "");
        h.GoBack();
        Assert.True(h.CanForward);
        h.Visit("c.md", "");
        Assert.False(h.CanForward);
        Assert.Equal("a.md", h.GoBack()!.Path);
    }

    [Fact]
    public void Opening_the_file_already_shown_is_not_a_new_stop()
    {
        var h = new MdHistory();
        h.Visit("A.md", "");
        h.Visit("a.md", "top");
        Assert.False(h.CanBack);
        Assert.Equal("top", h.Current!.Fragment);
        Assert.Null(new MdHistory().GoBack());
    }

    [Fact]
    public void The_history_is_bounded()
    {
        var h = new MdHistory();
        for (var i = 0; i < MdHistory.Limit + 50; i++)
            h.Visit($"f{i}.md", "");
        Assert.Equal(MdHistory.Limit, h.BackCount);
    }

    [Fact]
    public void The_toggle_cycles_auto_light_dark_and_labels_say_so()
    {
        var m = MdColorMode.System;
        var seen = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            seen.Add(MdColorModes.Label(m));
            m = MdColorModes.Next(m);
        }
        Assert.Equal(new[] { "Auto", "Light", "Dark", "Auto" }, seen);
        Assert.Null(MdColorModes.Attribute(MdColorMode.System));
        Assert.Equal("dark", MdColorModes.Attribute(MdColorMode.Dark));
    }

    [Theory]
    [InlineData(MdColorMode.System, true, true)]
    [InlineData(MdColorMode.System, false, false)]
    [InlineData(MdColorMode.Light, true, false)]
    [InlineData(MdColorMode.Dark, false, true)]
    public void The_chrome_follows_Windows_only_in_auto(MdColorMode mode, bool systemDark, bool expected) =>
        Assert.Equal(expected, MdColorModes.IsDark(mode, systemDark));

    [Fact]
    public void The_choice_is_remembered_and_a_bad_file_means_auto()
    {
        var path = IoPath.Combine(_dir, "sub", "theme.txt");
        Assert.Equal(MdColorMode.System, MdSettings.LoadColorMode(path));
        Assert.True(MdSettings.SaveColorMode(path, MdColorMode.Dark));
        Assert.Equal(MdColorMode.Dark, MdSettings.LoadColorMode(path));
        Assert.True(MdSettings.SaveColorMode(path, MdColorMode.System));
        Assert.Equal(MdColorMode.System, MdSettings.LoadColorMode(path));
        IoFile.WriteAllText(path, "purple");
        Assert.Equal(MdColorMode.System, MdSettings.LoadColorMode(path));
    }

    [Fact]
    public void The_headless_arguments_read_theme_and_steps()
    {
        var a = MdArgs.Parse(new[] { "--app", "md", "--open", "x.md", "--theme", "dark", "--steps", "s.txt" });
        Assert.True(a.Headless);
        Assert.Null(a.Error);
        Assert.Equal(MdColorMode.Dark, a.Theme);
        Assert.Equal("s.txt", a.StepsPath);

        Assert.Contains("--theme", MdArgs.Parse(new[] { "--app", "md", "--open", "x.md", "--theme", "purple", "--snapshot", "o.png" }).Error);
        Assert.Contains("script file", MdArgs.Parse(new[] { "--app", "md", "--open", "x.md", "--steps" }).Error);
        Assert.Contains("--open", MdArgs.Parse(new[] { "--app", "md", "--steps", "s.txt" }).Error);

        var plain = MdArgs.Parse(new[] { "--app", "md", "--open", "x.md", "--theme", "light" });
        Assert.False(plain.Headless);
        Assert.Equal(MdColorMode.Light, plain.Theme);
    }

    [Fact]
    public void A_relative_link_to_another_markdown_file_keeps_its_heading_for_the_history()
    {
        var link = MdLinks.Classify("docs/guide.md#install", _dir);
        Assert.Equal(MdLinkKind.Markdown, link.Kind);
        Assert.Equal(IoPath.GetFullPath(IoPath.Combine(_dir, "docs", "guide.md")), link.Target);
        Assert.Equal("install", link.Fragment);
        Assert.Equal(MdLinkKind.External, MdLinks.Classify("https://example.com/", _dir).Kind);
        Assert.Equal(MdLinkKind.Blocked, MdLinks.Classify("tool.exe", _dir).Kind);
    }
}
