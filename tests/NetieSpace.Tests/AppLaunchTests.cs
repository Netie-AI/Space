using NetieSpace.Apps.Shell;

namespace NetieSpace.Tests;

/// <summary>S50-E1-a: argv is classified before the Space single-instance mutex exists.</summary>
public class AppLaunchTests
{
    [Theory]
    [InlineData("pdf", "Netie.Pdf", "Netie PDF")]
    [InlineData("image", "Netie.Image", "Netie Image")]
    [InlineData("video", "Netie.Video", "Netie Video")]
    [InlineData("md", "Netie.Md", "Netie MD")]
    [InlineData("PDF", "Netie.Pdf", "Netie PDF")]
    public void App_flag_resolves_identity(string value, string aumid, string name)
    {
        var launch = AppLaunch.Parse(new[] { "--app", value });
        Assert.Equal(AppLaunchKind.App, launch.Kind);
        Assert.Equal(aumid, launch.Identity!.Aumid);
        Assert.Equal(name, launch.Identity.DisplayName);
        Assert.Null(launch.OpenPath);
    }

    [Fact]
    public void Open_flag_carries_the_path_even_when_missing_on_disk()
    {
        var launch = AppLaunch.Parse(new[] { "--app", "pdf", "--open", "\"C:\\nowhere\\a b.pdf\"" });
        Assert.Equal(AppLaunchKind.App, launch.Kind);
        Assert.Equal("C:\\nowhere\\a b.pdf", launch.OpenPath);
    }

    [Fact]
    public void Bare_existing_path_after_the_app_name_is_the_file()
    {
        var file = IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "sample.pdf");
        Assert.True(IoFile.Exists(file));
        var launch = AppLaunch.Parse(new[] { "--app", "pdf", file });
        Assert.Equal(file, launch.OpenPath);
    }

    [Theory]
    [InlineData("--snapshot")]
    [InlineData("no-such-file-here.pdf")]
    public void Bare_argument_that_is_a_flag_or_not_on_disk_is_not_a_file(string next)
    {
        var launch = AppLaunch.Parse(new[] { "--app", "image", next });
        Assert.Equal(AppLaunchKind.App, launch.Kind);
        Assert.Null(launch.OpenPath);
    }

    [Theory]
    [InlineData("word")]
    [InlineData("")]
    public void Unknown_app_value_is_invalid_and_names_it(string value)
    {
        var launch = AppLaunch.Parse(new[] { "--app", value });
        Assert.Equal(AppLaunchKind.Invalid, launch.Kind);
        Assert.Null(launch.Identity);
        Assert.Contains(value.Length == 0 ? "(none)" : value, launch.Error);
    }

    [Fact]
    public void App_flag_without_a_value_is_invalid()
    {
        Assert.Equal(AppLaunchKind.Invalid, AppLaunch.Parse(new[] { "--app" }).Kind);
    }

    [Theory]
    [InlineData("--list-tools")]
    [InlineData("--doctor")]
    [InlineData("--tool")]
    public void Tool_flags_route_to_the_tool_cli(string flag)
    {
        Assert.Equal(AppLaunchKind.ToolCli, AppLaunch.Parse(new[] { flag, "pdf-extract-text" }).Kind);
    }

    [Theory]
    [InlineData()]
    [InlineData("--preview", "C:\\a.pdf")]
    [InlineData("--edit", "C:\\a.pdf")]
    [InlineData("--stamp-test")]
    [InlineData("C:\\a.pdf")]
    [InlineData("--setup")]
    public void Space_args_are_left_to_the_tray_path(params string[] args)
    {
        Assert.Equal(AppLaunchKind.Space, AppLaunch.Parse(args).Kind);
    }

    [Fact]
    public void The_tool_apps_stay_three_and_every_app_is_four()
    {
        Assert.Equal(new[] { "pdf", "image", "video" }, AppIdentity.All.Select(a => a.Key));
        Assert.Equal(new[] { "pdf", "image", "video", "md" }, AppIdentity.Everything.Select(a => a.Key));
        Assert.True(AppIdentity.Md.IsMarkdown);
        Assert.DoesNotContain(AppIdentity.Everything.Where(a => a != AppIdentity.Md), a => a.IsMarkdown);
    }

    [Fact]
    public void Md_launch_carries_the_open_path_and_unknown_apps_name_the_choices()
    {
        var launch = AppLaunch.Parse(new[] { "--app", "md", "--open", "C:\\notes\\a b.md" });
        Assert.Equal(AppIdentity.Md, launch.Identity);
        Assert.Equal("C:\\notes\\a b.md", launch.OpenPath);
        var bad = AppLaunch.Parse(new[] { "--app", "docx" });
        Assert.Equal(AppLaunchKind.Invalid, bad.Kind);
        Assert.Contains("md", bad.Error);
    }

    [Fact]
    public void Kernel_object_names_are_per_app_and_never_the_Space_names()
    {
        var names = AppIdentity.Everything
            .SelectMany(a => new[] { a.InstanceMutexName, a.ActivateEventName, a.PendingOpenPath })
            .ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(names, n => Assert.DoesNotContain("NetieSpace.SingleInstance", n));
        Assert.All(names, n => Assert.DoesNotContain("ShowTray", n));
        Assert.Equal("Local\\Netie.Pdf.Instance.v1", AppIdentity.Pdf.InstanceMutexName);
        Assert.Equal("Local\\Netie.Pdf.Activate.v1", AppIdentity.Pdf.ActivateEventName);
        Assert.EndsWith(IoPath.Combine("NetieSpace", "apps", "pdf", "pending-open.txt"), AppIdentity.Pdf.PendingOpenPath);
    }

    [Theory]
    [InlineData("--send-to-phone", "report.pdf")]
    [InlineData("--send-to-phone", "report.pdf", "--yes")]
    [InlineData("--send-to-phone", "report.pdf", "--yes", "--json", "out.json")]
    [InlineData("--send-to-phone")]
    public void Send_to_phone_is_the_bridge_path_not_the_tray(params string[] args)
    {
        // Routed to BridgeCli even with a missing file: BridgeCli answers bad arguments with exit 2, never the tray.
        Assert.Equal(AppLaunchKind.Bridge, AppLaunch.Parse(args).Kind);
    }

    [Fact]
    public void Send_to_phone_inside_an_app_launch_stays_an_app_launch()
    {
        Assert.Equal(AppLaunchKind.App, AppLaunch.Parse(new[] { "--app", "pdf", "--send-to-phone" }).Kind);
    }

    [Fact]
    public void Plain_space_args_are_not_the_bridge()
    {
        Assert.Equal(AppLaunchKind.Space, AppLaunch.Parse(new[] { "--send-to-phonex" }).Kind);
        Assert.Equal(AppLaunchKind.Space, AppLaunch.Parse(System.Array.Empty<string>()).Kind);
    }
}
