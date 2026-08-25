using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NetieSpace.Helpers;
using NetieSpace.Models;
using NetieSpace.Services;

namespace NetieSpace.Controls;

public partial class ChatPanel : UserControl
{
    /// <summary>Per-asset cap so one huge parse cannot crowd out the rest of the tray.</summary>
    private const int AssetTextBudget = 4000;

    /// <summary>Total cap for one composed send.</summary>
    private const int ComposeTextBudget = 14000;

    private const string ModeNormal = "normal";
    private const string ModeCite = "cite";
    private const string ModeFirstPrinciples = "first-principles";
    private const string ModeMindmap = "mindmap";

    private const string CiteMarker = "[Cite mode]";
    private const string FirstPrinciplesMarker = "[First Principles mode]";
    private const string MindmapMarker = "[Mindmap mode]";

    private readonly List<ChatMessage> _history = [];
    private readonly List<ChatAsset> _assets = [];
    private readonly Dictionary<string, FrameworkElement> _assetChips = new(StringComparer.Ordinal);
    private string? _boundFilePath;
    private bool _notesOnly;
    private bool _busy;
    private string _chatMode = ModeNormal;
    private readonly DocumentChatStore _store = new();
    private bool _suppressPersist;
    private readonly DispatcherTimer _persistTimer;
    private Border? _thinkingBubble;

    public event Action<string>? SendRequested;
    public event Action<string>? LookupRequested;
    public event Action<string>? AddToChatRequested;
    public event Action? CloseRequested;
    public event Action? HistoryChanged;

    public ChatPanel()
    {
        InitializeComponent();

        // Chat used to hit disk synchronously on every appended bubble; coalesce instead.
        _persistTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _persistTimer.Tick += (_, _) => Persist();

        ShowWelcome();
    }

    public IReadOnlyList<ChatMessage> GetHistory() => _history;
    public bool NotesOnlyMode => _notesOnly;
    public int StagedAssetCount => _assets.Count;

