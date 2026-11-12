using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NetieSpace.Apps.Tools;

// Tool contract (S50-E2-a). Plain net8.0 only: no WPF, no WinRT, no UI-layer or Services references.
// This folder is compiled into the test project, so keep it free of the WPF namespaces.

public enum ToolApp { Pdf, Image, Video }

/// <summary>Verdict column of the research catalog, verbatim.</summary>
public enum ToolVerdict { Local, LocalPartial, NeedsEngine, CloudOnly, AiEgress }

public enum EngineLicense
{
    MIT,
    Apache2,
    BSD3,
    MPL2,
    LGPL3,
    GPL3OutOfProcess,
    AGPL,
    Proprietary,
    InProcessManaged,
    WinRT,
    OfficeCom,
}

/// <summary>
/// An engine a tool depends on. <paramref name="ProbeKey"/> selects the probe in
/// <see cref="EngineProbes"/>; the licence gate looks only at <paramref name="License"/>.
/// </summary>
public sealed record EngineRef(string Name, EngineLicense License, bool ShipsInPackage, string ProbeKey = "inproc");

public enum ToolOptionKind { Choice, Int, Percent, Bool, Text, PageRange, Color, File, Grid }

public sealed record ToolOption(
    string Key,
    ToolOptionKind Kind,
    string Label,
    string? Default = null,
    IReadOnlyList<string>? Choices = null,
    int? Min = null,
    int? Max = null);

/// <summary>
/// A confirm-to-install offer. Data only: nothing in this folder downloads or pip-installs.
/// </summary>
public sealed record InstallOffer
{
    public string SourceUrl { get; }
    public long SizeBytes { get; }
    public string License { get; }
    public string? Sha256 { get; }

    public InstallOffer(string sourceUrl, long sizeBytes, string license, string? sha256 = null)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
            throw new ArgumentException("InstallOffer needs a SourceUrl.", nameof(sourceUrl));
        if (sizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "InstallOffer needs SizeBytes > 0.");
        if (string.IsNullOrWhiteSpace(license))
            throw new ArgumentException("InstallOffer needs a License.", nameof(license));
        SourceUrl = sourceUrl;
        SizeBytes = sizeBytes;
        License = license;
        Sha256 = sha256;
    }
}

/// <summary>
/// One catalog row. <paramref name="Name"/> and <paramref name="Verb"/> are shown in the product:
/// Netie's own wording, a short name and a one-word action. Never a source site's name or a sentence.
/// </summary>
public sealed record ToolEntry(
    string Id,
    ToolApp App,
    string Category,
    string Name,
    string Verb,
    string Glyph,
    IReadOnlyList<string> AcceptedTypes,
    int MinFiles,
    int MaxFiles,
    IReadOnlyList<ToolOption> Options,
    EngineRef Engine,
    ToolVerdict Verdict,
    InstallOffer? InstallOffer = null,
    string Notes = "");

public enum UnavailableReason { None, NotBuilt, EngineMissing, LicenceNotCleared, NeedsDisclose, CloudOnly }

public sealed record ToolAvailability(bool Available, UnavailableReason Reason, string Text)
{
    public static readonly ToolAvailability Yes = new(true, UnavailableReason.None, "");
}

public sealed record ToolRequest(
    IReadOnlyList<string> Files,
    string OutDir,
    IReadOnlyDictionary<string, string> Options);

public sealed record ToolProgress(int Percent, string Message);

/// <summary>Runner contract: return every output path it claims to have written.</summary>
public delegate Task<IReadOnlyList<string>> ToolRunnerDelegate(
    ToolRequest request, IProgress<ToolProgress>? progress, CancellationToken ct);

public interface IToolProvider
{
    ToolApp App { get; }
    IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners();
}

/// <summary>Put on a provider type that must not be found by reflection (test fakes).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ToolProviderOptOutAttribute : Attribute { }

public enum ToolOutcome { Ok, Failed, Unavailable, Invalid }

public sealed record ToolResult(
    bool Ok,
    ToolOutcome Outcome,
    string ToolId,
    string Engine,
    IReadOnlyList<string> Outputs,
    string? Error,
    string? Reason,
    long ElapsedMs);
