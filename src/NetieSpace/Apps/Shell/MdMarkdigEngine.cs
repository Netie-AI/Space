using Markdig;
using NetieSpace.Apps.Md;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// The Markdig side of Netie MD. It lives here, in the exe only, so the Markdown-free parts under Apps/Md
/// stay testable without the package. Raw HTML is disabled: a tag written in a document is shown as text.
/// Only the extensions the viewer shows are on; generic attributes, math and media links are not.
/// </summary>
public sealed class MdMarkdigEngine : IMdEngine
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseGridTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .UseDefinitionLists()
        .UseFootnotes()
        .UseListExtras()
        .DisableHtml()
        .Build();

    public string ToHtml(string markdown) => Markdown.ToHtml(markdown, Pipeline);
}