    /// <summary>Swap history when the preview file changes. Persists the previous file first.</summary>
    public void BindToDocument(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(_boundFilePath) &&
            !string.Equals(_boundFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            Persist();

        _boundFilePath = filePath;
        _suppressPersist = true;
        try
        {
            _history.Clear();
            MessagesPanel.Children.Clear();
            ClearAssets(animate: false);

            if (string.IsNullOrWhiteSpace(filePath))
            {
                _chatMode = ModeNormal;
                ShowWelcome();
                UpdateChrome();
                return;
            }

            var thread = _store.Load(filePath);
            if (thread.Messages.Count == 0)
            {
                ShowWelcome();
            }
            else
            {
                foreach (var m in thread.Messages)
                {
                    _history.Add(m);
                    AppendBubble(m.Content, isUser: m.Role is "user" or "note", isNote: m.Role == "note", animate: false);
                }
            }

            TitleText.Text = string.IsNullOrWhiteSpace(thread.Title) ? "Notes & chat" : thread.Title;
            _chatMode = NormalizeChatMode(thread.ChatMode);
            UpdateChrome();
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    /// <summary>Immediate flush. Call before closing the panel or swapping documents.</summary>
    public void Persist()
    {
        _persistTimer.Stop();
        if (_suppressPersist || string.IsNullOrWhiteSpace(_boundFilePath)) return;
        var thread = new DocumentChatThread
        {
            Title = TitleText.Text,
            ChatMode = _chatMode,
            Messages = _history.Select(m => new ChatMessage
            {
                Role = m.Role,
                Content = m.Content,
                Timestamp = m.Timestamp
            }).ToList()
        };
        _store.Save(_boundFilePath!, thread);
        HistoryChanged?.Invoke();
    }

    private void SchedulePersist()
    {
        if (_suppressPersist || string.IsNullOrWhiteSpace(_boundFilePath)) return;
        _persistTimer.Stop();
        _persistTimer.Start();
    }

    // ── Staging ────────────────────────────────────────────────────────────────
    // Everything below stages an asset chip. Nothing reaches the model until Send.

    public void StageSelection(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return;

        AddAsset(new ChatAsset
        {
            Kind = ChatAssetKind.Selection,
            Title = "Selection",
            Detail = $"{trimmed.Length:N0} chars",
            Text = trimmed
        });
    }

    public void StageDocumentContext(string fileName, string parsedText, PreviewKind kind = PreviewKind.Text)
    {
        var pack = DocumentContextPacker.PackRaw(parsedText, kind);
        AddAsset(new ChatAsset
        {
            Kind = ChatAssetKind.File,
            Title = fileName,
            Detail = pack.Detail,
            Text = pack.Text
        });
    }

    /// <summary>Stage an already-packed digest (Excel sheet / workbook helpers).</summary>
    public void StagePackedContext(string title, string packedText, string detail)
    {
        AddAsset(new ChatAsset
        {
            Kind = ChatAssetKind.File,
            Title = title,
            Detail = detail,
            Text = packedText
        });
    }

    public void StageImage(string imagePath, string? title = null, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !IoFile.Exists(imagePath))
            return;

        AddAsset(new ChatAsset
        {
            Kind = ChatAssetKind.Image,
            Title = string.IsNullOrWhiteSpace(title) ? IoPath.GetFileName(imagePath) : title,
            Detail = string.IsNullOrWhiteSpace(detail) ? DescribeFile(imagePath) : detail,
            ImagePath = imagePath
        });
    }

    /// <summary>Stage one PPTX slide PNG through the same image-attach path. Nothing sends until Send.</summary>
    public void StageSlideImage(string imagePath, int slideIndex)
    {
        var label = slideIndex >= 1 ? $"Slide {slideIndex}" : IoPath.GetFileName(imagePath);
        StageImage(imagePath, label, $"{DescribeFile(imagePath)} · visible slide");
    }

    /// <summary>Marquee clip: page pixels plus whatever the light parse found under the box.</summary>
    public void StageRegion(string imagePath, string? parsedText, string title, string detail)
    {
        var text = parsedText?.Trim() ?? string.Empty;
        var haveImage = !string.IsNullOrWhiteSpace(imagePath) && IoFile.Exists(imagePath);
        if (!haveImage && text.Length == 0)
            return;

        var parts = new List<string>();
        if (text.Length > 0) parts.Add($"{text.Length:N0} chars parsed");
        if (haveImage) parts.Add("clip");
        if (!string.IsNullOrWhiteSpace(detail)) parts.Insert(0, detail);

        AddAsset(new ChatAsset
        {
            Kind = ChatAssetKind.Region,
            Title = string.IsNullOrWhiteSpace(title) ? "Area clip" : title,
            Detail = string.Join(" · ", parts),
            Text = text,
            ImagePath = haveImage ? imagePath : null
        });
    }

    private void AddAsset(ChatAsset asset)
    {
        _assets.Add(asset);

        var chip = BuildAssetChip(asset);
        _assetChips[asset.Id] = chip;
        AssetStrip.Children.Add(chip);

        var wasHidden = AssetTray.Visibility != Visibility.Visible;
        if (wasHidden)
        {
            AssetTray.Visibility = Visibility.Visible;
            AnimationHelper.AnimateOpenFadeOnly(AssetTray, 200);
        }

        AnimationHelper.PopIn(chip, 220);
        UpdateAssetChrome();
    }

    private void RemoveAsset(string id)
    {
        var index = _assets.FindIndex(a => a.Id == id);
        if (index < 0) return;
        _assets.RemoveAt(index);

        if (_assetChips.Remove(id, out var chip))
        {
            _ = AnimationHelper.PopOutAsync(chip, 150).ContinueWith(_ =>
            {
                AssetStrip.Children.Remove(chip);
                if (_assets.Count == 0)
                    AssetTray.Visibility = Visibility.Collapsed;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        UpdateAssetChrome();
    }

    private void ClearAssets(bool animate)
    {
        _assets.Clear();
        _assetChips.Clear();
        AssetStrip.Children.Clear();

        if (animate && AssetTray.Visibility == Visibility.Visible)
            _ = AnimationHelper.AnimateCloseFadeOnlyAsync(AssetTray, 140)
                .ContinueWith(_ =>
                {
                    AssetTray.Visibility = Visibility.Collapsed;
                    AssetTray.Opacity = 1;
                }, TaskScheduler.FromCurrentSynchronizationContext());
        else
            AssetTray.Visibility = Visibility.Collapsed;

        UpdateAssetChrome();
    }

    private void UpdateAssetChrome()
    {
        AssetTrayTitle.Text = _assets.Count == 1
            ? "1 attachment"
            : $"{_assets.Count} attachments";

        var parsedChars = _assets.Sum(a => a.Text.Length);
        var images = _assets.Count(a => a.ImagePath is not null);
        AssetHint.Text = _assets.Count == 0
            ? "Add a question below, then Send — attachments go with it."
            : $"{parsedChars:N0} chars parsed" +
              (images > 0 ? $" · {images} image{(images == 1 ? "" : "s")}" : "") +
              " · add a question below, then Send.";

        UpdateSendAffordance();
    }

    private FrameworkElement BuildAssetChip(ChatAsset asset)
    {
        var chip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(74, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(7, 6, 6, 6),
            Margin = new Thickness(0, 0, 6, 6),
            MaxWidth = 296,
            ToolTip = BuildAssetTooltip(asset)
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(BuildAssetBadge(asset));

        var textStack = new StackPanel
        {
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 190
        };
        textStack.Children.Add(new TextBlock
        {
            Text = asset.Title,
            FontFamily = new FontFamily("Bahnschrift, Segoe UI Variable Text, Segoe UI"),
            Foreground = new SolidColorBrush(Color.FromRgb(242, 244, 248)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        textStack.Children.Add(new TextBlock
        {
            Text = asset.Detail,
            FontFamily = new FontFamily("Bahnschrift, Segoe UI Variable Text, Segoe UI"),
            Foreground = new SolidColorBrush(Color.FromRgb(160, 168, 184)),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        row.Children.Add(textStack);

        var remove = new Button
        {
            Content = "✕",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Style = TryFindResource("GhostButton") as Style,
            Tag = asset.Id,
            ToolTip = "Remove this attachment"
        };
        remove.Click += (_, _) => RemoveAsset(asset.Id);
        row.Children.Add(remove);

        chip.Child = row;
        return chip;
    }

    private static string BuildAssetTooltip(ChatAsset asset)
    {
        if (asset.Text.Length > 0)
            return asset.Text.Length > 600 ? asset.Text[..600] + "…" : asset.Text;
        return asset.ImagePath ?? asset.Title;
    }

    private static UIElement BuildAssetBadge(ChatAsset asset)
    {
        if (asset.ImagePath is { } path && IoFile.Exists(path))
        {
            var bitmap = LoadThumbnail(path);
            if (bitmap is not null)
            {
                return new Border
                {
                    Width = 38,
                    Height = 38,
                    CornerRadius = new CornerRadius(9),
                    Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill },
                    BorderBrush = new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)),
                    BorderThickness = new Thickness(1)
                };
            }
        }

        var glyph = asset.Kind switch
        {
            ChatAssetKind.Selection => "❝",
            ChatAssetKind.Region => "⛶",
            ChatAssetKind.Image => "🖼",
            _ => "📄"
        };

        return new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(56, 94, 196, 184)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 94, 196, 184)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(216, 255, 248)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    /// <summary>Decode small and detached — the old UriSource ctor kept a lock on every clip file.</summary>
    private static BitmapImage? LoadThumbnail(string path, int decodeWidth = 160)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = decodeWidth;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static string DescribeFile(string path)
    {
        try
        {
            var bytes = new System.IO.FileInfo(path).Length;
            var ext = IoPath.GetExtension(path).TrimStart('.').ToUpperInvariant();
            var size = bytes >= 1024 * 1024
                ? $"{bytes / 1024d / 1024d:0.#} MB"
                : $"{Math.Max(1, bytes / 1024d):0.#} KB";
            return string.IsNullOrEmpty(ext) ? size : $"{ext} · {size}";
        }
        catch
        {
            return "image";
        }
    }

    // ── Messages ───────────────────────────────────────────────────────────────

    public void AddUserMessage(string content)
    {
        _history.Add(new ChatMessage { Role = "user", Content = content });
        AppendBubble(content, isUser: true);
        SchedulePersist();
    }

    public void AddAssistantMessage(string content)
    {
        _history.Add(new ChatMessage { Role = "assistant", Content = content });
        AppendBubble(content, isUser: false);
        SchedulePersist();
    }

    public void AddNote(string content)
    {
        _history.Add(new ChatMessage { Role = "note", Content = content });
        AppendBubble(content, isUser: true, isNote: true);
        SchedulePersist();
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        InputBox.IsEnabled = !busy;
        NoteButton.IsEnabled = !busy;
        if (busy) ShowCortexThinking();
        else HideCortexThinking();
        UpdateSendAffordance();
    }

    /// <summary>AirGPT-style in-thread thinking indicator — materialize, no pop icon.</summary>
    private void ShowCortexThinking()
    {
        HideCortexThinking();

        var frost = AppleMotion.PrefersReducedTransparency
            ? Color.FromArgb(220, 28, 34, 44)
            : Color.FromArgb(120, 255, 255, 255);

        var border = new Border
        {
            Background = new SolidColorBrush(frost),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(6, 0, 34, 12),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = ResolveBubbleMaxWidth(),
            Tag = "cortex-thinking",
            Opacity = 0
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Netie Cortex · thinking…",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(168, 176, 192)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var dots = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < 3; i++)
        {
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(Color.FromRgb(242, 244, 248)),
                Margin = new Thickness(0, 0, 5, 0),
                Opacity = 0.35
            };
            if (!AppleMotion.PrefersReducedMotion)
            {
                var anim = new DoubleAnimation(0.35, 1.0, new Duration(TimeSpan.FromMilliseconds(420)))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(i * 160)
                };
                dot.BeginAnimation(OpacityProperty, anim);
            }
            else
            {
                dot.Opacity = 0.7;
            }
            dots.Children.Add(dot);
        }
        stack.Children.Add(dots);
        border.Child = stack;

        MessagesPanel.Children.Add(border);
        _thinkingBubble = border;
        AnimationHelper.SlideFadeIn(border, 12, AppleMotion.SheetResponseMs);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => MessagesScroll.ScrollToEnd()));
    }

    private void HideCortexThinking()
    {
        if (_thinkingBubble is null) return;
        var bubble = _thinkingBubble;
        _thinkingBubble = null;
        // Interruptible dismiss from current opacity — no hard cut
        _ = AnimationHelper.AnimateCloseFadeOnlyAsync(bubble, AppleMotion.PressMs + 40)
            .ContinueWith(_ => Dispatcher.Invoke(() => MessagesPanel.Children.Remove(bubble)));
    }

    private double ResolveBubbleMaxWidth()
    {
        // Phone-scale companion vs desktop: keep bubbles readable, never edge-to-edge
        var w = ActualWidth > 40 ? ActualWidth : 360;
        return Math.Clamp(w * 0.82, 220, 320);
    }

    public void FocusInput()
    {
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text.Length;
    }

    private void ShowWelcome()
    {
        AppendBubble(
            "Attached to this file. Notes work offline (Pin note). Send uses AI when a key is set.\n\n" +
            "PDF: drag Select area to clip a table or figure — it parses first, then stages as an attachment. " +
            "Nothing is sent until you press Send.",
            isUser: false, animate: false);
    }

    private void UpdateChrome()
    {
        SubtitleText.Text = _notesOnly
            ? "Notes mode — ideas stay on this file, no AI calls"
            : _chatMode switch
            {
                ModeCite => "Cite · quotes the open document with short citations",
                ModeFirstPrinciples => "First Principles · axioms, then rebuild — no fluff",
                ModeMindmap => "Mindmap · indented outline / mermaid tree of this file",
                _ => "Saved with this file · reopen anytime"
            };
        NotesOnlyButton.Content = _notesOnly ? "Notes ON" : "Notes";
        ApplyModeChipStyles();
        UpdateSendAffordance();
    }

    private void ApplyModeChipStyles()
    {
        if (ModeCiteButton is null) return;
        var ghost = TryFindResource("GhostButton") as Style;
        var on = TryFindResource("TealButton") as Style ?? ghost;
        ModeCiteButton.Style = _chatMode == ModeCite ? on : ghost;
        ModeFirstPrinciplesButton.Style = _chatMode == ModeFirstPrinciples ? on : ghost;
        ModeMindmapButton.Style = _chatMode == ModeMindmap ? on : ghost;
    }

    private void ModeChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string mode)
            return;

        _chatMode = string.Equals(_chatMode, mode, StringComparison.OrdinalIgnoreCase)
            ? ModeNormal
            : NormalizeChatMode(mode);
        UpdateChrome();
        SchedulePersist();
        FocusInput();
    }

    private static string NormalizeChatMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        ModeCite => ModeCite,
        ModeFirstPrinciples => ModeFirstPrinciples,
        ModeMindmap => ModeMindmap,
        _ => ModeNormal
    };

    private void UpdateSendAffordance()
    {
        if (SendButton is null) return;

        SendButton.IsEnabled = !_busy;
        if (_busy)
        {
            SendButton.Content = "…";
            return;
        }

        var label = _notesOnly ? "Note" : "Send";
        SendButton.Content = _assets.Count > 0 ? $"{label} · {_assets.Count}" : label;
        SendButton.ToolTip = _notesOnly
            ? "Save as note (no AI)"
            : _assets.Count > 0
                ? $"Send your question with {_assets.Count} attachment{(_assets.Count == 1 ? "" : "s")}"
                : _chatMode switch
                {
                    ModeCite => "Send in Cite mode (quotes + citations)",
                    ModeFirstPrinciples => "Send in First Principles mode",
                    ModeMindmap => "Send in Mindmap mode (outline / mermaid tree)",
                    _ => "Send to AI"
                };
    }

    private void AppendBubble(string content, bool isUser, bool isNote = false, bool animate = true)
    {
        var bg = isNote
            ? Color.FromArgb(160, 94, 196, 184)
            : isUser
                ? Color.FromArgb(140, 80, 110, 220)
                : Color.FromArgb(90, 255, 255, 255);

        var fg = new SolidColorBrush(isNote
            ? Color.FromRgb(14, 18, 24)
            : Color.FromRgb(242, 244, 248));

        var border = new Border
        {
            Background = new SolidColorBrush(bg),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14, 11, 14, 11),
            // Breathing room on BOTH sides — the aligned edge no longer kisses the panel wall.
            Margin = isUser || isNote
                ? new Thickness(34, 0, 6, 12)
                : new Thickness(6, 0, 34, 12),
            HorizontalAlignment = isUser || isNote ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = ResolveBubbleMaxWidth()
        };

        var stack = new StackPanel();
        if (isNote)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "NOTE",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(14, 18, 24)),
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        // Assistant: Markdig + mermaid fences. User/note: plain text.
        if (!isUser && !isNote)
            stack.Children.Add(ChatMarkdownRenderer.BuildBody(content, fg));
        else
        {
            stack.Children.Add(new TextBlock
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, SF Pro Text, Helvetica Neue"),
                Foreground = fg,
                FontSize = 13.5,
                LineHeight = 19
            });
        }

