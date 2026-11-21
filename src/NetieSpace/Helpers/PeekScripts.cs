namespace NetieSpace.Helpers;

/// <summary>
/// Small scripts the peek injects into HTML pages it renders (text, markdown, Word, Excel, HTML).
/// Plain strings so xunit can pin the contract on Linux; the page behaviour is judged from the desktop job.
/// </summary>
public static class PeekScripts
{
    /// <summary>
    /// Find in page. window.__netieFind.run(q) wraps matches in mark.netie-find and returns {count,index};
    /// step(dir) moves to the next/previous match with wrap and scrolls it to the middle; clear() unwraps.
    /// </summary>
    public const string Find = """
        (function () {
          if (window.__netieFind) return;
          var CLS = 'netie-find', marks = [], cur = -1;
          var st = document.createElement('style');
          st.textContent = 'mark.netie-find{background:#ffe066;color:#111;border-radius:2px;padding:0}' +
            'mark.netie-find.netie-cur{background:#ff9f1a;outline:2px solid #ff9f1a}';
          (document.head || document.documentElement).appendChild(st);
          function report() { return { count: marks.length, index: cur }; }
          function clear() {
            for (var i = 0; i < marks.length; i++) {
              var m = marks[i], p = m.parentNode;
              if (!p) continue;
              p.replaceChild(document.createTextNode(m.textContent), m);
              p.normalize();
            }
            marks = []; cur = -1;
          }
          function nodes() {
            var out = [], w = document.createTreeWalker(document.body || document.documentElement, NodeFilter.SHOW_TEXT, null), n;
            while ((n = w.nextNode())) {
              var t = n.parentNode && n.parentNode.nodeName;
              if (!t || t === 'SCRIPT' || t === 'STYLE' || t === 'NOSCRIPT' || t === 'TEXTAREA') continue;
              if (n.nodeValue && n.nodeValue.trim()) out.push(n);
            }
            return out;
          }
          function show(i) {
            if (cur >= 0 && marks[cur]) marks[cur].classList.remove('netie-cur');
            cur = i;
            var m = marks[cur];
            if (m) { m.classList.add('netie-cur'); m.scrollIntoView({ block: 'center', inline: 'nearest' }); }
          }
          window.__netieFind = {
            run: function (q) {
              clear();
              var needle = String(q || '').toLowerCase();
              if (!needle.trim()) return report();
              var list = nodes();
              for (var i = 0; i < list.length; i++) {
                var node = list[i], txt = node.nodeValue, low = txt.toLowerCase();
                if (low.length !== txt.length) continue;
                var idx = low.indexOf(needle);
                if (idx < 0) continue;
                var frag = document.createDocumentFragment(), pos = 0;
                while (idx >= 0) {
                  if (idx > pos) frag.appendChild(document.createTextNode(txt.slice(pos, idx)));
                  var mk = document.createElement('mark');
                  mk.className = CLS;
                  mk.textContent = txt.slice(idx, idx + needle.length);
                  frag.appendChild(mk);
                  marks.push(mk);
                  pos = idx + needle.length;
                  idx = low.indexOf(needle, pos);
                }
                if (pos < txt.length) frag.appendChild(document.createTextNode(txt.slice(pos)));
                node.parentNode.replaceChild(frag, node);
              }
              if (marks.length) show(0);
              return report();
            },
            step: function (dir) {
              if (!marks.length) return report();
              show((cur + (dir < 0 ? -1 : 1) + marks.length) % marks.length);
              return report();
            },
            clear: function () { clear(); return report(); }
          };
        })();
        """;

    /// <summary>
    /// Keys the WebView would swallow while it holds focus (browser accelerators are off, so Ctrl+F and
    /// Ctrl +/- do nothing by themselves). Posts {type:'peekKey', key} to the host, which owns the action.
    /// </summary>
    public const string KeyBridge = """
        (function () {
          if (window.__netieKeyBridge) return;
          window.__netieKeyBridge = true;
          function typing(el) {
            if (!el) return false;
            var tag = (el.tagName || '').toUpperCase();
            return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || !!el.isContentEditable;
          }
          document.addEventListener('keydown', function (e) {
            var k = e.key, ctrl = (e.ctrlKey || e.metaKey) && !e.altKey, msg = null;
            if (ctrl && (k === 'f' || k === 'F')) msg = 'find';
            else if (ctrl && (k === '+' || k === '=')) msg = 'zoomIn';
            else if (ctrl && (k === '-' || k === '_')) msg = 'zoomOut';
            else if (ctrl && k === '0') msg = 'zoomReset';
            else if (!ctrl && !typing(e.target) && k === '?') msg = 'help';
            else if (!ctrl && k === 'F3') msg = e.shiftKey ? 'findPrev' : 'findNext';
            if (!msg) return;
            e.preventDefault();
            e.stopPropagation();
            try { chrome.webview.postMessage(JSON.stringify({ type: 'peekKey', key: msg })); } catch (_) { }
          }, true);
        })();
        """;
}
