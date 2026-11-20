using NetieSpace.Apps.Shell;

namespace NetieSpace.Tests;

/// <summary>S50-E1-a: argv is classified before the Space single-instance mutex exists.</summary>
public class AppLaunchTests
{
    [Theory]
    [InlineData("pdf", "Netie.Pdf", "Netie PDF")]
    [InlineData("image", "Netie.Image", "Netie Image")]
    [InlineData("video", "Netie.Video", "Netie Video")]
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
    public void Kernel_object_names_are_per_app_and_never_the_Space_names()
    {
        var names = AppIdentity.All
            .SelectMany(a => new[] { a.InstanceMutexName, a.ActivateEventName, a.PendingOpenPath })
            .ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(names, n => Assert.DoesNotContain("NetieSpace.SingleInstance", n));
        Assert.All(names, n => Assert.DoesNotContain("ShowTray", n));
        Assert.Equal("Local\\Netie.Pdf.Instance.v1", AppIdentity.Pdf.InstanceMutexName);
        Assert.Equal("Local\\Netie.Pdf.Activate.v1", AppIdentity.Pdf.ActivateEventName);
        Assert.EndsWith(IoPath.Combine("NetieSpace", "apps", "pdf", "pending-open.txt"), AppIdentity.Pdf.PendingOpenPath);
    }
}