        // Reserved footer — opacity only on hover (no layout shift).
        if (!isUser && !isNote)
        {
            var copyBtn = new Button
            {
                Content = "⧉",
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                FontSize = 11,
                Opacity = 0,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "Copy",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 208, 220)),
                Tag = content
            };
            copyBtn.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(ChatMarkdownRenderer.CollectCopyText(content));
                    copyBtn.ToolTip = "Copied";
                }
                catch { /* ignore clipboard races */ }
            };

            // Fixed-height footer always present so hover never reflows the bubble.
            var footer = new Grid { Height = 22, Margin = new Thickness(0, 4, 0, 0) };
            footer.Children.Add(copyBtn);
            stack.Children.Add(footer);

            border.MouseEnter += (_, _) =>
            {
                copyBtn.Opacity = 0.85;
                copyBtn.IsHitTestVisible = true;
            };
            border.MouseLeave += (_, _) =>
            {
                copyBtn.Opacity = 0;
                copyBtn.IsHitTestVisible = false;
                copyBtn.ToolTip = "Copy";
            };
        }

        border.Child = stack;
        MessagesPanel.Children.Add(border);

        if (animate && !AppleMotion.PrefersReducedMotion)
        {
            AnimationHelper.SlideFadeIn(border, isUser || isNote ? 18 : -18, AppleMotion.SheetResponseMs);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => MessagesScroll.ScrollToEnd()));
        }
        else if (animate)
        {
            AnimationHelper.AnimateOpenFadeOnly(border, 160);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => MessagesScroll.ScrollToEnd()));
        }
        else
        {
            MessagesScroll.ScrollToEnd();
        }
    }

    /// <summary>Attach thumbnails under a sent bubble so the transcript shows what went with it.</summary>
    private void AppendAttachmentStrip(IReadOnlyList<ChatAsset> assets)
    {
        var images = assets.Where(a => a.ImagePath is not null).ToList();
        if (images.Count == 0) return;

        var strip = new WrapPanel
        {
            Margin = new Thickness(34, 0, 6, 12),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        foreach (var asset in images.Take(6))
        {
            var bitmap = LoadThumbnail(asset.ImagePath!, 320);
            if (bitmap is null) continue;
            strip.Children.Add(new Border
            {
                Width = 76,
                Height = 76,
                Margin = new Thickness(0, 0, 6, 0),
                CornerRadius = new CornerRadius(12),
                Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill },
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                ToolTip = asset.Title
            });
        }

        if (strip.Children.Count == 0) return;
        MessagesPanel.Children.Add(strip);
        AnimationHelper.AnimateOpenFadeOnly(strip, 200);
    }

    // ── Send ───────────────────────────────────────────────────────────────────

    private void SendButton_Click(object sender, RoutedEventArgs e) => Submit(forceNote: _notesOnly);
    private void PinNote_Click(object sender, RoutedEventArgs e) => Submit(forceNote: true);

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        // Kept for XAML binding; send/newline handled in PreviewKeyDown
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            var caret = InputBox.CaretIndex;
            InputBox.Text = InputBox.Text.Insert(caret, Environment.NewLine);
            InputBox.CaretIndex = caret + Environment.NewLine.Length;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            Submit(forceNote: _notesOnly);
        }
    }

    private void Submit(bool forceNote)
    {
        var question = InputBox.Text.Trim();
        if (question.Length == 0 && _assets.Count == 0)
        {
            if (forceNote || _chatMode == ModeNormal)
                return;
            question = DefaultQuestionForMode(_chatMode);
        }

        var staged = _assets.ToList();
        var transcript = BuildTranscriptEntry(question, staged);
        var payload = BuildModelPayload(question, staged);

        InputBox.Clear();
        ClearAssets(animate: true);

        if (forceNote)
        {
            AddNote(transcript);
            AppendAttachmentStrip(staged);
            return;
        }

        _history.Add(new ChatMessage { Role = "user", Content = payload });
        AppendBubble(transcript, isUser: true);
        AppendAttachmentStrip(staged);
        SchedulePersist();

        SendRequested?.Invoke(payload);
    }

    /// <summary>What the user sees in the thread — compact, no dumped payload.</summary>
    private static string BuildTranscriptEntry(string question, IReadOnlyList<ChatAsset> assets)
    {
        if (assets.Count == 0)
            return question;

        var sb = new StringBuilder();
        if (question.Length > 0)
            sb.Append(question).Append("\n\n");

        foreach (var asset in assets)
        {
            sb.Append("📎 ").Append(asset.Title);
            if (!string.IsNullOrWhiteSpace(asset.Detail))
                sb.Append(" · ").Append(asset.Detail);
            sb.Append('\n');
        }

        if (question.Length == 0)
            sb.Append("\n(no question — summarise these)");

        return sb.ToString().TrimEnd();
    }

    /// <summary>What actually goes to the model — parsed text first, image paths noted.</summary>
    private string BuildModelPayload(string question, IReadOnlyList<ChatAsset> assets)
    {
        var body = assets.Count == 0 ? question : BuildAttachedPayload(question, assets);
        var prefix = UserPrefixForMode(_chatMode);
        return prefix is null ? body : prefix + "\n\n" + body;
    }

    private static string? UserPrefixForMode(string mode) => mode switch
    {
        ModeCite => CiteMarker + " Quote the open document. Include short citations (page / heading / excerpt) for every claim.",
        ModeFirstPrinciples => FirstPrinciplesMarker + " Strip to axioms, then rebuild. No fluff.",
        ModeMindmap => MindmapMarker + " Return a compact indented outline or mermaid-friendly tree of the open document. No prose preamble.",
        _ => null
    };

    private static string DefaultQuestionForMode(string mode) => mode switch
    {
        ModeCite => "What does this document claim? Quote and cite.",
        ModeFirstPrinciples => "Explain this document from first principles.",
        ModeMindmap => "Map the open document as a compact outline.",
        _ => ""
    };

    private static string BuildAttachedPayload(string question, IReadOnlyList<ChatAsset> assets)
    {
        var sb = new StringBuilder();
        sb.Append("Attached context (").Append(assets.Count)
          .Append(assets.Count == 1 ? " item" : " items").Append("):\n\n");

        var spent = 0;
        for (var i = 0; i < assets.Count; i++)
        {
            var asset = assets[i];
            sb.Append(i + 1).Append(". ").Append(asset.Title);
            if (!string.IsNullOrWhiteSpace(asset.Detail))
                sb.Append(" — ").Append(asset.Detail);
            sb.Append('\n');

            if (asset.ImagePath is { } img)
                sb.Append("[image] ").Append(img).Append('\n');

            if (asset.Text.Length > 0 && spent < ComposeTextBudget)
            {
                var room = Math.Min(AssetTextBudget, ComposeTextBudget - spent);
                var slice = asset.Text.Length > room ? asset.Text[..room] + "\n…(truncated)" : asset.Text;
                spent += slice.Length;
                sb.Append("\"\"\"\n").Append(slice).Append("\n\"\"\"\n");
            }

            sb.Append('\n');
        }

        sb.Append(question.Length > 0
            ? "Question:\n" + question
            : "Summarise the attached context and call out anything that needs a decision.");

        return sb.ToString();
    }

    // ── Handlers ───────────────────────────────────────────────────────────────

    private void AssetClear_Click(object sender, RoutedEventArgs e) => ClearAssets(animate: true);

    private void AssetLookup_Click(object sender, RoutedEventArgs e)
    {
        var text = _assets.FirstOrDefault(a => a.Text.Length > 0)?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            AssetHint.Text = "Look Up needs text — this attachment is pixels only. Press Send instead.";
            return;
        }

        LookupRequested?.Invoke(text.Length > 600 ? text[..600] : text);
    }

    /// <summary>Lets the host push a selection through the same path the viewer uses.</summary>
    public void RequestAddToChat(string text) => AddToChatRequested?.Invoke(text);

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Persist();
        CloseRequested?.Invoke();
    }

    private void NewChat_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_boundFilePath)) return;
        var next = _store.StartNew(_boundFilePath, new DocumentChatThread
        {
            Title = TitleText.Text,
            ChatMode = _chatMode,
            Messages = _history.ToList()
        });
        _history.Clear();
        MessagesPanel.Children.Clear();
        _thinkingBubble = null;
        ClearAssets(animate: false);
        TitleText.Text = next.Title;
        ShowWelcome();
        Persist();
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_boundFilePath))
        {
            MessageBox.Show("Open a file first — history is saved per document.",
                "History", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var archives = _store.ListArchives(_boundFilePath!);
        if (archives.Count == 0)
        {
            MessageBox.Show(
                "No previous threads yet. Press New to archive the current chat, then History to restore it.",
                "History", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var menu = new ContextMenu();
        foreach (var info in archives.Take(24))
        {
            var label = $"{info.Title} · {info.MessageCount} msg · {info.UpdatedUtc.ToLocalTime():MMM d HH:mm}";
            var item = new MenuItem { Header = label, Tag = info.ArchivePath };
            item.Click += (_, _) => RestoreArchive(info.ArchivePath);
            menu.Items.Add(item);
        }

        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
    }

    private void RestoreArchive(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(_boundFilePath)) return;
        var archived = _store.LoadArchiveFile(archivePath);
        if (archived is null)
        {
            MessageBox.Show("Could not open that archive.", "History",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Archive current active thread first so nothing is lost
        if (_history.Count > 0)
        {
            _store.StartNew(_boundFilePath!, new DocumentChatThread
            {
                Title = TitleText.Text,
                ChatMode = _chatMode,
                Messages = _history.ToList()
            });
        }

        _suppressPersist = true;
        try
        {
            _history.Clear();
            MessagesPanel.Children.Clear();
            _thinkingBubble = null;
            ClearAssets(animate: false);

            foreach (var m in archived.Messages)
            {
                _history.Add(m);
                AppendBubble(m.Content, isUser: m.Role is "user" or "note", isNote: m.Role == "note", animate: false);
            }

            TitleText.Text = string.IsNullOrWhiteSpace(archived.Title)
                ? "Restored chat"
                : archived.Title;
            _chatMode = NormalizeChatMode(archived.ChatMode);
            UpdateChrome();
        }
        finally
        {
            _suppressPersist = false;
        }

        Persist();
    }

    private void ClearChat_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_boundFilePath)) return;
        var ok = MessageBox.Show(
            "Clear all chat and notes saved for this file?",
            "Clear chat",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
        if (!ok) return;

        _persistTimer.Stop();
        _store.Clear(_boundFilePath!);
        _history.Clear();
        MessagesPanel.Children.Clear();
        _thinkingBubble = null;
        ClearAssets(animate: false);
        TitleText.Text = "Notes & chat";
        ShowWelcome();
    }

    private void NotesOnly_Click(object sender, RoutedEventArgs e)
    {
        _notesOnly = !_notesOnly;
        UpdateChrome();
        FocusInput();
    }
}
