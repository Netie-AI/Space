using System.IO;
using System.Net;
using System.Security.Cryptography;

namespace NetieSpace.Apps.Md;

/// <summary>What the page is born with: the colours and, when the exe bundles it, the highlighter.</summary>
public sealed record MdPageOptions(MdColorMode Color = MdColorMode.System, string? HighlightJs = null);

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

    public const string FindBar =
        "<div class=\"md-find\" id=\"md-find\" role=\"search\" hidden>" +
        "<input id=\"md-find-q\" type=\"text\" placeholder=\"Find in document\" aria-label=\"Find in document\" autocomplete=\"off\" spellcheck=\"false\">" +
        "<span class=\"md-find-count\" id=\"md-find-count\" aria-live=\"polite\"></span>" +
        "<button type=\"button\" data-find=\"prev\" aria-label=\"Previous match\">Prev</button>" +
        "<button type=\"button\" data-find=\"next\" aria-label=\"Next match\">Next</button>" +
        "<button type=\"button\" data-find=\"close\" aria-label=\"Close find\">Close</button></div>";

    public static string Page(string title, string bodyHtml, string nonce, MdPageOptions? options = null)
    {
        options ??= new MdPageOptions();
        var theme = MdColorModes.Attribute(options.Color);
        var themeAttr = theme is null ? "" : $" data-theme=\"{theme}\"";
        return "<!doctype html>\n<html lang=\"en\"" + themeAttr + "><head><meta charset=\"utf-8\">" +
               $"<meta http-equiv=\"Content-Security-Policy\" content=\"{Csp(nonce)}\">" +
               "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
               $"<title>{WebUtility.HtmlEncode(title)}</title>" +
               $"<style>{Css}</style></head><body><main class=\"md\">{bodyHtml}</main>{FindBar}" +
               $"<script nonce=\"{nonce}\">{options.HighlightJs}\n{Script}</script></body></html>";
    }

    /// <summary>A short page for a file that cannot be shown (too large, unreadable). The text is escaped.</summary>
    public static string Notice(string title, string message, string nonce, MdPageOptions? options = null) =>
        Page(title, $"<p class=\"md-notice\">{WebUtility.HtmlEncode(message)}</p>", nonce, options);

    /// <summary>
    /// The page's script. Clicks never navigate the page: anchors scroll, everything else goes to the host as
    /// "link:HREF". It also highlights code that names a language, copies code ("copy:TEXT"), finds text
    /// (Ctrl+F), reports the section in view ("section:ID") and the shortcuts the web view would swallow
    /// ("nav:back", "nav:forward", "zoom:in", "zoom:out", "zoom:reset", "ui:open", "ui:print", "ui:reload").
    /// </summary>
    public const string Script = """
(function(){
'use strict';
var host=window.chrome&&window.chrome.webview;
function post(m){if(host)host.postMessage(m);}
var root=document.querySelector('main.md');

if(window.hljs){
  Array.prototype.forEach.call(document.querySelectorAll('pre code[class^="language-"]'),function(c){
    var lang=c.className.split(' ')[0].slice(9);
    if(!lang||/nohighlight/.test(c.className)||!window.hljs.getLanguage(lang))return;
    try{window.hljs.highlightElement(c);}catch(e){}
  });
}

function jump(id){
  try{id=decodeURIComponent(id);}catch(x){}
  if(!id){window.scrollTo(0,0);return true;}
  var t=document.getElementById(id)||document.getElementById(id.toLowerCase());
  if(t){t.scrollIntoView();return true;}
  return false;
}

function copyCode(btn){
  var box=btn.closest('.md-code'),code=box&&box.querySelector('code');
  var text=code?code.textContent:'';
  post('copy:'+text);
  if(!host&&navigator.clipboard){try{navigator.clipboard.writeText(text);}catch(e){}}
  btn.textContent='Copied';btn.classList.add('md-copied');
  setTimeout(function(){btn.textContent='Copy';btn.classList.remove('md-copied');},1500);
}

var bar=document.getElementById('md-find'),input=document.getElementById('md-find-q'),countEl=document.getElementById('md-find-count');
var matches=[],marks=[],cur=-1,capped=false;
var BLOCK='p,li,td,th,h1,h2,h3,h4,h5,h6,pre,blockquote,dt,dd,div';
var MAX_HITS=5000;

function clearMarks(){
  Array.prototype.forEach.call(root.querySelectorAll('mark.md-hit'),function(m){
    var p=m.parentNode;
    while(m.firstChild)p.insertBefore(m.firstChild,m);
    p.removeChild(m);p.normalize();
  });
  matches=[];marks=[];cur=-1;capped=false;
}

function groups(){
  var list=[],map=new Map(),w=document.createTreeWalker(root,NodeFilter.SHOW_TEXT,null),n;
  while((n=w.nextNode())){
    var p=n.parentNode;
    if(p.closest('.md-code-bar,.md-anchor,script,style'))continue;
    var blk=p.closest(BLOCK)||root,g=map.get(blk);
    if(!g){g={nodes:[],text:''};map.set(blk,g);list.push(g);}
    g.nodes.push({n:n,s:g.text.length,l:n.nodeValue.length});
    g.text+=n.nodeValue;
  }
  return list;
}

function wrap(m,idx){
  for(var k=m.g.nodes.length-1;k>=0;k--){
    var nd=m.g.nodes[k],a=Math.max(m.s,nd.s)-nd.s,b=Math.min(m.e,nd.s+nd.l)-nd.s;
    if(a>=b)continue;
    var mid=nd.n;
    if(a>0)mid=nd.n.splitText(a);
    if(b-a<mid.nodeValue.length)mid.splitText(b-a);
    var mk=document.createElement('mark');
    mk.className='md-hit';mk.setAttribute('data-m',idx);
    mid.parentNode.insertBefore(mk,mid);mk.appendChild(mid);
  }
}

function report(){
  if(!input.value){countEl.textContent='';return;}
  countEl.textContent=matches.length===0?'No matches':(cur+1)+' of '+matches.length+(capped?'+':'');
}

function select(i){
  if(!matches.length)return;
  if(cur>=0&&marks[cur])marks[cur].forEach(function(m){m.classList.remove('md-hit-cur');});
  cur=((i%matches.length)+matches.length)%matches.length;
  marks[cur].forEach(function(m){m.classList.add('md-hit-cur');});
  marks[cur][0].scrollIntoView({block:'center'});
  report();
}

function run(q){
  clearMarks();
  if(q){
    var re=new RegExp(q.replace(/[.*+?^${}()|[\]\\]/g,'\\$&'),'gi');
    groups().forEach(function(g){
      var m;re.lastIndex=0;
      while(matches.length<MAX_HITS&&(m=re.exec(g.text))!==null){
        if(m[0].length===0){re.lastIndex++;continue;}
        matches.push({g:g,s:m.index,e:m.index+m[0].length});
      }
      if(matches.length>=MAX_HITS)capped=true;
    });
    for(var i=matches.length-1;i>=0;i--)wrap(matches[i],i);
    for(var j=0;j<matches.length;j++)marks.push([]);
    Array.prototype.forEach.call(root.querySelectorAll('mark.md-hit'),function(m){marks[+m.getAttribute('data-m')].push(m);});
    if(matches.length)select(0);
  }
  report();
}

function openFind(q){
  bar.hidden=false;
  if(typeof q==='string'){input.value=q;run(q);}
  else if(input.value&&!matches.length)run(input.value);
  input.focus();input.select();
}
function closeFind(){clearMarks();bar.hidden=true;countEl.textContent='';input.blur();}
function step(d){if(matches.length)select(cur+d);}
function findAction(a){if(a==='next')step(1);else if(a==='prev')step(-1);else if(a==='close')closeFind();}

input.addEventListener('input',function(){run(input.value);});
input.addEventListener('keydown',function(e){
  if(e.key==='Enter'){e.preventDefault();step(e.shiftKey?-1:1);}
  else if(e.key==='Escape'){e.preventDefault();closeFind();}
});

document.addEventListener('click',function(e){
  var t=e.target;
  if(!t||!t.closest)return;
  var b=t.closest('button.md-copy');
  if(b){copyCode(b);return;}
  var f=t.closest('[data-find]');
  if(f){findAction(f.getAttribute('data-find'));return;}
  var a=t.closest('a');
  if(!a)return;
  e.preventDefault();
  var h=a.getAttribute('href');
  if(!h)return;
  if(h.charAt(0)==='#'){jump(h.slice(1));return;}
  post('link:'+h);
});

document.addEventListener('keydown',function(e){
  var k=e.key,c=e.ctrlKey||e.metaKey;
  if(c&&!e.altKey&&(k==='f'||k==='F')){e.preventDefault();openFind();return;}
  if(k==='F3'||(c&&(k==='g'||k==='G'))){if(!bar.hidden){e.preventDefault();step(e.shiftKey?-1:1);}return;}
  if(e.altKey&&k==='ArrowLeft'){e.preventDefault();post('nav:back');return;}
  if(e.altKey&&k==='ArrowRight'){e.preventDefault();post('nav:forward');return;}
  if(c&&(k==='+'||k==='=')){e.preventDefault();post('zoom:in');return;}
  if(c&&(k==='-'||k==='_')){e.preventDefault();post('zoom:out');return;}
  if(c&&k==='0'){e.preventDefault();post('zoom:reset');return;}
  if(c&&(k==='o'||k==='O')){e.preventDefault();post('ui:open');return;}
  if(c&&(k==='p'||k==='P')){e.preventDefault();post('ui:print');return;}
  if(k==='F5'){e.preventDefault();post('ui:reload');}
});
document.addEventListener('mouseup',function(e){
  if(e.button===3){e.preventDefault();post('nav:back');}
  else if(e.button===4){e.preventDefault();post('nav:forward');}
});

var heads=Array.prototype.slice.call(root.querySelectorAll('h1[id],h2[id],h3[id],h4[id],h5[id],h6[id]'));
var lastSection=null,queued=false;
function section(){
  queued=false;
  var line=window.innerHeight*0.25,now='';
  for(var i=0;i<heads.length;i++){
    if(heads[i].getBoundingClientRect().top<=line)now=heads[i].id;else break;
  }
  if(window.scrollY>0&&window.innerHeight+window.scrollY>=document.documentElement.scrollHeight-2){
    for(var j=heads.length-1;j>=0;j--){if(heads[j].getBoundingClientRect().top<window.innerHeight){now=heads[j].id;break;}}
  }
  if(now!==lastSection){lastSection=now;post('section:'+now);}
}
function queue(){if(!queued){queued=true;window.requestAnimationFrame(section);}}
window.addEventListener('scroll',queue,{passive:true});
window.addEventListener('resize',queue);
window.addEventListener('load',queue);
queue();

window.netieMd={find:openFind,closeFind:closeFind,section:function(){return lastSection;}};
})();
""";

    private const string LightVars =
        "--bg:#fff;--fg:#2c2c2c;--muted:#6e6e6e;--line:#d6d6d6;--line2:#e6e6e6;--border:#bdbdbd;--panel:#f4f5f7;" +
        "--link:#1d7f74;--accent:#5ec4b8;--mark:#ffe58a;--mark-cur:#ff9f43;--h-kw:#cf222e;--h-str:#0a3069;--h-com:#6e7781;" +
        "--h-num:#0550ae;--h-fn:#8250df;--h-bi:#953800;--a-note:#0969da;--a-tip:#1a7f37;--a-important:#8250df;" +
        "--a-warning:#9a6700;--a-caution:#cf222e;";

    private const string DarkVars =
        "--bg:#16181c;--fg:#d9dce1;--muted:#9aa0aa;--line:#3a3f47;--line2:#2c3036;--border:#4a505a;--panel:#1f2227;" +
        "--link:#5ec4b8;--accent:#5ec4b8;--mark:#5c4b00;--mark-cur:#b35c00;--h-kw:#ff7b72;--h-str:#a5d6ff;--h-com:#8b949e;" +
        "--h-num:#79c0ff;--h-fn:#d2a8ff;--h-bi:#ffa657;--a-note:#4493f8;--a-tip:#3fb950;--a-important:#ab7df8;" +
        "--a-warning:#d29922;--a-caution:#f85149;";

    public const string Css =
        ":root{" + LightVars + "color-scheme:light dark}" +
        "@media (prefers-color-scheme:dark){:root:not([data-theme=light]){" + DarkVars + "}}" +
        ":root[data-theme=dark]{" + DarkVars + "color-scheme:dark}" +
        ":root[data-theme=light]{color-scheme:light}" +
        "html{-webkit-text-size-adjust:100%}" +
        "body{margin:0;background:var(--bg);color:var(--fg);font:15px/1.65 Bahnschrift,'Segoe UI Variable Text','Segoe UI',Helvetica,Arial,sans-serif}" +
        ".md{max-width:860px;margin:0 auto;padding:32px 40px 72px;overflow-wrap:anywhere}" +
        ".md h1,.md h2,.md h3,.md h4,.md h5,.md h6{font-family:Bahnschrift,'Segoe UI Variable Display','Segoe UI',Helvetica,Arial,sans-serif;line-height:1.25;margin:1.6em 0 .6em;scroll-margin-top:16px;position:relative}" +
        ".md h1{font-size:2em;border-bottom:1px solid var(--line);padding-bottom:.3em;margin-top:.2em}" +
        ".md h2{font-size:1.5em;border-bottom:1px solid var(--line2);padding-bottom:.25em}" +
        ".md h3{font-size:1.25em}.md h4{font-size:1.05em}.md h5,.md h6{font-size:.95em;color:var(--muted)}" +
        ".md .md-anchor{margin-left:.4em;color:var(--muted);text-decoration:none;font-weight:400;opacity:0;user-select:none}" +
        ".md h1:hover .md-anchor,.md h2:hover .md-anchor,.md h3:hover .md-anchor,.md h4:hover .md-anchor,.md h5:hover .md-anchor,.md h6:hover .md-anchor,.md .md-anchor:focus{opacity:1}" +
        ".md p,.md ul,.md ol,.md blockquote,.md pre,.md table,.md dl{margin:0 0 1em}" +
        ".md a{color:var(--link);text-decoration:underline;text-underline-offset:2px}" +
        ".md a.md-blocked-link{color:var(--muted);text-decoration:none;cursor:default}" +
        ".md img{max-width:100%;height:auto;border-radius:4px}" +
        ".md ul,.md ol{padding-left:1.8em}.md li+li{margin-top:.25em}" +
        ".md ul.contains-task-list{list-style:none;padding-left:.4em}" +
        ".md li.task-list-item input{margin:0 .5em 0 0;vertical-align:-1px}" +
        ".md blockquote{margin-left:0;padding:.2em 1em;border-left:3px solid var(--accent);background:var(--panel);color:var(--fg)}" +
        ".md blockquote>:last-child{margin-bottom:0}" +
        ".md code{font:.9em Consolas,'Cascadia Mono','Courier New',monospace;background:var(--panel);border:1px solid var(--line2);border-radius:4px;padding:.1em .35em}" +
        ".md .md-code{margin:0 0 1em;border:1px solid var(--line);border-radius:6px;background:var(--panel);overflow:hidden}" +
        ".md .md-code-bar{display:flex;align-items:center;gap:10px;padding:4px 8px 4px 12px;border-bottom:1px solid var(--line2);font-size:.8em;color:var(--muted)}" +
        ".md .md-lang{text-transform:lowercase;letter-spacing:.03em}" +
        ".md .md-note{flex:1;font-style:italic}" +
        ".md .md-code-bar .md-copy{margin-left:auto;font:inherit;color:var(--muted);background:transparent;border:1px solid var(--line);border-radius:4px;padding:1px 8px;cursor:pointer}" +
        ".md .md-code-bar .md-note+.md-copy{margin-left:0}" +
        ".md .md-code-bar .md-copy:hover{color:var(--fg);border-color:var(--border)}" +
        ".md .md-code-bar .md-copy.md-copied{color:var(--a-tip);border-color:var(--a-tip)}" +
        ".md .md-code pre{margin:0;border:0;border-radius:0;background:transparent;padding:12px 14px;overflow:auto}" +
        ".md pre{background:var(--panel);border:1px solid var(--line);border-radius:4px;padding:12px 14px;overflow:auto}" +
        ".md pre code{background:none;border:0;padding:0;font-size:.88em;white-space:pre}" +
        ".md .md-mermaid{border-style:dashed}" +
        ".hljs-keyword,.hljs-selector-tag,.hljs-literal,.hljs-section,.hljs-doctag,.hljs-meta .hljs-keyword{color:var(--h-kw)}" +
        ".hljs-string,.hljs-regexp,.hljs-meta .hljs-string,.hljs-addition{color:var(--h-str)}" +
        ".hljs-comment,.hljs-quote,.hljs-deletion{color:var(--h-com);font-style:italic}" +
        ".hljs-number,.hljs-attr,.hljs-attribute,.hljs-symbol,.hljs-bullet,.hljs-variable,.hljs-template-variable,.hljs-selector-id,.hljs-selector-class{color:var(--h-num)}" +
        ".hljs-title,.hljs-title.function_,.hljs-name,.hljs-type.class_,.hljs-title.class_{color:var(--h-fn)}" +
        ".hljs-built_in,.hljs-type,.hljs-params,.hljs-meta{color:var(--h-bi)}" +
        ".hljs-emphasis{font-style:italic}.hljs-strong{font-weight:700}" +
        ".md table{border-collapse:collapse;display:block;max-width:100%;overflow:auto}" +
        ".md th,.md td{border:1px solid var(--border);padding:6px 12px;text-align:left}" +
        ".md th{background:var(--panel);font-weight:600}" +
        ".md hr{border:0;border-top:1px solid var(--line);margin:1.8em 0}" +
        ".md .markdown-alert{margin:0 0 1em;padding:.4em 1em;border-left:4px solid var(--a-note);background:var(--panel);border-radius:0 4px 4px 0}" +
        ".md .markdown-alert>:last-child{margin-bottom:0}" +
        ".md .markdown-alert-title{display:flex;align-items:center;gap:8px;margin:0 0 .4em;font-weight:600;color:var(--a-note)}" +
        ".md .markdown-alert-title svg{fill:currentColor;flex:none}" +
        ".md .markdown-alert-tip{border-left-color:var(--a-tip)}.md .markdown-alert-tip .markdown-alert-title{color:var(--a-tip)}" +
        ".md .markdown-alert-important{border-left-color:var(--a-important)}.md .markdown-alert-important .markdown-alert-title{color:var(--a-important)}" +
        ".md .markdown-alert-warning{border-left-color:var(--a-warning)}.md .markdown-alert-warning .markdown-alert-title{color:var(--a-warning)}" +
        ".md .markdown-alert-caution{border-left-color:var(--a-caution)}.md .markdown-alert-caution .markdown-alert-title{color:var(--a-caution)}" +
        ".md .footnotes{font-size:.9em;color:var(--muted)}" +
        ".md-pic{display:inline-block;border:1px dashed var(--border);border-radius:4px;padding:2px 8px;color:var(--muted);font-size:.9em}" +
        ".md-tag{font-size:.78em;text-transform:uppercase;letter-spacing:.04em;color:var(--muted)}" +
        ".md-notice{color:var(--muted);font-size:1.05em;padding:24px 0}" +
        "mark.md-hit{background:var(--mark);color:inherit;border-radius:2px}" +
        "mark.md-hit.md-hit-cur{background:var(--mark-cur);outline:1px solid var(--mark-cur)}" +
        ".md-find{position:fixed;top:12px;right:20px;z-index:10;display:flex;align-items:center;gap:6px;padding:6px 8px;background:var(--panel);color:var(--fg);border:1px solid var(--border);border-radius:8px;box-shadow:0 2px 10px rgba(0,0,0,.25);font-size:13px}" +
        ".md-find[hidden]{display:none}" +
        ".md-find input{width:200px;font:inherit;color:var(--fg);background:var(--bg);border:1px solid var(--border);border-radius:4px;padding:3px 8px}" +
        ".md-find-count{min-width:64px;color:var(--muted)}" +
        ".md-find button{font:inherit;color:var(--fg);background:var(--bg);border:1px solid var(--border);border-radius:4px;padding:2px 8px;cursor:pointer}" +
        "@media print{" +
        ":root:root:root{" + LightVars + "color-scheme:light}" +
        "html,body{background:#fff}" +
        ".md{max-width:none;padding:0}" +
        ".md,.md pre,.md .md-code,.md th,.md blockquote,.md .markdown-alert,.md code{-webkit-print-color-adjust:exact;print-color-adjust:exact}" +
        ".md-find,.md .md-copy,.md .md-anchor{display:none!important}" +
        "mark.md-hit{background:none!important;outline:0!important}" +
        ".md pre code{white-space:pre-wrap}" +
        ".md pre,.md .md-code,.md table,.md img,.md blockquote,.md .markdown-alert,.md tr{break-inside:avoid}" +
        ".md h1,.md h2,.md h3,.md h4,.md h5,.md h6{break-after:avoid}" +
        ".md a{color:var(--link)}" +
        "}";
}
