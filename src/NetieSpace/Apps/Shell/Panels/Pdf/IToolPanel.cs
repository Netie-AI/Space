using System.Windows;
using System.Windows.Controls;
using NetieSpace.Apps.Pdf;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Shell.Panels.Pdf;

/// <summary>
/// A visual panel that takes the place of the plain file list on a tool page (merge strip, page grid, signature
/// placement). The page keeps ownership of the run: it asks the panel which files are chosen, whether the action may
/// run, which option values the panel decides, and which tool to run. No panel starts a run itself.
/// </summary>
internal interface IToolPanel
{
    FrameworkElement View { get; }

    /// <summary>Options the panel decides itself; the page does not draw a control for them.</summary>
    IReadOnlyCollection<string> HiddenOptions { get; }

    /// <summary>The input files in the order the panel shows them (the order the engine receives).</summary>
    IReadOnlyList<string> Files { get; }

    /// <summary>Raised when the files or the edits changed: the page re-reads <see cref="Files"/> and the action state.</summary>
    event Action? Changed;

    /// <summary>The page hands the panel a file list (a pending file, a snapshot). Same list as <see cref="Files"/>: nothing happens.</summary>
    void Show(IReadOnlyList<string> files);

    /// <summary>Files dropped from Explorer or chosen with Browse.</summary>
    void AddFiles(IEnumerable<string> dropped);

    /// <summary>False with the reason a person reads next to the disabled action button.</summary>
    bool Ready(out string? why);

    /// <summary>The tool to run, when it is not the page's own tool; null for the page's tool.</summary>
    RegisteredTool? RunTool { get; }

    /// <summary>Option values the panel decides; they win over the page's controls. Never remembered.</summary>
    IReadOnlyDictionary<string, string> Options();

    /// <summary>Adds the panel's own buttons next to Open / Show in folder after a run.</summary>
    void DecorateResult(Panel buttons, string? output);

    /// <summary>Completes when thumbnails and previews have been drawn (the snapshot waits on it).</summary>
    Task Settled { get; }

    /// <summary>One line the snapshot prints so CI can assert what was drawn.</summary>
    string Describe();

    /// <summary>--state text from the snapshot command line (selections, rotations); ignored by panels without one.</summary>
    void ApplyState(string state);
}

/// <summary>What a panel needs from the page that hosts it.</summary>
internal sealed record PanelContext(FrameworkElement Host, RegisteredTool Tool, Func<string, string> ReadOption, Action Browse);

internal static class PdfPanels
{
    /// <summary>The panel of a PDF tool, or null when the tool keeps the plain page.</summary>
    public static IToolPanel? For(string toolId, PanelContext ctx) => toolId switch
    {
        "pdf-merge" => new MergeStripPanel(ctx),
        "pdf-organize" => new PageGridPanel(ctx, PageGridRules.Organize),
        "pdf-remove-pages" => new PageGridPanel(ctx, PageGridRules.Remove),
        "pdf-rotate" => new PageGridPanel(ctx, PageGridRules.Rotate),
        "pdf-sign-self" => new SignPlacePanel(ctx),
        _ => null,
    };
}
