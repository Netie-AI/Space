using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace NetieSpace.Apps.Md.WinRt;

/// <summary>
/// Runs a script of reader actions against a real MdView in a real WebView2 and prints one line per step:
/// <c>STEP_OK [label] command</c> or <c>STEP_FAIL [label] command :: why</c>. It stops at the first failure.
/// Clicks and keys go through the browser's input pipeline (DevTools Input.dispatch*), toolbar buttons through
/// UI Automation Invoke, so the wiring a reader uses is the wiring under test. One step per line,
/// <c>[label] command arguments</c>; lines starting with # are comments. See scripts/checks.d/md.ps1.
/// </summary>
public sealed class MdDriver
{
    private readonly MdView _view;
    private readonly TextWriter _log;
    private readonly List<string> _asked = new();
    private readonly List<string> _launched = new();
    private string? _nextPick;
    private bool _allowExternal;
    private string _baseFolder = "";

    public MdDriver(MdView view, TextWriter log)
    {
        _view = view;
        _log = log;
        _view.ConfirmExternal = url => { _asked.Add(url); return _allowExternal; };
        _view.Launcher = info =>
        {
            _launched.Add(info.FileName + " " + string.Join(' ', info.ArgumentList));
            return true;
        };
        _view.PickFile = () => _nextPick;
    }

