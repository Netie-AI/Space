using NetieSpace.Apps.Shell;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NetieSpace.Tests;

/// <summary>S50-E1-c: what Start Menu registration writes, pinned as data. COM itself needs Windows.</summary>
public class AppRegistrationTests
{
    [Theory]
    [InlineData("pdf", "Netie PDF.lnk", "--app pdf", "Netie.Pdf", "netie-pdf.ico")]
    [InlineData("image", "Netie Image.lnk", "--app image", "Netie.Image", "netie-image.ico")]
    [InlineData("video", "Netie Video.lnk", "--app video", "Netie.Video", "netie-video.ico")]
    [InlineData("md", "Netie MD.lnk", "--app md", "Netie.Md", "netie-md.ico")]
    public void Each_app_has_one_shortcut_with_its_arguments_aumid_and_icon(
        string key, string file, string args, string aumid, string icon)
    {
        var spec = Assert.Single(AppRegistration.Specs, s => s.Key == key);
        Assert.Equal(file, spec.FileName);
        Assert.Equal(args, spec.Arguments);
        Assert.Equal(aumid, spec.Aumid);
        Assert.Equal(icon, spec.IconFileName);
    }

    [Fact]
    public void Exactly_four_shortcuts_with_distinct_names_and_aumids()
    {
        Assert.Equal(4, AppRegistration.Specs.Count);
        Assert.Equal(4, AppRegistration.Specs.Select(s => s.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(4, AppRegistration.Specs.Select(s => s.Aumid).Distinct().Count());
        Assert.DoesNotContain(AppRegistration.Specs, s => s.FileName.Equals("Netie Space.lnk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Shortcut_arguments_round_trip_through_the_launch_parser()
    {
        foreach (var spec in AppRegistration.Specs)
        {
            var launch = AppLaunch.Parse(spec.Arguments.Split(' '));
            Assert.Equal(AppLaunchKind.App, launch.Kind);
            Assert.Equal(spec.Aumid, launch.Identity!.Aumid);
            Assert.Equal(spec.DisplayName, launch.Identity.DisplayName);
        }
    }

    [Fact]
    public void Relaunch_command_quotes_the_exe_and_names_the_app()
    {
        Assert.Equal(
            "\"C:\\Program Files\\Netie Space\\NetieSpace.exe\" --app pdf",
            AppRegistration.RelaunchCommand("C:\\Program Files\\Netie Space\\NetieSpace.exe", AppIdentity.Pdf));
        Assert.EndsWith("\" --app video", AppRegistration.RelaunchCommand("/x/NetieSpace.exe", AppIdentity.Video));
    }

    [Fact]
    public void Property_keys_are_the_documented_appusermodel_ones()
    {
        Assert.Equal(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), AppRegistration.AppUserModelFormatId);
        Assert.Equal(5u, AppRegistration.AppUserModelIdPid);
        Assert.Equal(2u, AppRegistration.RelaunchCommandPid);
        Assert.Equal(3u, AppRegistration.RelaunchIconResourcePid);
        Assert.Equal(4u, AppRegistration.RelaunchDisplayNameResourcePid);
    }

    [Fact]
    public void Icon_path_sits_in_Assets_apps_beside_the_exe()
    {
        var path = AppRegistration.IconPath(IoPath.Combine("root", "app"), AppIdentity.Image);
        Assert.Equal(IoPath.Combine("root", "app", "Assets", "apps", "netie-image.ico"), path);
        Assert.Equal(AppIdentity.Image.IconPath, AppRegistration.IconPath(AppContext.BaseDirectory, AppIdentity.Image));
    }

    [Fact]
    public void Shortcut_lands_in_Start_Menu_Programs()
    {
        var programs = AppRegistration.ProgramsDirectory("sm");
        Assert.Equal(IoPath.Combine("sm", "Programs"), programs);
        Assert.Equal(
            IoPath.Combine("sm", "Programs", "Netie PDF.lnk"),
            AppRegistration.ShortcutPath(programs, AppRegistration.ForIdentity(AppIdentity.Pdf)));
    }

    [Theory]
    [InlineData("--register-apps", RegistrationSwitch.Register)]
    [InlineData("--unregister-apps", RegistrationSwitch.Unregister)]
    [InlineData("--REGISTER-APPS", RegistrationSwitch.Register)]
    public void Registration_switch_is_recognised(string arg, RegistrationSwitch expected) =>
        Assert.Equal(expected, AppRegistration.Parse(new[] { arg }));

    [Fact]
    public void Registration_switch_is_only_honoured_as_the_first_argument()
    {
        Assert.Equal(RegistrationSwitch.None, AppRegistration.Parse(Array.Empty<string>()));
        Assert.Equal(RegistrationSwitch.None, AppRegistration.Parse(new[] { "--app", "pdf", "--register-apps" }));
        Assert.Equal(RegistrationSwitch.None, AppRegistration.Parse(new[] { "--register-apps-now" }));
        Assert.Equal(RegistrationSwitch.None, AppRegistration.Parse(new[] { "C:\\a\\--register-apps" }));
        // Not an app launch either, so the Space path would handle it; AppEntry intercepts first.
        Assert.Equal(AppLaunchKind.Space, AppLaunch.Parse(new[] { "--register-apps" }).Kind);
    }

    // ---- the committed icons, read back from the files a person gets ----

    private static string IconDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !IoFile.Exists(IoPath.Combine(dir.FullName, "NetieSpace.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return IoPath.Combine(dir!.FullName, "src", "NetieSpace", "Assets", "apps");
    }

    private static readonly int[] Sizes = { 16, 24, 32, 48, 256 };

    [Theory]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("video")]
    [InlineData("md")]
    public void Icon_is_a_real_ico_with_the_five_sizes_and_png_frames(string key)
    {
        var bytes = IoFile.ReadAllBytes(IoPath.Combine(IconDir(), $"netie-{key}.ico"));
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        var count = BitConverter.ToUInt16(bytes, 4);
        Assert.Equal(Sizes.Length, count);

        var seen = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var e = 6 + 16 * i;
            var size = bytes[e] == 0 ? 256 : bytes[e];
            Assert.Equal(size, bytes[e + 1] == 0 ? 256 : bytes[e + 1]);
            Assert.Equal(32, BitConverter.ToUInt16(bytes, e + 6));
            var length = (int)BitConverter.ToUInt32(bytes, e + 8);
            var offset = (int)BitConverter.ToUInt32(bytes, e + 12);
            Assert.True(offset + length <= bytes.Length, "frame runs past the end of the file");

            // Every frame is PNG-compressed; decode it with an independent reader.
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[offset..(offset + 4)]);
            using var image = Image.Load<Rgba32>(bytes[offset..(offset + length)]);
            Assert.Equal(size, image.Width);
            Assert.Equal(size, image.Height);
            Assert.Equal(0, image[0, 0].A);                       // rounded corner is transparent
            Assert.Equal(255, image[size / 2, size / 8 * 7].A);   // body is opaque
            seen.Add(size);
        }
        Assert.Equal(Sizes, seen);
    }

    [Fact]
    public void The_three_icons_differ_and_the_256_frame_carries_a_mark_on_the_accent()
    {
        var faces = new List<(int R, int G, int B)>();
        foreach (var key in new[] { "pdf", "image", "video" })
        {
            var bytes = IoFile.ReadAllBytes(IoPath.Combine(IconDir(), $"netie-{key}.ico"));
            var big = Enumerable.Range(0, 5).Select(i => 6 + 16 * i).Single(e => bytes[e] == 0);
            var length = (int)BitConverter.ToUInt32(bytes, big + 8);
            var offset = (int)BitConverter.ToUInt32(bytes, big + 12);
            using var image = Image.Load<Rgba32>(bytes[offset..(offset + length)]);
            var bg = image[40, 40];                 // accent face, outside every mark
            faces.Add((bg.R, bg.G, bg.B));
            var white = 0;
            for (var y = 0; y < 256; y += 4)
                for (var x = 0; x < 256; x += 4)
                    if (image[x, y] is { R: 255, G: 255, B: 255, A: 255 })
                        white++;
            Assert.InRange(white, 100, 2500);       // a mark exists but is not the whole face
        }
        Assert.Equal(3, faces.Distinct().Count());
    }
}
