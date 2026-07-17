namespace NetieSpace.Helpers;

/// <summary>Shared liquid-glass scrollbar CSS for WebView2 HTML previews.</summary>
public static class GlassScrollbarCss
{
    public const string StyleBlock = """
        /* Netie liquid-glass scrollbars — thumb only, no arrows, no white track */
        *::-webkit-scrollbar { width: 8px; height: 8px; }
        *::-webkit-scrollbar-button { display: none; width: 0; height: 0; }
        *::-webkit-scrollbar-corner { background: transparent; }
        *::-webkit-scrollbar-track { background: transparent; }
        *::-webkit-scrollbar-thumb {
          background: rgba(255,255,255,0.22);
          border-radius: 999px;
          border: 2px solid transparent;
          background-clip: content-box;
        }
        *::-webkit-scrollbar-thumb:hover { background: rgba(255,255,255,0.36); background-clip: content-box; }
        * { scrollbar-width: thin; scrollbar-color: rgba(255,255,255,0.28) transparent; }
        """;
}
