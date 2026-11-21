namespace NetieSpace.Tests;

public class PdfToolSecurityTests
{
    [Fact]
    public void Protect_makes_the_file_unreadable_without_the_password_and_readable_with_it()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "secret.pdf", 2, "SEC");

        var path = Assert.Single(PdfKit.RunOk("pdf-protect", new[] { src }, PdfKit.NewDir(), ("password", "open-sesame-77")));

        Assert.ThrowsAny<Exception>(() => PdfKit.Texts(path));
        Assert.ThrowsAny<Exception>(() => PdfKit.Texts(path, "wrong-password"));
        var t = PdfKit.Texts(path, "open-sesame-77");
        Assert.Equal(2, t.Count);
        Assert.Contains("SEC page 1", t[0]);
        Assert.Contains("SEC page 2", t[1]);
        Assert.Contains("/Encrypt", PdfKit.Latin1(path));
        Assert.DoesNotContain("/Encrypt", PdfKit.Latin1(src));
    }

    [Fact]
    public void Protect_writes_the_permission_flags_it_was_given()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "secret.pdf", 1, "SEC");
        var locked = Assert.Single(PdfKit.RunOk("pdf-protect", new[] { src }, PdfKit.NewDir(),
            ("password", "pw-one-two"), ("allow_print", "false"), ("allow_copy", "false"), ("allow_modify", "false")));
        var open = Assert.Single(PdfKit.RunOk("pdf-protect", new[] { src }, PdfKit.NewDir(),
            ("password", "pw-one-two"), ("allow_print", "true"), ("allow_copy", "true"), ("allow_modify", "true")));

        static int P(string path)
        {
            var m = System.Text.RegularExpressions.Regex.Match(PdfKit.Latin1(path), @"/P\s+(-?\d+)");
            Assert.True(m.Success, "no /P entry in " + path);
            return int.Parse(m.Groups[1].Value);
        }
        var lockedP = P(locked);
        var openP = P(open);
        Assert.NotEqual(lockedP, openP);
        Assert.Equal(0, lockedP & 0x4);      // bit 3: print
        Assert.Equal(0, lockedP & 0x10);     // bit 5: copy
        Assert.NotEqual(0, openP & 0x4);
        Assert.NotEqual(0, openP & 0x10);
        Assert.NotEqual(0, openP & 0x8);     // bit 4: modify
    }

    [Fact]
    public void Protect_requires_a_password_and_protects_each_input_to_its_own_file()
    {
        var dir = PdfKit.NewDir();
        var a = PdfKit.Make(PdfKit.NewDir("one"), "same.pdf", 1, "ONE");
        var b = PdfKit.Make(PdfKit.NewDir("two"), "same.pdf", 1, "TWO");
        var none = PdfKit.Run("pdf-protect", new[] { a }, dir);
        Assert.False(none.Ok);
        Assert.Contains("'password'", none.Error);
        Assert.Empty(IoDirectory.GetFiles(dir));

        var outs = PdfKit.RunOk("pdf-protect", new[] { a, b }, dir, ("password", "pw-one-two"));
        Assert.Equal(2, outs.Count);
        Assert.Contains("ONE", PdfKit.Texts(outs[0], "pw-one-two")[0]);
        Assert.Contains("TWO", PdfKit.Texts(outs[1], "pw-one-two")[0]);
    }

    [Fact]
    public void Unlock_with_the_right_password_yields_a_file_that_opens_without_one()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "doc.pdf", 3, "LCK");
        var locked = Assert.Single(PdfKit.RunOk("pdf-protect", new[] { src }, PdfKit.NewDir(), ("password", "open-sesame-77")));

        var path = Assert.Single(PdfKit.RunOk("pdf-unlock", new[] { locked }, PdfKit.NewDir(), ("password", "open-sesame-77")));

        var t = PdfKit.Texts(path);                       // no password given
        Assert.Equal(3, t.Count);
        Assert.Contains("LCK page 3", t[2]);
        Assert.DoesNotContain("/Encrypt", PdfKit.Latin1(path));
    }

    [Fact]
    public void Unlock_never_hands_back_blank_pages_when_given_only_the_permissions_password()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "doc.pdf", 3, "OWN");
        var locked = Assert.Single(PdfKit.RunOk("pdf-protect", new[] { src }, PdfKit.NewDir(),
            ("password", "user-pass-1"), ("owner_password", "owner-pass-9"), ("allow_print", "false")));
        var outDir = PdfKit.NewDir();

        var viaOwner = PdfKit.Run("pdf-unlock", new[] { locked }, outDir, ("password", "owner-pass-9"));

        Assert.False(viaOwner.Ok);
        Assert.Contains("user", viaOwner.Error);
        Assert.Empty(IoDirectory.GetFiles(outDir));

        var viaUser = Assert.Single(PdfKit.RunOk("pdf-unlock", new[] { locked }, PdfKit.NewDir(), ("password", "user-pass-1")));
        Assert.Contains("OWN page 3", PdfKit.Texts(viaUser)[2]);
        Assert.DoesNotContain("/Encrypt", PdfKit.Latin1(viaUser));
    }

    [Fact]
    public void Unlock_fails_clearly_on_a_wrong_or_missing_password_and_writes_nothing()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "doc.pdf", 1);
        var locked = Assert.Single(PdfKit.RunOk("pdf-protect", new[] { src }, PdfKit.NewDir(), ("password", "open-sesame-77")));
        var outDir = PdfKit.NewDir();

        var wrong = PdfKit.Run("pdf-unlock", new[] { locked }, outDir, ("password", "nope-nope"));
        var missing = PdfKit.Run("pdf-unlock", new[] { locked }, outDir);

        Assert.False(wrong.Ok);
        Assert.Contains("password", wrong.Error);
        Assert.False(missing.Ok);
        Assert.Contains("password", missing.Error);
        Assert.Empty(IoDirectory.GetFiles(outDir));
    }

    [Fact]
    public void Unlock_lifts_owner_only_restrictions_without_a_password()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "doc.pdf", 1, "OWN");
        // Empty open password is not possible through pdf-protect (it demands one), so build the
        // owner-only file with the engine directly.
        var restricted = IoPath.Combine(PdfKit.NewDir(), "restricted.pdf");
        using (var d = PdfSharpCore.Pdf.IO.PdfReader.Open(src, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Modify))
        {
            d.SecuritySettings.DocumentSecurityLevel = PdfSharpCore.Pdf.Security.PdfDocumentSecurityLevel.Encrypted128Bit;
            d.SecuritySettings.OwnerPassword = "owner-only-secret";
            d.SecuritySettings.PermitPrint = false;
            d.SecuritySettings.PermitExtractContent = false;
            d.Save(restricted);
        }
        Assert.Contains("/Encrypt", PdfKit.Latin1(restricted));

        var path = Assert.Single(PdfKit.RunOk("pdf-unlock", new[] { restricted }, PdfKit.NewDir()));

        Assert.DoesNotContain("/Encrypt", PdfKit.Latin1(path));
        Assert.Contains("OWN page 1", PdfKit.Texts(path)[0]);
    }

    [Fact]
    public void Unlock_of_a_file_that_is_not_encrypted_still_produces_a_plain_copy_it_does_not_pretend_otherwise()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "plain.pdf", 1, "PLN");
        var path = Assert.Single(PdfKit.RunOk("pdf-unlock", new[] { src }, PdfKit.NewDir()));
        Assert.Contains("PLN page 1", PdfKit.Texts(path)[0]);
    }

    [Fact]
    public void Protect_and_unlock_run_through_the_tool_cli()
    {
        var src = PdfKit.Make(PdfKit.NewDir(), "doc.pdf", 1, "CLI");
        var (e1, o1, err1) = PdfKit.Cli("pdf-protect", new[] { src }, PdfKit.NewDir(), ("password", "cli-pass-123"));
        Assert.True(e1 == 0, err1);
        var locked = Assert.Single(PdfKit.Lines(o1));
        Assert.ThrowsAny<Exception>(() => PdfKit.Texts(locked));

        var (e2, o2, err2) = PdfKit.Cli("pdf-unlock", new[] { locked }, PdfKit.NewDir(), ("password", "cli-pass-123"));
        Assert.True(e2 == 0, err2);
        Assert.Contains("CLI page 1", PdfKit.Texts(Assert.Single(PdfKit.Lines(o2)))[0]);

        var (e3, _, err3) = PdfKit.Cli("pdf-unlock", new[] { locked }, PdfKit.NewDir(), ("password", "bad"));
        Assert.Equal(1, e3);
        Assert.Contains("password", err3);
    }
}