    private static readonly Dictionary<string, (string Key, string Code, int Vk)> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["left"] = ("ArrowLeft", "ArrowLeft", 37),
        ["right"] = ("ArrowRight", "ArrowRight", 39),
        ["f"] = ("f", "KeyF", 70),
        ["enter"] = ("Enter", "Enter", 13),
        ["escape"] = ("Escape", "Escape", 27),
        ["f3"] = ("F3", "F3", 114),
        ["0"] = ("0", "Digit0", 48),
    };

    /// <returns>True when every step passed.</returns>
    public async Task<bool> RunAsync(IEnumerable<string> lines)
    {
        var core = _view.Core ?? throw new InvalidOperationException("web view not ready");
        _baseFolder = IoPath.GetDirectoryName(_view.CurrentPath ?? "") ?? "";
        await core.CallDevToolsProtocolMethodAsync("Emulation.setFocusEmulationEnabled", "{\"enabled\":true}");
        var n = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            n++;
            var label = "";
            if (line[0] == '[')
            {
                var close = line.IndexOf(']');
                if (close > 0)
                {
                    label = line[1..close];
                    line = line[(close + 1)..].TrimStart();
                }
            }
            var space = line.IndexOf(' ');
            var cmd = space < 0 ? line : line[..space];
            var arg = space < 0 ? "" : line[(space + 1)..].Trim();
            string? failure;
            try
            {
                failure = await StepAsync(core, cmd, arg);
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name + ": " + ex.Message;
            }
            var tag = label.Length == 0 ? "" : "[" + label + "] ";
            if (failure is null)
            {
                _log.WriteLine($"STEP_OK {tag}{cmd} {arg}".TrimEnd());
            }
            else
            {
                _log.WriteLine($"STEP_FAIL {tag}{cmd} {arg} :: {failure}".Replace("\r", " ").Replace("\n", " "));
                return false;
            }
        }
        _log.WriteLine($"STEPS_DONE {n}");
        return true;
    }

    private string Full(string path) => IoPath.IsPathRooted(path) ? path : IoPath.Combine(_baseFolder, path);

    private static string? Equal(string what, string expected, string actual) =>
        string.Equals(expected, actual, StringComparison.Ordinal) ? null : $"{what}: expected '{expected}' got '{actual}'";

    private async Task<string> EvalTextAsync(string js)
    {
        var raw = await _view.EvalAsync(js);
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString() ?? "" : raw;
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    /// <summary>Polls until the condition holds; null on success, the description on timeout.</summary>
    private static async Task<bool> WaitAsync(Func<Task<bool>> condition, double seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            if (await condition())
                return true;
            await Task.Delay(100);
        }
        return await condition();
    }

    private async Task<string?> StepAsync(CoreWebView2 core, string cmd, string arg)
    {
        switch (cmd)
        {
            case "wait-doc":
            {
                var parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var ok = await WaitAsync(() => Task.FromResult(_view.Document is { Error: null } d && d.Title == parts[0]), 15);
                if (!ok)
                    return $"document is '{_view.Document?.Title}' error='{_view.Document?.Error}'";
                await _view.WaitRenderedAsync();
                return null;
            }
            case "expect-doc":
                return Equal("document", arg, _view.Document?.Title ?? "(none)");
            case "expect-doc-error":
                return _view.Document?.Error is { } e && e.Contains(arg, StringComparison.OrdinalIgnoreCase) ? null : $"error is '{_view.Document?.Error}'";
            case "invoke":
                return _view.InvokeButton(arg) ? null : $"button {arg} missing or disabled";
            case "expect-enabled":
                return _view.ButtonEnabled(arg) == true ? null : $"button {arg} is not enabled";
            case "expect-disabled":
                return _view.ButtonEnabled(arg) == false ? null : $"button {arg} is not disabled";
            case "click-link":
                return await ClickAsync(core, "a", arg);
            case "click-selector":
                return await ClickAsync(core, arg, null);
            case "key":
                return await KeyAsync(core, arg);
            case "type":
                await core.CallDevToolsProtocolMethodAsync("Input.insertText", JsonSerializer.Serialize(new { text = arg }));
                return null;
            case "find":
                return await KeyAsync(core, "ctrl+f") ?? await StepAsync(core, "type", arg);
            case "expect-js":
            {
                var sep = arg.IndexOf(" :: ", StringComparison.Ordinal);
                if (sep < 0)
                    return "needs 'expected :: javascript'";
                var expected = arg[..sep];
                var js = arg[(sep + 4)..];
                string actual = "";
                var ok = await WaitAsync(async () => (actual = await EvalTextAsync(js)) == expected, 3);
                return ok ? null : $"expected '{expected}' got '{actual}' for {js}";
            }
            case "eval":
                await _view.EvalAsync(arg);
                return null;
            case "expect-section":
            {
                var ok = await WaitAsync(() => Task.FromResult(_view.CurrentSection == arg), 3);
                return ok ? null : $"section is '{_view.CurrentSection}'";
            }
            case "expect-zoom":
            {
                var ok = await WaitAsync(() => Task.FromResult(Math.Abs(_view.ZoomFactor - double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture)) < 0.01), 3);
                return ok ? null : $"zoom is {_view.ZoomFactor}";
            }
            case "set-theme":
                if (!MdColorModes.TryParse(arg, out var mode))
                    return "unknown mode";
                _view.SetColorMode(mode);
                return null;
            case "expect-theme":
                return Equal("mode", arg, MdColorModes.Label(_view.ColorMode).ToLowerInvariant());
            case "emulate-media":
                await core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", JsonSerializer.Serialize(new { media = arg == "none" ? "" : arg }));
                return null;
            case "append":
            {
                var at = arg.IndexOf(' ');
                if (at < 0)
                    return "needs 'file text'";
                File.AppendAllText(Full(arg[..at]), "\n" + arg[(at + 1)..] + "\n");
                return null;
            }
            case "wait-text":
            {
                var ok = await WaitAsync(async () => (await EvalTextAsync("document.body.innerText")).Contains(arg, StringComparison.Ordinal), 12);
                return ok ? null : "text did not appear after the file changed";
            }
            case "drop":
                _view.OpenDropped(new[] { Full(arg) });
                return null;
            case "set-pick":
                _nextPick = Full(arg);
                return null;
            case "answer-external":
                _allowExternal = arg.Equals("yes", StringComparison.OrdinalIgnoreCase);
                return null;
            // UI Automation Invoke and page messages reach the view a moment later: poll, do not read once.
            case "expect-asked":
                return await WaitAsync(() => Task.FromResult(_asked.Any(a => a.Contains(arg, StringComparison.Ordinal))), 3) ? null : $"asked: [{string.Join(", ", _asked)}]";
            case "expect-launched":
                return await WaitAsync(() => Task.FromResult(_launched.Any(a => a.Contains(arg, StringComparison.Ordinal))), 3) ? null : $"launched: [{string.Join(", ", _launched)}]";
            case "expect-not-launched":
                await Task.Delay(500);
                return _launched.Count == 0 ? null : $"launched: [{string.Join(", ", _launched)}]";
            case "expect-clipboard":
                return Clipboard.ContainsText() && Clipboard.GetText().Contains(arg, StringComparison.Ordinal) ? null : "clipboard does not hold the text";
            case "expect-status":
            {
                var ok = await WaitAsync(() => Task.FromResult(_view.StatusText.Contains(arg, StringComparison.OrdinalIgnoreCase)), 3);
                return ok ? null : $"status is '{_view.StatusText}'";
            }
            case "snapshot":
                await _view.WaitRenderedAsync();
                await _view.CapturePngAsync(Full(arg));
                return null;
            case "window":
                await _view.WaitRenderedAsync();
                await _view.CaptureWindowPngAsync(Full(arg));
                return null;
            case "pdf":
                return await _view.PrintPdfAsync(Full(arg)) ? null : "print to PDF failed";
            case "sleep":
                await Task.Delay(int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture));
                return null;
            default:
                return "unknown step";
        }
    }

    private async Task<string?> ClickAsync(CoreWebView2 core, string selector, string? text)
    {
        var js = "(function(sel,t){var l=document.querySelectorAll(sel);for(var i=0;i<l.length;i++){" +
                 "if(t===null||l[i].textContent.trim()===t){l[i].scrollIntoView({block:'center'});var r=l[i].getBoundingClientRect();" +
                 "return [r.left+r.width/2,r.top+r.height/2];}}return null;})(" +
                 JsonSerializer.Serialize(selector) + "," + (text is null ? "null" : JsonSerializer.Serialize(text)) + ")";
        var raw = await _view.EvalAsync(js);
        if (raw == "null")
            return $"nothing to click for '{text ?? selector}'";
        using var point = JsonDocument.Parse(raw);
        var x = point.RootElement[0].GetDouble();
        var y = point.RootElement[1].GetDouble();
        foreach (var type in new[] { "mouseMoved", "mousePressed", "mouseReleased" })
        {
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new
            {
                type,
                x,
                y,
                button = type == "mouseMoved" ? "none" : "left",
                clickCount = type == "mouseMoved" ? 0 : 1,
            }));
        }
        return null;
    }

    private static async Task<string?> KeyAsync(CoreWebView2 core, string combo)
    {
        var modifiers = 0;
        string? key = null;
        foreach (var part in combo.Split('+'))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt": modifiers |= 1; break;
                case "ctrl": modifiers |= 2; break;
                case "shift": modifiers |= 8; break;
                default: key = part; break;
            }
        }
        if (key is null || !Keys.TryGetValue(key, out var k))
            return $"unknown key '{combo}'";
        foreach (var type in new[] { "rawKeyDown", "keyUp" })
        {
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(new
            {
                type,
                modifiers,
                key = k.Key,
                code = k.Code,
                windowsVirtualKeyCode = k.Vk,
            }));
        }
        return null;
    }
}
