namespace NetieSpace.Apps.Md;

/// <summary>
/// Turns Markdown into an HTML fragment. The implementation (Markdig) lives in the exe only, so the
/// pure parts here stay free of it. It must not pass raw HTML through: <see cref="MdHtml"/> assumes
/// that every tag it sees was written by the engine.
/// </summary>
public interface IMdEngine
{
    string ToHtml(string markdown);
}
