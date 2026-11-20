using System.IO;
using System.Net;
using System.Security.Cryptography;

namespace NetieSpace.Apps.Md;

/// <summary>
/// The HTML page around a rendered document: an escaped title, a content security policy that allows
/// no script but the page's own (by nonce), no network and pictures only from the document host, and
/// the stylesheet. Pure string work.
/// </summary>
public static class MdShell
{
    /// <summary>Navigation and request rules for the host (WebView2 reads these).</summary>
    public static bool IsAllowedRequest(string uri) =>
        uri.StartsWith(MdPictures.HostOrigin, StringComparison.OrdinalIgnoreCase)
        || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri, "about:blank", StringComparison.OrdinalIgnoreCase);

    public static string NewNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public static string Csp(string nonce) =>
        "default-src 'none'; " +
        "img-src " + MdPictures.HostOrigin + " data:; " +
        "style-src 'unsafe-inline'; " +
        $"script-src 'nonce-{nonce}'; " +
        "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    public static string Page(string title, string bodyHtml, string nonce) =>
        "<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">" +
        $"<meta http-equiv=\"Content-Security-Policy\" content=\"{Csp(nonce)}\">" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
        $"<title>{WebUtility.HtmlEncode(title)}</title>" +
        $"<style>{Css}</style></head><body><main class=\"md\">{bodyHtml}</main>" +
        $"<script nonce=\"{nonce}\">{Script}</script></body></html>";

    /// <summary>A short page for a file that cannot be shown (too large, unreadable). The text is escaped.</summary>
    public static string Notice(string title, string message, string nonce) =>
        Page(title, $"<p class=\"md-notice\">{WebUtility.HtmlEncode(message)}</p>", nonce);

    /// <summary>Clicks never navigate the page: anchors scroll, everything else goes to the host.</summary>
    public const string Script =
        "document.addEventListener('click',function(e){var a=e.target.closest&&e.target.closest('a');if(!a)return;" +
        "e.preventDefault();var h=a.getAttribute('href');if(!h)return;" +
        "if(h.charAt(0)==='#'){var id=h.slice(1),t=null;try{id=decodeURIComponent(id)}catch(x){}" +
        "t=document.getElementById(id);if(t)t.scrollIntoView();return}" +
        "if(window.chrome&&window.chrome.webview)window.chrome.webview.postMessage('link:'+h)});";

    public const string Css =
        "html{-webkit-text-size-adjust:100%}" +
        "body{margin:0;background:#fff;color:#2c2c2c;font:15px/1.65 Bahnschrift,'Segoe UI Variable Text','Segoe UI',Helvetica,Arial,sans-serif}" +
        ".md{max-width:860px;margin:0 auto;padding:32px 40px 72px;overflow-wrap:anywhere}" +
        ".md h1,.md h2,.md h3,.md h4,.md h5,.md h6{font-family:Bahnschrift,'Segoe UI Variable Display','Segoe UI',Helvetica,Arial,sans-serif;line-height:1.25;margin:1.6em 0 .6em;scroll-margin-top:16px}" +
        ".md h1{font-size:2em;border-bottom:1px solid #d6d6d6;padding-bottom:.3em;margin-top:.2em}" +
        ".md h2{font-size:1.5em;border-bottom:1px solid #e6e6e6;padding-bottom:.25em}" +
        ".md h3{font-size:1.25em}.md h4{font-size:1.05em}.md h5,.md h6{font-size:.95em;color:#6e6e6e}" +
        ".md p,.md ul,.md ol,.md blockquote,.md pre,.md table,.md dl{margin:0 0 1em}" +
        ".md a{color:#1d7f74;text-decoration:underline;text-underline-offset:2px}" +
        ".md a.md-blocked-link{color:#6e6e6e;text-decoration:none;cursor:default}" +
        ".md img{max-width:100%;height:auto;border-radius:4px}" +
        ".md ul,.md ol{padding-left:1.8em}.md li+li{margin-top:.25em}" +
        ".md ul.contains-task-list{list-style:none;padding-left:.4em}" +
        ".md li.task-list-item input{margin:0 .5em 0 0;vertical-align:-1px}" +
        ".md blockquote{margin-left:0;padding:.2em 1em;border-left:3px solid #5ec4b8;background:#f4f5f7;color:#454545}" +
        ".md blockquote>:last-child{margin-bottom:0}" +
        ".md code{font:.9em Consolas,'Cascadia Mono','Courier New',monospace;background:#f4f5f7;border:1px solid #e6e6e6;border-radius:4px;padding:.1em .35em}" +
        ".md pre{background:#f4f5f7;border:1px solid #d6d6d6;border-radius:4px;padding:12px 14px;overflow:auto}" +
        ".md pre code{background:none;border:0;padding:0;font-size:.88em;white-space:pre}" +
        ".md table{border-collapse:collapse;display:block;max-width:100%;overflow:auto}" +
        ".md th,.md td{border:1px solid #bdbdbd;padding:6px 12px;text-align:left}" +
        ".md th{background:#f4f5f7;font-weight:600}" +
        ".md hr{border:0;border-top:1px solid #d6d6d6;margin:1.8em 0}" +
        ".md-pic{display:inline-block;border:1px dashed #bdbdbd;border-radius:4px;padding:2px 8px;color:#6e6e6e;font-size:.9em}" +
        ".md-tag{font-size:.78em;text-transform:uppercase;letter-spacing:.04em;color:#6e6e6e}" +
        ".md-notice{color:#6e6e6e;font-size:1.05em;padding:24px 0}";
}
