using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using PdfSharpCore.Pdf.Security;
using static NetieSpace.Apps.Pdf.PdfToolSupport;

namespace NetieSpace.Apps.Pdf;

/// <summary>
/// Password protection on PdfSharpCore's standard security handler (RC4, 128-bit key). That is the
/// strongest handler this engine writes; AES-256 needs a different engine and is not claimed here.
/// </summary>
internal static class PdfSecurityTools
{
    public static Task<IReadOnlyList<string>> ProtectAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            var user = Opt(req, "password");
            if (user.Length == 0) throw new ArgumentException("option 'password': an open password is required");
            var owner = Opt(req, "owner_password");
            if (owner.Length == 0)
                owner = user + "#owner-" + Guid.NewGuid().ToString("N");
            var allowPrint = OptBool(req, "allow_print", true);
            var allowCopy = OptBool(req, "allow_copy", true);
            var allowModify = OptBool(req, "allow_modify", false);
            var allowAnnotate = OptBool(req, "allow_comments", true);

            var names = new OutputNamer(req.OutDir);
            var outputs = new List<string>();
            for (var f = 0; f < req.Files.Count; f++)
            {
                ct.ThrowIfCancellationRequested();
                var input = req.Files[f];
                using var doc = OpenModify(input);
                var s = doc.SecuritySettings;
                s.DocumentSecurityLevel = PdfDocumentSecurityLevel.Encrypted128Bit;
                s.UserPassword = user;
                s.OwnerPassword = owner;
                s.PermitPrint = allowPrint;
                s.PermitFullQualityPrint = allowPrint;
                s.PermitExtractContent = allowCopy;
                s.PermitAccessibilityExtractContent = true;
                s.PermitModifyDocument = allowModify;
                s.PermitAssembleDocument = allowModify;
                s.PermitAnnotations = allowAnnotate;
                s.PermitFormsFill = allowAnnotate;
                RefreshInfoStrings(doc);
                var path = names.Next(Stem(input) + "_protected", ".pdf");
                doc.Save(path);
                Verify("protect", input, "", path, user, "The protected copy was discarded.");
                outputs.Add(path);
                Report(progress, f + 1, req.Files.Count, "protected " + IoPath.GetFileName(input));
            }
            return outputs;
        });

    /// <summary>
    /// This engine writes a string it holds as UTF-16 (which is what its Title, Author and Creator setters make) in
    /// the clear even in a protected file, so every other viewer decrypts it into noise and shows a garbled title.
    /// A string held in PDFDocEncoding is encrypted correctly, so the descriptive fields are written that way.
    /// Characters outside Latin-1 cannot be written that way and become '?' rather than noise.
    /// </summary>
    private static void RefreshInfoStrings(PdfSharpCore.Pdf.PdfDocument doc)
    {
        var i = doc.Info;
        void Put(string key, string? value)
        {
            var safe = new string((value ?? "").Select(c => (c >= ' ' && c <= '~') || (c >= '\u00A1' && c <= '\u00FF') ? c : '?').ToArray());
            i.Elements[key] = new PdfSharpCore.Pdf.PdfString(safe, PdfSharpCore.Pdf.PdfStringEncoding.PDFDocEncoding);
        }
        Put("/Title", i.Title);
        Put("/Author", i.Author);
        Put("/Subject", i.Subject);
        Put("/Keywords", i.Keywords);
        Put("/Creator", i.Creator);
    }

    public static Task<IReadOnlyList<string>> UnlockAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            var password = Opt(req, "password");
            var names = new OutputNamer(req.OutDir);
            var outputs = new List<string>();
            for (var f = 0; f < req.Files.Count; f++)
            {
                ct.ThrowIfCancellationRequested();
                var input = req.Files[f];
                var path = names.Next(Stem(input) + "_unlocked", ".pdf");
                // Decrypting in place through the editing route leaves some streams undecoded in this engine, so the
                // pages are read through the import route (user or owner password both work) and carried into a
                // fresh, unprotected document. Page content and annotations come along; bookmarks and form
                // dictionaries do not.
                PdfSharpCore.Pdf.PdfDocument src;
                try
                {
                    src = OpenImport(input, password);
                }
                catch (InvalidOperationException ex) when (ex.InnerException is PdfSharpCore.Pdf.IO.PdfReaderException)
                {
                    var why = password.Length == 0
                        ? "it needs a password (option 'password')"
                        : "the password in option 'password' is wrong, or the file uses an encryption this engine cannot read";
                    throw new InvalidOperationException($"cannot unlock {IoPath.GetFileName(input)}: {why}. Reader said: {ex.InnerException.Message}", ex);
                }
                using (src)
                using (var copy = new PdfSharpCore.Pdf.PdfDocument())
                {
                    copy.Info.Title = src.Info.Title;
                    copy.Info.Author = src.Info.Author;
                    copy.Info.Subject = src.Info.Subject;
                    copy.Info.Keywords = src.Info.Keywords;
                    for (var pg = 0; pg < src.PageCount; pg++) copy.AddPage(src.Pages[pg]);
                    copy.Save(path);
                }
                Verify("unlock", input, password, path, null, "Try the open (user) password instead of a permissions password.");
                outputs.Add(path);
                Report(progress, f + 1, req.Files.Count, "unlocked " + IoPath.GetFileName(input));
            }
            return outputs;
        });

    /// <summary>
    /// Reads input and output side by side with PdfPig and fails (deleting the output) when page count, page
    /// text or the number of drawing operations differ. This engine can hand back blank pages without an
    /// error when it is given the permissions password, so a result is never trusted unread.
    /// </summary>
    private static void Verify(string verb, string input, string? inputPassword, string output, string? outputPassword, string hint)
    {
        void Fail(string why)
        {
            try { IoFile.Delete(output); } catch (System.IO.IOException) { }
            throw new InvalidOperationException($"cannot {verb} {IoPath.GetFileName(input)}: {why}. {hint}");
        }
        UglyToad.PdfPig.ParsingOptions Opts(string? pw) => new() { Passwords = pw is null ? new List<string>() : new List<string> { pw } };
        try
        {
            using var before = UglyToad.PdfPig.PdfDocument.Open(input, Opts(inputPassword));
            using var after = UglyToad.PdfPig.PdfDocument.Open(output, Opts(outputPassword));
            if (before.NumberOfPages != after.NumberOfPages) Fail("the result has a different page count");
            for (var n = 1; n <= before.NumberOfPages; n++)
            {
                var a = before.GetPage(n);
                var b = after.GetPage(n);
                if (a.Text != b.Text) Fail($"page {n} of the result reads differently from the original");
                if (a.Operations.Count != b.Operations.Count) Fail($"page {n} of the result lost drawing content");
            }
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            Fail("the result could not be read back to check it (" + ex.Message + ")");
        }
    }
}
