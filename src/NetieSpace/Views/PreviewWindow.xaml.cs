using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using NetieSpace.Helpers;
using NetieSpace.Models;
using NetieSpace.Services;
using NetieSpace.Services.Timeline;

namespace NetieSpace.Views;

public partial class PreviewWindow : Window
{
    private PreviewHandlerOverlay? _previewHandler;
    private readonly int _animationMs;
    private readonly ContentParserService _parser;
    private readonly ConversionService _conversion;
    private readonly AiService _ai;
    private readonly ExplorerFileService _explorer;
    private readonly IntPtr _ownerHwnd;
    private readonly bool _fromDesktop;
    private readonly FfmpegService _ffmpeg = new();
    private readonly PdfEditService _pdfEdit = new();
    private readonly BackgroundRemovalService _bgRemoval = new();
    private readonly SignatureLibrary _sigLibrary = new();
    private readonly Action? _openApiSetup;
    private readonly DispatcherTimer _videoTimer;
    private PreviewContent? _content;
    private string? _currentFilePath;
    private bool _chatOpen;
    /// <summary>Chat column mid-slide — skip the per-frame corner-mask rebuild until it settles.</summary>
    private bool _chatAnimating;
    private bool _busy;
    private string? _nobgTempPath;
    private Point _imageDragStart;
    private bool _imageDragArmed;
    private bool _sliderDragging;
    private bool _wasPlayingBeforeScrub;
    private double _volumeBeforeScrub = 1.0;
    private DateTime _lastScrubSeekUtc = DateTime.MinValue;
    private double? _pendingSeekSourceSeconds;
    private DateTime _suppressPositionSyncUntilUtc = DateTime.MinValue;
    private const int SeekSettleMs = 450;
    private const double SeekToleranceSec = 0.4;
    private bool _suppressSliderValueChanged;
    private TimeSpan? _markIn;
    private TimeSpan? _markOut;
    private readonly VideoEditSession _videoEdit = new();
    private readonly TimelineProject _timeline = new();
    private bool _editMode;
    private string? _pendingStampName;
    private string? _pendingStampFont;
    private string? _pendingStampImage;
    private readonly List<PendingPdfStamp> _pendingStamps = [];
    private readonly List<PendingPdfFillText> _pendingFillTexts = [];
    private readonly List<PendingPdfHighlight> _pendingHighlights = [];
    private TaskCompletionSource<bool>? _viewerFlushTcs;
    private TaskCompletionSource<PagePngCapture>? _pagePngTcs;
    private string _pdfToolMode = ""; // organize | sign | fill | comment
    private string _preferredSignFont = "Segoe Script";
    private bool _pdfSignFontComboReady;
    private bool _pdfDirty;
    /// <summary>Clean PDF before any stamps — restamp from this so move+save never doubles.</summary>
    private string? _pdfStampBasePath;
    private CancellationTokenSource? _stampAutosaveCts;
    private bool _allowClose;
    private bool _bladeMode;
    private TimelineDragMode _timelineDrag = TimelineDragMode.None;
    private CancellationTokenSource? _cts;
    private double _zoom = 1.0;
    private double _fitZoom = 1.0;
    private bool _zoomAtFit = true;
    private bool _summaryBarEnabled = true;
    private bool _textEditMode;
    private CancellationTokenSource? _textAutosaveCts;
    private readonly Stack<string> _previewStack = new();
    private bool _webViewHandlersWired;
    private bool _timelineLayoutPending;
    private CancellationTokenSource? _exportCts;
    private bool _exportDetached;
    private bool _captionDetached;
    /// <summary>True when focus is inside an editable field in WebView2 (search, page #, contenteditable).</summary>
    private bool _webViewTyping;
    /// <summary>Serializes arrow/Home/End travel so LoadFileAsync never stacks.</summary>
    private int _travelGeneration;
    private bool _travelInFlight;
    private DispatcherTimer? _seekResumeTimer;
    /// <summary>Summary strip hidden by the user (distinct from _summaryBarEnabled, which also stops AI calls).</summary>
    private bool _summaryCollapsed;
    /// <summary>Summary lives in the left rail instead of the top strip.</summary>
    private bool _summaryDocked;
    /// <summary>Height the strip had before collapsing, so expand lands where it started.</summary>
    private double _summaryExpandedHeight;
    private bool _convertTargetsAvailable = true;
    /// <summary>Marquee ("Select area") armed in the PDF viewer.</summary>
    private bool _regionMode;
    private string? _activeSheetName;
    private const double SummaryRailWidth = 268;
    private const double ChatPanelWidth = 380;

    private enum TimelineDragMode { None, Seek, MarkIn, MarkOut }

    private sealed class PendingPdfStamp
    {
        public string Id { get; set; } = "";
        public int Page { get; set; }
        public double XPt { get; set; }
        public double YFromBottom { get; set; }
        public double? StampW { get; set; }
        public double? StampH { get; set; }
        public string? ImagePath { get; set; }
        public string? TypedName { get; set; }
        public string? Font { get; set; }
    }

    /// <summary>Fill &amp; Sign typed text box from the viewer (PDF points, origin bottom-left).</summary>
    private sealed class PendingPdfFillText
    {
        public string Id { get; set; } = "";
        public int Page { get; set; }
        public double XPt { get; set; }
        public double YFromBottom { get; set; }
        public double WPt { get; set; }
        public double HPt { get; set; }
        public double FontPt { get; set; }
        public string Text { get; set; } = "";
        public bool CoverBackground { get; set; }
    }

    /// <summary>Comment highlight or ink mark from the viewer (PDF points, origin bottom-left).</summary>
    private sealed class PendingPdfHighlight
    {
        public string Id { get; set; } = "";
        public int Page { get; set; }
        public double XPt { get; set; }
        public double YFromBottom { get; set; }
        public double WPt { get; set; }
        public double HPt { get; set; }
        public string Kind { get; set; } = "highlight";
    }

    private sealed class PagePngCapture
    {
        public int Page { get; set; }
        public string? DataUrl { get; set; }
        public string? Error { get; set; }
        public double WidthPt { get; set; }
        public double HeightPt { get; set; }
        public int WidthPx { get; set; }
        public int HeightPx { get; set; }
    }

    private ExportPreset _exportPreset = ExportPreset.MatchSource;

    public PreviewWindow(
        int animationMs,
        ContentParserService parser,
        ConversionService conversion,
        AiService ai,
        ExplorerFileService explorer,
        IntPtr ownerHwnd,
        Action? openApiSetup = null)
    {
        InitializeComponent();
        AppIconHelper.ApplyWindowIcon(this);
        _animationMs = animationMs;
        _parser = parser;
        _conversion = conversion;
        _ai = ai;
        _explorer = explorer;
        _ownerHwnd = ownerHwnd;
        _openApiSetup = openApiSetup;

        Win32Api.TryFindExplorerShellWindow(ownerHwnd, out _, out var ownerClass);
        _fromDesktop = Win32Api.IsDesktopClass(ownerClass);

        ChatPanel.SendRequested += OnChatSendRequested;
        ChatPanel.LookupRequested += OnLookupRequested;
        ChatPanel.AddToChatRequested += OnAddToChatRequested;
        ChatPanel.CloseRequested += CloseChatPanel;
        PreviewVideo.MediaFailed += PreviewVideo_MediaFailed;

        _videoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _videoTimer.Tick += (_, _) => UpdateVideoUi();

        ProviderButton.Content = $"AI: {_ai.ActiveProvider}";
        _summaryBarEnabled = UserPrefs.SummaryBarEnabled;
        _summaryCollapsed = UserPrefs.SummaryCollapsed;
        _summaryDocked = UserPrefs.SummaryDocked;
        ApplySummaryBarVisibility();
        if (SummaryToggleButton is not null)
            SummaryToggleButton.Content = _summaryBarEnabled ? "Summary" : "Summary off";

        // Opaque #10141C — FromArgb(0x10,0x14,0x1C) was alpha=16 (nearly transparent) and
        // crashed/composited badly with AllowsTransparency + WebView2 HWND.
        try
        {
            PreviewWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 0x10, 0x14, 0x1C);
        }
        catch { /* ignore */ }

        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
        GlassHelper.ApplyGlass(this, 0.94, useAcrylic: true, cornerRadius: 24);
    }

    public void PositionOnOwnerMonitor(IntPtr ownerHwnd)
    {
        var hwnd = ownerHwnd != IntPtr.Zero ? ownerHwnd : _ownerHwnd;
        // Win32 monitor rects are physical pixels; WPF Left/Top are DIPs — convert or the
        // window lands off-center on scaled displays.
        var dpi = VisualTreeHelper.GetDpi(this);
        var sx = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
        var sy = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;

        if (!Win32Api.TryGetMonitorWorkArea(hwnd, out var work))
        {
            var wa = SystemParameters.WorkArea; // already DIPs
            Width = Math.Min(980, wa.Width * 0.70);
            Height = Math.Min(680, wa.Height * 0.72);
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + (wa.Height - Height) / 2;
            return;
        }

        var workLeft = work.Left / sx;
        var workTop = work.Top / sy;
        var workWidth = (work.Right - work.Left) / sx;
        var workHeight = (work.Bottom - work.Top) / sy;

        Width = Math.Clamp(workWidth * 0.70, MinWidth, Math.Max(MinWidth, workWidth - 64));
        Height = Math.Clamp(workHeight * 0.72, MinHeight, Math.Max(MinHeight, workHeight - 64));
        Left = workLeft + (workWidth - Width) / 2;
        Top = workTop + (workHeight - Height) / 2;
    }

    // Acrylic is enabled once via ApplyGlass → SourceInitialized
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource src)
            src.AddHook(ImageHorizontalWheelHook);
    }

    private IntPtr ImageHorizontalWheelHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmMouseHWheel = 0x020E;
        if (msg != wmMouseHWheel || ImageScroll.Visibility != Visibility.Visible)
            return IntPtr.Zero;

        var delta = (short)((wParam.ToInt64() >> 16) & 0xffff);
        ImageScroll.ScrollToHorizontalOffset(ImageScroll.HorizontalOffset - delta);
        handled = true;
        return IntPtr.Zero;
    }

    public async Task LoadFileAsync(string filePath)
    {
        StopVideo();
        HidePreviewHandler();
        // Persist chat for the previous file before swapping
        try { ChatPanel.Persist(); } catch { /* ignore */ }
        if (!string.Equals(_currentFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            ClearStampSession();
        _currentFilePath = filePath;
        _activeSheetName = null;
        // Binding does a synchronous JSON read + full bubble rebuild. Only pay it when the panel is
        // actually visible — a closed panel rebinds lazily in OpenChatPanel, so arrow-key browsing
        // between files never touches the chat store.
        if (_chatOpen)
            ChatPanel.BindToDocument(filePath);
        _markIn = null;
        _markOut = null;
        UpdateMarkLabel();
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        FileNameText.Text = IoPath.GetFileName(filePath);
        _textAutosaveCts?.Cancel();
        _textEditMode = false;
        // Fresh viewer load resets region mode on the JS side — mirror it on the button.
        if (_regionMode) { _regionMode = false; RegionSelectButton.Content = "Select area"; }
        _captionCues.Clear();
        StatusText.Text = "Loading preview...";
        SummaryText.Text = !_summaryBarEnabled
            ? "Summary hidden — turn Summary on to generate."
            : _ai.IsConfigured
                ? "Generating summary..."
                : "Preview ready — add an API key for AI summary (tray → API Keys…).";
        ConvertStatusText.Text = "";
        OpenExternallyButton.Visibility = Visibility.Collapsed;
        SetLoading(true, "Loading preview...");

        try
        {
            // Instant first paint for PPTX: Shell thumbnail while COM / LO / HTML warms
            var ext = IoPath.GetExtension(filePath).ToLowerInvariant();
            if (ext is ".pptx" or ".ppt")
                TryShowInstantThumbnail(filePath);

            _content = await _parser.ParseAsync(filePath, 1024, ct);
            // A slower earlier load can complete its parse after a newer one cancelled it — bail
            // before rendering so a stale file never paints over the file the user actually wants.
            if (ct.IsCancellationRequested)
                return;

            await RenderContentAsync(_content);
            UpdateActionButtons(_content.Kind);
            ApplySummaryBarVisibility();

            // Sidecar captions for video
            if (_content.Kind == PreviewKind.Video)
            {
                var srt = IoPath.Combine(
                    IoPath.GetDirectoryName(filePath) ?? "",
                    IoPath.GetFileNameWithoutExtension(filePath) + ".srt");
                if (IoFile.Exists(srt))
                {
                    _timeline.SubtitleBurnPath = srt;
                    LoadCaptionCuesFromSrt(srt);
                }
            }

            var aiLabel = _ai.IsConfigured ? _ai.ActiveProvider : "no AI key";
            StatusText.Text = $"{_content.Kind} · {FormatSize(_content.FileSizeBytes)} · {aiLabel}";
            ProviderButton.Content = $"AI: {aiLabel}";
            if (_summaryBarEnabled)
            {
                // First-run consent is modal — showing it inside the load path pinned the loading
                // overlay behind the dialog. Defer it so the preview paints first, then prompt.
                if (!UserPrefs.EmbeddingConsentPrompted)
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Background,
                        new Action(() => { try { EmbeddingConsentWindow.EnsurePrompted(); } catch { } }));
                _ = LoadSummaryAsync(_content, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            SummaryText.Text = "Could not load file.";
        }
        finally
        {
            // A superseded load must not tear down the newer load's overlay.
            if (!ct.IsCancellationRequested)
                SetLoading(false);
        }
    }

    private void TryShowInstantThumbnail(string filePath)
    {
        try
        {
            var thumb = ShellThumbnail.TryGetBitmapSource(filePath, 640);
            if (thumb is null)
                return;

            VideoPanel.Visibility = Visibility.Collapsed;
            PreviewWebView.Visibility = Visibility.Collapsed;
            ImageScroll.Visibility = Visibility.Visible;
            PreviewImage.Source = thumb;
            StatusText.Text = "Loading slides…";
            ConvertStatusText.Text = "Shell thumbnail";
            SetLoading(false);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Instant PPTX thumbnail failed: {ex.Message}");
        }
    }

    private void UpdateActionButtons(PreviewKind kind)
    {
        var isVideo = kind == PreviewKind.Video;
        var isPdf = kind == PreviewKind.Pdf;
        var isImage = kind == PreviewKind.Image;
        var isTextCode = kind is PreviewKind.Text or PreviewKind.Markdown;
        var isHtmlWord = IsHtmlWordEditable();
        var editing = _editMode && (isVideo || isPdf);

        EditModeButton.Visibility = (isVideo || isPdf) ? Visibility.Visible : Visibility.Collapsed;
        EditModeButton.Content = _editMode ? "Done" : "Edit";
        EditModeButton.ToolTip = _editMode
            ? "Return to light Space preview"
            : "Morph into full Netie editor (all tools visible)";

        if (TextEditButton is not null)
        {
            TextEditButton.Visibility = isTextCode || isHtmlWord ? Visibility.Visible : Visibility.Collapsed;
            TextEditButton.Content = _textEditMode ? "Done" : "Edit";
            TextEditButton.ToolTip = isHtmlWord
                ? "Edit Word/HTML in the preview — autosaves as you type"
                : "Edit text — autosaves as you type";
        }

        if (BrandModeText is not null)
        {
            BrandModeText.Text = editing
                ? (isPdf ? "PDF Editor" : "Video Editor")
                : _textEditMode && isHtmlWord
                    ? HtmlWordBrandLabel()
                    : _textEditMode && isTextCode
                        ? "Text Editor"
                        : "NETIE SPACE";
        }

        // ApplySummaryLayout already yields the row to the editor ribbons while editing.
        ApplySummaryBarVisibility();
        if (PdfEditorRibbon is not null)
            PdfEditorRibbon.Visibility = isPdf && _editMode ? Visibility.Visible : Visibility.Collapsed;
        if (VideoEditorRibbon is not null)
            VideoEditorRibbon.Visibility = isVideo && _editMode ? Visibility.Visible : Visibility.Collapsed;

        // Tools live on the ribbon; keep named buttons visible while editing
        if (PdfMoreButton is not null)
            PdfMoreButton.Visibility = isPdf && _editMode ? Visibility.Visible : Visibility.Collapsed;
        if (isPdf && _editMode)
            EnsurePdfSignFontsLoaded();
        if (!isPdf || !_editMode)
            SetPdfToolMode("");

        if (AddVideoButton is not null)
            AddVideoButton.Visibility = isVideo && _editMode ? Visibility.Visible : Visibility.Collapsed;
        if (OrderClipsButton is not null)
            OrderClipsButton.Visibility = isVideo && _editMode ? Visibility.Visible : Visibility.Collapsed;
        if (VideoMoreButton is not null)
            VideoMoreButton.Visibility = isVideo && _editMode ? Visibility.Visible : Visibility.Collapsed;
        CutMiddleButton.Visibility = isVideo && _editMode && _videoEdit.HasSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (ExportEditButton is not null)
            ExportEditButton.Visibility = isVideo && _editMode ? Visibility.Visible : Visibility.Collapsed;
        RefreshExportPresetButtons();

        RemoveBgButton.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
        if (OcrImageButton is not null)
            OcrImageButton.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
        if (CopyImageButton is not null)
            CopyImageButton.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
        if (CompressImageButton is not null)
            CompressImageButton.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
        if (SendToComfyButton is not null)
            SendToComfyButton.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
        if (WindowsAiButton is not null)
            WindowsAiButton.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;

        if (VideoEditorChrome is not null)
            VideoEditorChrome.Visibility = isVideo && _editMode ? Visibility.Visible : Visibility.Collapsed;
        if (VideoLightHint is not null)
            VideoLightHint.Visibility = isVideo && !_editMode ? Visibility.Visible : Visibility.Collapsed;

        // PDF zoom/Fit live in the embedded viewer toolbar — hide duplicate WPF strip
        var zoomable = kind is PreviewKind.Image or PreviewKind.Office or PreviewKind.Html or PreviewKind.Markdown or PreviewKind.Text;
        if (ZoomStrip is not null)
            ZoomStrip.Visibility = zoomable ? Visibility.Visible : Visibility.Collapsed;
        ZoomInButton.Visibility = zoomable ? Visibility.Visible : Visibility.Collapsed;
        ZoomOutButton.Visibility = zoomable ? Visibility.Visible : Visibility.Collapsed;
        ZoomFitButton.Visibility = zoomable ? Visibility.Visible : Visibility.Collapsed;
        ZoomLabel.Visibility = zoomable ? Visibility.Visible : Visibility.Collapsed;

        // Let the WebView own trackpad/Ctrl+wheel zoom for reflowable content; keep it OFF for PDF
        // (its own canvas-scale path already handles zoom — WebView factor on top double-zooms/blurs).
        if (PreviewWebView.CoreWebView2 is { } zoomCore)
        {
            try { zoomCore.Settings.IsZoomControlEnabled = kind != PreviewKind.Pdf; } catch { /* older runtime */ }
        }

        // Share is always useful (copy / mail / reveal); only the format rows depend on the kind.
        var convertOk = kind is not PreviewKind.Video and not PreviewKind.ShellInfo and not PreviewKind.Binary;
        _convertTargetsAvailable = convertOk;

        // Region marquee is a PDF-viewer feature; images fall back to a whole-frame clip.
        var regionOk = kind is PreviewKind.Pdf or PreviewKind.Image;
        RegionSelectButton.Visibility = regionOk ? Visibility.Visible : Visibility.Collapsed;
        if (SendSlideChatButton is not null)
            SendSlideChatButton.Visibility = PptxSlideImageService.IsPptxPath(_currentFilePath)
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (!regionOk && _regionMode)
        {
            _regionMode = false;
            RegionSelectButton.Content = "Select area";
        }
    }

    private void EditModeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppLogger.Info($"EditMode toggle → {(!_editMode ? "enter" : "exit")} kind={_content?.Kind}");
            _editMode = !_editMode;
            if (_content is not null)
                UpdateActionButtons(_content.Kind);

            if (_editMode)
            {
                if (ActualHeight < 720)
                    Height = Math.Max(ActualHeight, Math.Min(900, SystemParameters.WorkArea.Height * 0.85));

                ConvertStatusText.Text = _content?.Kind == PreviewKind.Video
                    ? "Video editor — all tools ready from the first frame."
                    : "PDF editor — Organize · Sign · Fill · Comment";

                FrameworkElement? ribbon = _content?.Kind switch
                {
                    PreviewKind.Pdf => PdfEditorRibbon,
                    PreviewKind.Video => VideoEditorRibbon,
                    _ => null
                };
                // Ribbon lives in Row 1, above the WebView2 HWND in Row 2 — a small Y slide is safe here
                // and turns the row-height jump into an arrival instead of a pop.
                if (ribbon is not null)
                    AnimationHelper.SlideDownFadeIn(ribbon, -12, _animationMs);

                if (BrandModeText is not null)
                    AnimationHelper.AnimateOpen(BrandModeText, _animationMs - 40);

                // Defer canvas rebuild off this click/layout pass (prevents InvalidOperationException crash)
                ScheduleTimelineLayout();
            }
            else
            {
                ConvertStatusText.Text = "Light preview";
                // Morph the ribbon back out and the summary back in, wherever the user parked it.
                ApplySummaryLayout(animate: true);
                if (BrandModeText is not null)
                    AnimationHelper.AnimateOpen(BrandModeText, _animationMs - 40);
            }

            EditModeButton.InvalidateVisual();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "EditModeButton_Click");
            ConvertStatusText.Text = $"Editor error: {ex.Message}";
            try
            {
                _editMode = false;
                if (_content is not null)
                    UpdateActionButtons(_content.Kind);
            }
            catch { /* ignore rollback failure */ }
        }
    }

    private async Task RenderContentAsync(PreviewContent content)
    {
        ImageScroll.Visibility = Visibility.Collapsed;
        PreviewWebView.Visibility = Visibility.Collapsed;
        VideoPanel.Visibility = Visibility.Collapsed;
        StopVideo();
        HidePreviewHandler();
        ResetZoom();

        if (content.Kind == PreviewKind.Image)
        {
            var ext = IoPath.GetExtension(content.FilePath).ToLowerInvariant();

            // SVG (and any image that already has HTML) → WebView2 paints like a picture
            if (ext == ".svg" || !string.IsNullOrWhiteSpace(content.RenderedHtml))
            {
                PreviewWebView.Visibility = Visibility.Visible;
                await EnsureWebViewAsync();
                await NavigateHtmlContentAsync(content);
                SummaryText.Text = ext == ".svg"
                    ? "SVG vector preview — zoom with scroll / Ctrl+wheel."
                    : SummaryText.Text;
                UpdateActionButtons(PreviewKind.Image);
                return;
            }

            ImageScroll.Visibility = Visibility.Visible;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(content.FilePath);
            bitmap.EndInit();
            PreviewImage.Source = bitmap;
            if (ImageAlphaBoard is not null)
            {
                ImageAlphaBoard.Visibility = ext is ".png" or ".webp" or ".svg"
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            PreviewImage.UpdateLayout();
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                FitImageToViewport(resetUserZoom: true);
            }), DispatcherPriority.Loaded);
            return;
        }

        if (content.Kind == PreviewKind.Video)
        {
            _editMode = false;
            VideoPanel.Visibility = Visibility.Visible;
            PreviewVideo.Visibility = Visibility.Visible;
            ConvertStatusText.Text = "";
            var playPath = content.FilePath;

            // Light path only: MediaElement autoplay. No LibVLC (was crashing).
            // Hard codecs: prepare in background after play attempt via MediaFailed.
            try
            {
                PreviewVideo.Source = new Uri(playPath);
                PreviewVideo.Volume = 1;
                PreviewVideo.Play();
                PlayPauseButton.Content = "Pause";
                _videoTimer.Start();
                SummaryText.Text = "Playing — press Edit for the full Netie video editor.";
                UpdateActionButtons(PreviewKind.Video);
                UpdateMarkRangeTint();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Video open failed: {ex.Message}");
                ConvertStatusText.Text = $"Could not open video: {ex.Message}";
            }

            return;
        }

        if (content.Kind == PreviewKind.ShellInfo &&
            !string.IsNullOrWhiteSpace(content.FilePath) &&
            !IoDirectory.Exists(content.FilePath))
        {
            EnsurePreviewHandler();
            if (_previewHandler!.TryShow(content.FilePath))
            {
                ConvertStatusText.Text = "Windows preview handler";
                return;
            }
        }

        PreviewWebView.Visibility = Visibility.Visible;
        await EnsureWebViewAsync();

        if (content.Kind == PreviewKind.Pdf)
        {
            _editMode = false;
            try
            {
                await NavigatePdfJsAsync(content.FilePath);
                SummaryText.Text = "PDF preview — press Edit for full Netie PDF tools (merge, sign, organize).";
                UpdateActionButtons(PreviewKind.Pdf);
                ApplyZoom();
            }
            catch (Exception ex)
            {
                try
                {
                    PreviewWebView.Source = new Uri(new Uri(content.FilePath).AbsoluteUri);
                    SummaryText.Text = "PDF preview — press Edit for tools.";
                    UpdateActionButtons(PreviewKind.Pdf);
                }
                catch
                {
                    ShowPdfLoadFailure(ex.Message);
                }
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(content.HtmlFilePath) || !string.IsNullOrWhiteSpace(content.RenderedHtml))
        {
            await NavigateHtmlContentAsync(content);
            return;
        }

        if (!string.IsNullOrWhiteSpace(content.RawText))
        {
            var escaped = System.Net.WebUtility.HtmlEncode(content.RawText).Replace("\n", "<br/>");
            await NavigateHtmlContentAsync(new PreviewContent
            {
                FilePath = content.FilePath,
                FileName = content.FileName,
                Kind = content.Kind,
                FileSizeBytes = content.FileSizeBytes,
                RenderedHtml =
                    $"<!DOCTYPE html><html><body style='background:#10141c;color:#f2f4f8;font:14px Segoe UI;padding:24px'>{escaped}</body></html>"
            });
        }
    }

    /// <summary>
    /// WebView2 NavigateToString fails on large Office HTML (base64 images) with
    /// "Value does not fall within the expected range". Always prefer file://.
    /// </summary>
    private async Task NavigateHtmlContentAsync(PreviewContent content)
    {
        await EnsureWebViewAsync();

        string? path = content.HtmlFilePath;
        if (string.IsNullOrWhiteSpace(path) || !IoFile.Exists(path))
        {
            if (string.IsNullOrWhiteSpace(content.RenderedHtml))
                return;

            try
            {
                var info = new System.IO.FileInfo(content.FilePath);
                path = PreviewHtmlDiskCache.Write(
                    content.FilePath,
                    info.Exists ? info.Length : content.FileSizeBytes,
                    info.Exists ? info.LastWriteTimeUtc : DateTime.UtcNow,
                    content.RenderedHtml);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"HTML disk write failed, trying NavigateToString: {ex.Message}");
                try
                {
                    PreviewWebView.NavigateToString(content.RenderedHtml);
                }
                catch (Exception navEx)
                {
                    StatusText.Text = $"Error: {navEx.Message}";
                    SummaryText.Text = "Could not load file.";
                }
                return;
            }
        }

        try
        {
            PreviewWebView.Source = new Uri(path);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"file:// navigate failed: {ex.Message}");
            if (!string.IsNullOrWhiteSpace(content.RenderedHtml) && content.RenderedHtml.Length < 750_000)
            {
                try { PreviewWebView.NavigateToString(content.RenderedHtml); }
                catch (Exception navEx)
                {
                    StatusText.Text = $"Error: {navEx.Message}";
                    SummaryText.Text = "Could not load file.";
                }
            }
            else
            {
                StatusText.Text = $"Error: {ex.Message}";
                SummaryText.Text = "Could not load file.";
            }
        }
    }

    private async void PreviewVideo_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath) || !_ffmpeg.IsAvailable)
        {
            ConvertStatusText.Text = $"Video playback failed: {e.ErrorException?.Message}";
            return;
        }

        ConvertStatusText.Text = "Codec not supported natively — transcoding with FFmpeg…";
        SetLoading(true, "Transcoding video for preview…");
        try
        {
            var prepared = await _ffmpeg.EnsurePlayablePreviewAsync(_currentFilePath);
            if (!string.IsNullOrWhiteSpace(prepared))
            {
                PreviewVideo.Source = new Uri(prepared);
                PreviewVideo.Play();
                PreviewVideo.Pause();
                ConvertStatusText.Text = "Playing H.264 preview (original unchanged)";
            }
            else
            {
                ConvertStatusText.Text = "FFmpeg could not prepare this video for preview.";
            }
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void ProviderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_ai.IsConfigured)
        {
            ProviderButton.Content = "AI: setup";
            ConvertStatusText.Text = "Add an API key…";
            _openApiSetup?.Invoke();
            return;
        }

        // Switch + paint UI on the same click — no await, no delay
        var next = _ai.SwitchToNextProvider();
        ProviderButton.Content = $"AI: {next}";
        ConvertStatusText.Text = $"AI → {next}";
        ProviderButton.InvalidateVisual();
        StatusText.Text = $"Provider: {next}";
    }

    private async Task EnsureWebViewAsync()
    {
        if (PreviewWebView.CoreWebView2 is not null)
            return;

        var env = await WebViewWarmup.GetEnvironmentAsync();
        if (env is not null)
            await PreviewWebView.EnsureCoreWebView2Async(env);
        else
            await PreviewWebView.EnsureCoreWebView2Async();

        var core = PreviewWebView.CoreWebView2;
        if (core is null)
            return;

        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        // Peek/QL-Win lesson: Chromium must not eat Esc / Ctrl+W / accelerators from the host.
        try { core.Settings.AreBrowserAcceleratorKeysEnabled = false; } catch { /* older runtime */ }
        // Trackpad pinch + Ctrl+wheel zoom for markdown / text / HTML / Office (PDF has its own path).
        try { core.Settings.IsZoomControlEnabled = true; } catch { /* older runtime */ }
        try { core.Settings.IsPinchZoomEnabled = true; } catch { /* older runtime */ }

        if (!_webViewHandlersWired)
        {
            _webViewHandlersWired = true;
            core.NavigationCompleted += PreviewWebView_NavigationCompleted;
            core.WebMessageReceived += PreviewWebView_WebMessageReceived;
            core.NavigationCompleted += (_, _) => _ = InjectWebViewTypingProbeAsync();
            // Keep the toolbar % in sync when the user pinch / Ctrl+wheel zooms non-PDF content.
            PreviewWebView.ZoomFactorChanged += PreviewWebView_ZoomFactorChanged;
        }

        // Map pdf.js assets for ES module viewer
        try
        {
            var pdfjsDir = IoPath.GetDirectoryName(PdfJsViewerService.FindViewerHtml() ?? "");
            if (!string.IsNullOrWhiteSpace(pdfjsDir) && IoDirectory.Exists(pdfjsDir))
            {
                core.SetVirtualHostNameToFolderMapping(
                    "netie.pdfjs",
                    pdfjsDir,
                    CoreWebView2HostResourceAccessKind.Allow);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"pdf.js host map failed: {ex.Message}");
        }

        core.ContextMenuRequested += async (_, args) =>
        {
            // Always inject items. PDF viewer often returns empty JS selection — fall back to clipboard on click.
            var deferral = args.GetDeferral();
            try
            {
                var selection = UnquoteJsString(await core.ExecuteScriptAsync(
                    "(function(){try{return window.getSelection().toString()}catch(e){return ''}})()"));

                var menuEnv = core.Environment;
                var lookupLabel = string.IsNullOrWhiteSpace(selection)
                    ? "Look Up with AI (use copied text)"
                    : "Look Up with AI";
                var addLabel = string.IsNullOrWhiteSpace(selection)
                    ? "Add to Chat (use copied text)"
                    : "Add to Chat";

                var lookup = menuEnv.CreateContextMenuItem(lookupLabel, null, CoreWebView2ContextMenuItemKind.Command);
                lookup.CustomItemSelected += async (_, _) =>
                {
                    var text = selection;
                    if (string.IsNullOrWhiteSpace(text))
                        text = Clipboard.GetText()?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        ConvertStatusText.Text = "Copy text first (Ctrl+C), then Look Up.";
                        return;
                    }
                    var (word, ctx) = await TryGetLookupPayloadAsync(text);
                    await RunLookupAsync(string.IsNullOrWhiteSpace(word) ? text : word, ctx);
                };

                var addChat = menuEnv.CreateContextMenuItem(addLabel, null, CoreWebView2ContextMenuItemKind.Command);
                addChat.CustomItemSelected += (_, _) =>
                {
                    var text = selection;
                    if (string.IsNullOrWhiteSpace(text))
                        text = Clipboard.GetText()?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        ConvertStatusText.Text = "Copy text first (Ctrl+C), then Add to Chat.";
                        return;
                    }
                    AddSelectionToChat(text);
                };

                args.MenuItems.Insert(0, addChat);
                args.MenuItems.Insert(0, lookup);
            }
            finally
            {
                deferral.Complete();
            }
        };
    }

    private async void LookupClipButton_Click(object sender, RoutedEventArgs e)
    {
        // Prefer live selection / image capture over clipboard
        if (_content?.Kind == PreviewKind.Image)
        {
            var shot = await CaptureVisiblePreviewPngAsync()
                       ?? CaptureImagePanelPng()
                       ?? _currentFilePath;
            if (!string.IsNullOrWhiteSpace(shot) && IoFile.Exists(shot))
            {
                if (!_chatOpen) OpenChatPanel();
                ChatPanel.StageImage(shot);
                await RunLookupAsync($"[image] {shot}");
                ConvertStatusText.Text = "Look Up · image screenshot";
                return;
            }
        }

        var text = await TryGetPreviewSelectionAsync();
        if (string.IsNullOrWhiteSpace(text))
            text = Clipboard.GetText()?.Trim() ?? "";

        // Excel Look Up with no selection → active sheet digest (Add = whole workbook)
        if (string.IsNullOrWhiteSpace(text) &&
            _content?.Kind == PreviewKind.Office &&
            !string.IsNullOrWhiteSpace(_currentFilePath) &&
            (IoPath.GetExtension(_currentFilePath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
             IoPath.GetExtension(_currentFilePath).Equals(".xlsm", StringComparison.OrdinalIgnoreCase)))
        {
            var sheet = _activeSheetName;
            var digest = string.IsNullOrWhiteSpace(sheet)
                ? OfficePreviewService.XlsxDigest(_currentFilePath!)
                : OfficePreviewService.XlsxSheetDigest(_currentFilePath!, sheet!);
            if (!_chatOpen) OpenChatPanel();
            ChatPanel.StagePackedContext(
                string.IsNullOrWhiteSpace(sheet) ? "Workbook" : $"Sheet: {sheet}",
                digest,
                string.IsNullOrWhiteSpace(sheet) ? "workbook look up" : $"sheet · {sheet}");
            await RunLookupAsync(string.IsNullOrWhiteSpace(sheet)
                ? "Summarise the active workbook sheet structure and key columns."
                : $"Explain sheet '{sheet}' — columns, sample rows, and notable values.");
            ConvertStatusText.Text = string.IsNullOrWhiteSpace(sheet)
                ? "Look Up · workbook digest"
                : $"Look Up · sheet {sheet}";
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            ConvertStatusText.Text = "Highlight text in the preview (or Ctrl+C), then Look Up.";
            return;
        }
        var (word, ctx) = await TryGetLookupPayloadAsync(text);
        await RunLookupAsync(string.IsNullOrWhiteSpace(word) ? text : word, ctx);
    }

    private async void SendSlideChatButton_Click(object sender, RoutedEventArgs e) =>
        await StageCurrentSlideToChatAsync();

    private async void AddChatClipButton_Click(object sender, RoutedEventArgs e)
    {
        // PPTX: attach the visible slide, not packed "PPTX preview" text or a whole-WebView shot.
        if (PptxSlideImageService.IsPptxPath(_currentFilePath))
        {
            if (await StageCurrentSlideToChatAsync())
                return;
        }

        // Whole-file attach: first-layer parse (OCR/text extract) before AI
        if (_content is not null && !string.IsNullOrWhiteSpace(_currentFilePath))
        {
            if (!_chatOpen) OpenChatPanel();

            if (_content.Kind == PreviewKind.Image)
            {
                var shot = await CaptureVisiblePreviewPngAsync() ?? CaptureImagePanelPng() ?? _currentFilePath;
                if (!string.IsNullOrWhiteSpace(shot))
                {
                    ChatPanel.StageImage(shot!);
                    try
                    {
                        var ocr = await ImageOcrService.RecognizeAsync(shot!);
                        if (ocr.Ok && ocr.Text.Trim().Length >= 20)
                        {
                            ChatPanel.StagePackedContext(
                                $"OCR · {IoPath.GetFileName(shot)}",
                                ocr.Text,
                                $"OCR · {ocr.Engine}");
                            ConvertStatusText.Text = $"Added image + OCR · {ocr.Engine}";
                        }
                        else
                        {
                            ConvertStatusText.Text = ocr.Engine == "none"
                                ? $"Added image · {IoPath.GetFileName(shot)} (no OCR)"
                                : $"Added image · OCR light ({ocr.Error ?? "empty"})";
                        }
                    }
                    catch
                    {
                        ConvertStatusText.Text = $"Added image · {IoPath.GetFileName(shot)}";
                    }
                    return;
                }
            }

            var raw = _content.RawText?.Trim() ?? "";
            if (raw.Length < 40 && _content.Kind is PreviewKind.Pdf or PreviewKind.Office or PreviewKind.Html)
            {
                // Visual fallback when text layer is thin (scanned PDF)
                var shot = await CaptureVisiblePreviewPngAsync();
                if (!string.IsNullOrWhiteSpace(shot))
                {
                    ChatPanel.StageImage(shot);
                    try
                    {
                        var ocr = await ImageOcrService.RecognizeAsync(shot);
                        if (ocr.Ok && ocr.Text.Trim().Length >= 20)
                        {
                            ChatPanel.StagePackedContext("OCR · page", ocr.Text, $"OCR · {ocr.Engine}");
                            ConvertStatusText.Text = $"Added page screenshot + OCR · {ocr.Engine}";
                        }
                        else
                            ConvertStatusText.Text = "Added page screenshot (thin text layer — parse/OCR light)";
                    }
                    catch
                    {
                        ConvertStatusText.Text = "Added page screenshot (thin text layer — parse/OCR light)";
                    }
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(raw) && raw.Length > 8)
            {
                var ext = IoPath.GetExtension(_currentFilePath);
                if (_content.Kind == PreviewKind.Office &&
                    (ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                     ext.Equals(".xlsm", StringComparison.OrdinalIgnoreCase)))
                {
                    // Add to Chat = whole workbook structured digest
                    var digest = OfficePreviewService.XlsxDigest(_currentFilePath!);
                    var pack = DocumentContextPacker.PackRaw(digest, PreviewKind.Office);
                    ChatPanel.StagePackedContext(
                        $"Workbook · {_content.FileName}",
                        pack.Text,
                        pack.Detail);
                    ConvertStatusText.Text = $"Added workbook · {pack.Detail}";
                    return;
                }

                var packed = DocumentContextPacker.Pack(_content);
                ChatPanel.StagePackedContext(
                    _content.FileName ?? IoPath.GetFileName(_currentFilePath)!,
                    packed.Text,
                    packed.Detail);
                ConvertStatusText.Text = $"Added to chat · {packed.Detail}";
                return;
            }
        }

        // Selection / clipboard fallback
        var sel = await TryGetPreviewSelectionAsync();
        if (string.IsNullOrWhiteSpace(sel))
            sel = Clipboard.GetText()?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(sel))
        {
            AddSelectionToChat(sel);
            ConvertStatusText.Text = "Added selection to chat.";
            return;
        }

        ConvertStatusText.Text = "Nothing to add — open a file or select text first.";
    }

    /// <summary>Word-style marquee. On PDF it arms the viewer; on images it clips the visible frame.</summary>
    private async void RegionSelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_content?.Kind == PreviewKind.Pdf)
        {
            await EnsureWebViewAsync();
            var core = PreviewWebView.CoreWebView2;
            if (core is null)
            {
                ConvertStatusText.Text = "Viewer still loading — try again in a moment.";
                return;
            }

            _regionMode = !_regionMode;
            core.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(
                new { type = "setRegionMode", on = _regionMode }));
            RegionSelectButton.Content = _regionMode ? "Selecting…" : "Select area";
            ConvertStatusText.Text = _regionMode
                ? "Drag a box over a table or figure — it parses, then stages in chat."
                : "Select area off.";
            return;
        }

        if (_content?.Kind == PreviewKind.Image)
        {
            // No marquee over the WPF Image host yet — stage the whole visible frame instead.
            var shot = await CaptureVisiblePreviewPngAsync() ?? CaptureImagePanelPng() ?? _currentFilePath;
            if (!string.IsNullOrWhiteSpace(shot) && IoFile.Exists(shot))
            {
                if (!_chatOpen) OpenChatPanel();
                ChatPanel.StageRegion(shot, null, "Image clip", "PNG");
                ConvertStatusText.Text = "Staged the visible image in chat.";
            }
            return;
        }

        ConvertStatusText.Text = "Select area works on PDFs and images.";
    }

    /// <summary>Persist a viewer region clip (crop pixels + parsed text) and stage it as a chat asset.</summary>
    private void HandleRegionClip(System.Text.Json.JsonElement root)
    {
        var page = root.TryGetProperty("page", out var pEl) && pEl.TryGetInt32(out var pv) ? pv : 1;
        var text = root.TryGetProperty("text", out var tEl) ? tEl.GetString() ?? "" : "";
        string? imagePath = null;

        if (root.TryGetProperty("dataUrl", out var duEl))
        {
            var dataUrl = duEl.GetString();
            if (!string.IsNullOrWhiteSpace(dataUrl) &&
                dataUrl.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var comma = dataUrl.IndexOf(',');
                    if (comma > 0)
                    {
                        var bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
                        var dir = AppResourceBudget.ChatClipsCache;
                        IoDirectory.CreateDirectory(dir);
                        imagePath = IoPath.Combine(dir, $"region_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                        IoFile.WriteAllBytes(imagePath, bytes);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Region clip save failed: {ex.Message}");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(imagePath) && string.IsNullOrWhiteSpace(text))
            return;

        if (!_chatOpen) OpenChatPanel();
        ChatPanel.StageRegion(imagePath ?? "", text, $"Page {page} clip", "PDF");
        ConvertStatusText.Text = string.IsNullOrWhiteSpace(text)
            ? $"Clip from page {page} staged in chat."
            : $"Clip from page {page} staged · {text.Length:N0} chars parsed.";
    }

    /// <summary>
    /// Stage the slide currently on screen into ChatPanel. Uses the exported PNG when
    /// present; otherwise the visible &lt;img&gt; pixels — never a whole-deck WebView capture.
    /// </summary>
    private async Task<bool> StageCurrentSlideToChatAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath) || !PptxSlideImageService.IsPptxPath(_currentFilePath))
            return false;

        var index = await TryGetVisibleSlideIndexAsync();
        if (index < 1)
            index = 1;

        string? png = null;
        try
        {
            png = await Task.Run(() => PptxSlideImageService.TryCopySlidePngForChat(
                _currentFilePath!,
                index,
                AppResourceBudget.ChatClipsCache,
                _content?.HtmlFilePath));
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Slide PNG resolve failed: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(png) || !IoFile.Exists(png))
            png = await CaptureVisibleSlidePngAsync(index);

        if (string.IsNullOrWhiteSpace(png) || !IoFile.Exists(png))
        {
            ConvertStatusText.Text = "Could not capture this slide — wait for the preview to finish loading.";
            return false;
        }

        if (!_chatOpen) OpenChatPanel();
        ChatPanel.StageSlideImage(png, index);
        ConvertStatusText.Text = $"Slide {index} staged in chat — add a question, then Send.";
        return true;
    }

    private async Task<int> TryGetVisibleSlideIndexAsync()
    {
        try
        {
            if (PreviewWebView.Visibility != Visibility.Visible || PreviewWebView.CoreWebView2 is null)
                return 1;
            var raw = UnquoteJsString(await PreviewWebView.CoreWebView2.ExecuteScriptAsync(
                PptxSlideImageService.VisibleSlideIndexScript));
            return int.TryParse(raw, out var n) && n >= 1 ? n : 1;
        }
        catch
        {
            return 1;
        }
    }

    private async Task<string?> CaptureVisibleSlidePngAsync(int slideIndex)
    {
        try
        {
            await EnsureWebViewAsync();
            var core = PreviewWebView.CoreWebView2;
            if (core is null)
                return null;

            var raw = UnquoteJsString(await core.ExecuteScriptAsync(
                PptxSlideImageService.VisibleSlideDataUrlScript));
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var dir = AppResourceBudget.ChatClipsCache;
            IoDirectory.CreateDirectory(dir);
            var path = IoPath.Combine(dir, $"slide_{Math.Max(1, slideIndex):D3}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            return PptxSlideImageService.TryWritePngFromDataUrl(raw, path);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Visible slide capture failed: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> TryGetPreviewSelectionAsync()
    {
        try
        {
            if (PreviewWebView.Visibility != Visibility.Visible || PreviewWebView.CoreWebView2 is null)
                return null;
            var raw = UnquoteJsString(await PreviewWebView.CoreWebView2.ExecuteScriptAsync(
                "(function(){try{return window.getSelection().toString()}catch(e){return ''}})()"));
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Selection plus nearby tokens / page for Look Up. Falls back to plain selection.
    /// </summary>
    private async Task<(string Word, string? Context)> TryGetLookupPayloadAsync(string? fallbackWord = null)
    {
        string? word = null;
        string? nearby = null;
        string? location = null;

        try
        {
            if (PreviewWebView.Visibility == Visibility.Visible && PreviewWebView.CoreWebView2 is not null)
            {
                var fwJson = System.Text.Json.JsonSerializer.Serialize(fallbackWord ?? "");
                var raw = UnquoteJsString(await PreviewWebView.CoreWebView2.ExecuteScriptAsync(
                    "(async function(fw){try{" +
                    "if(typeof window.netieGetLookupContextAsync==='function')" +
                    "return await window.netieGetLookupContextAsync(100,fw);" +
                    "if(typeof window.netieGetLookupContext==='function'){" +
                    "var o=JSON.parse(window.netieGetLookupContext(100));" +
                    "if((!o.word||!o.word.trim())&&fw)o.word=fw;" +
                    "return JSON.stringify(o);}" +
                    "return JSON.stringify({word:(window.getSelection&&window.getSelection().toString())||fw||''});" +
                    "}catch(e){return JSON.stringify({word:fw||''});}})(" + fwJson + ")"));

                if (!string.IsNullOrWhiteSpace(raw) && raw.TrimStart().StartsWith('{'))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    word = root.TryGetProperty("word", out var w) ? w.GetString() : null;
                    nearby = root.TryGetProperty("nearby", out var n) ? n.GetString() : null;
                    location = root.TryGetProperty("location", out var loc) ? loc.GetString() : null;
                    if (string.IsNullOrWhiteSpace(location) &&
                        root.TryGetProperty("page", out var p) && p.TryGetInt32(out var page))
                        location = $"page {page}";
                }
                else if (!string.IsNullOrWhiteSpace(raw))
                {
                    word = raw.Trim();
                }
            }
        }
        catch
        {
            /* fall through */
        }

        if (string.IsNullOrWhiteSpace(word))
            word = fallbackWord;
        if (string.IsNullOrWhiteSpace(word))
            word = await TryGetPreviewSelectionAsync();
        if (string.IsNullOrWhiteSpace(word))
            return ("", null);

        word = word.Trim();
        var ctx = BuildLookupSurroundingContext(word, nearby, location);
        return (word, ctx);
    }

    private string? BuildLookupSurroundingContext(string word, string? nearby, string? location)
    {
        var sb = new System.Text.StringBuilder();
        var fileName = _content?.FileName
                       ?? (string.IsNullOrWhiteSpace(_currentFilePath)
                           ? null
                           : IoPath.GetFileName(_currentFilePath));
        if (!string.IsNullOrWhiteSpace(fileName))
            sb.Append("File: ").Append(fileName).Append('\n');
        if (!string.IsNullOrWhiteSpace(location))
            sb.Append("Location: ").Append(location.Trim()).Append('\n');

        var summary = SummaryText.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(summary) &&
            summary.Length > 12 &&
            !summary.StartsWith("Generating", StringComparison.OrdinalIgnoreCase) &&
            !summary.StartsWith("Could not", StringComparison.OrdinalIgnoreCase) &&
            !summary.StartsWith("Summary hidden", StringComparison.OrdinalIgnoreCase) &&
            !summary.StartsWith("Playing", StringComparison.OrdinalIgnoreCase) &&
            !summary.StartsWith("PDF preview", StringComparison.OrdinalIgnoreCase))
        {
            var clip = summary.Length > 420 ? summary[..420] + "…" : summary;
            sb.Append("Summary: ").Append(clip).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(nearby))
        {
            sb.Append("Nearby tokens: …").Append(nearby.Trim()).Append('…');
        }
        else if (_content?.RawText is { Length: > 20 } raw)
        {
            // Office / HTML / text: locate selection in RawText when PDF JS context is absent
            var idx = raw.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (idx < 0 && word.Length >= 4)
            {
                // Soft match first token against raw excerpt
                var excerpt = raw.Length > 8000 ? raw[..8000] : raw;
                idx = excerpt.IndexOf(word.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            if (idx >= 0)
            {
                var start = Math.Max(0, idx - 100);
                var end = Math.Min(raw.Length, idx + word.Length + 100);
                sb.Append("Nearby tokens: …").Append(raw.AsSpan(start, end - start).ToString().Trim()).Append('…');
            }
        }

        var built = sb.ToString().Trim();
        return built.Length > 0 ? built : null;
    }

    private string? CaptureImagePanelPng()
    {
        try
        {
            if (ImageScroll.Visibility != Visibility.Visible || PreviewImage.Source is null)
                return null;
            var dir = AppResourceBudget.ChatClipsCache;
            IoDirectory.CreateDirectory(dir);
            var path = IoPath.Combine(dir, $"img_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            if (!string.IsNullOrWhiteSpace(_currentFilePath) && IoFile.Exists(_currentFilePath))
            {
                IoFile.Copy(_currentFilePath, path, overwrite: true);
                return path;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> CaptureVisiblePreviewPngAsync()
    {
        try
        {
            await EnsureWebViewAsync();
            var core = PreviewWebView.CoreWebView2;
            if (core is null)
                return null;

            var dir = AppResourceBudget.ChatClipsCache;
            IoDirectory.CreateDirectory(dir);
            var path = IoPath.Combine(dir, $"clip_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            await using var fs = IoFile.Create(path);
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
            return path;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Capture preview failed: {ex.Message}");
            return null;
        }
    }

    private void PreviewVideo_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (!PreviewVideo.NaturalDuration.HasTimeSpan)
            return;

        var dur = PreviewVideo.NaturalDuration.TimeSpan;
        VideoSlider.Maximum = Math.Max(0.001, dur.TotalSeconds);
        _videoEdit.Reset(dur, _currentFilePath);
        if (!string.IsNullOrWhiteSpace(_currentFilePath))
            _timeline.ResetFromSource(
                _currentFilePath, dur, PreviewVideo.NaturalVideoWidth, PreviewVideo.NaturalVideoHeight);
        // Full-clip keep range so CapCut-light handles are usable immediately
        _markIn = TimeSpan.Zero;
        _markOut = dur;
        UpdateMarkLabel();
        UpdateVideoUi();
        RefreshDeleteVisibility();
        if (TimelineHintText is not null)
            TimelineHintText.Text = "Timeline · Blade / Split · select clip · Delete · Export timeline";
    }

    private void BladeModeButton_Click(object sender, RoutedEventArgs e)
    {
        _bladeMode = !_bladeMode;
        BladeModeButton.Content = _bladeMode ? "Select ON" : "Select";
        BladeModeButton.Background = _bladeMode
            ? new SolidColorBrush(Color.FromArgb(0x55, 0x5E, 0xC4, 0xB8))
            : Brushes.Transparent;
        ConvertStatusText.Text = _bladeMode
            ? "Select ON — click a colored clip block to highlight it, then Delete clip."
            : "Click ✂ Split here at the playhead to cut.";
    }

    private void RefreshDeleteVisibility()
    {
        var isVideo = _content?.Kind == PreviewKind.Video;
        var canDelete = isVideo && _editMode && _videoEdit.HasSelection;
        CutMiddleButton.Visibility = isVideo && _editMode ? Visibility.Visible : Visibility.Collapsed;
        CutMiddleButton.IsEnabled = canDelete;
        CutMiddleButton.Opacity = canDelete ? 1.0 : 0.45;
    }

    private void SplitAtPlayhead_Click(object sender, RoutedEventArgs e)
    {
        if (VideoSlider.Maximum <= 0)
            return;
        if (PreviewVideo.Source is null)
            return;

        PushVideoUndo();
        var pos = GetPlayhead();
        var split = _videoEdit.HasMultipleSources
            ? _videoEdit.SplitAtPacked(TimeSpan.FromSeconds(VideoSlider.Value))
            : _videoEdit.SplitAt(pos);
        if (!split)
        {
            // Playbook Ctrl+Z after Split/Delete. A failed Split (playhead
            // still at packed 0 after + Clip) used to leave a no-op undo, so
            // the next Ctrl+Z popped + Clip and Export was ~1.6s.
            DiscardLastVideoUndo();
            ConvertStatusText.Text = "Split needs playhead inside a clip (not on an edge).";
            return;
        }

        SyncTimelineFromSession();

        // Reflect nearest kept edges on In/Out for trim handles
        // ApplyBlade selects the outgoing half. IndexAtPacked at the cut
        // is the incoming half; GetPlayhead after + Clip is clip-1 source.
        var idx = _videoEdit.SelectedIndex >= 0
            ? _videoEdit.SelectedIndex
            : (_videoEdit.HasMultipleSources
                ? _videoEdit.IndexAtPacked(TimeSpan.FromSeconds(VideoSlider.Value), fillGaps: false)
                : _videoEdit.IndexAt(pos));
        if (idx >= 0)
        {
            var kept = _videoEdit.Kept;
            if (idx + 1 < kept.Count && kept[idx].End == kept[idx + 1].Start)
            {
                _markIn = _videoEdit.PackedStartOf(idx);
                _markOut = _videoEdit.PackedEndOf(idx + 1);
            }
            else if (idx > 0 && kept[idx - 1].End == kept[idx].Start)
            {
                _markIn = _videoEdit.PackedStartOf(idx - 1);
                _markOut = _videoEdit.PackedEndOf(idx);
            }
            else
            {
                _markIn = _videoEdit.PackedStartOf(idx);
                _markOut = _videoEdit.PackedEndOf(idx);
            }
        }

        UpdateMarkLabel();
        RefreshDeleteVisibility();
        ScheduleTimelineLayout();
        ConvertStatusText.Text =
            $"✂ Split → {_videoEdit.Kept.Count} clips. Yellow marks = cuts. Click a clip (it highlights), then Delete.";
        if (TimelineHintText is not null)
            TimelineHintText.Text = _videoEdit.Describe();
        // Auto-select left piece so Delete appears immediately
        if (_videoEdit.SelectedIndex < 0 && _videoEdit.Kept.Count > 0)
        {
            var pick = idx >= 0 ? idx : 0;
            _videoEdit.Select(Math.Min(pick, _videoEdit.Kept.Count - 1));
            RefreshDeleteVisibility();
            ScheduleTimelineLayout();
        }
    }

    private void CutMiddleButton_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewVideo.Source is null)
            return;

        if (!_videoEdit.HasSelection)
        {
            ConvertStatusText.Text = "Select a clip on the timeline first (click it), then Delete.";
            return;
        }

        PushVideoUndo();

        // CapCut-style: Ripple (close gap) vs Leave black scene (add text later on the black)
        var choice = GlassChoiceDialog.Show(this,
            "Delete clip",
            "Ripple closes the gap. Black leaves a blank scene you can title later.",
            [
                ("merge", "Ripple delete (join)", true),
                ("black", "Leave black scene", false),
                ("cancel", "Cancel", false)
            ]);

        if (choice is null or "cancel")
        {
            DiscardLastVideoUndo();
            return;
        }

        _videoEdit.FillGapsWithBlack = choice == "black";
        _timeline.FillGapsWithBlack = _videoEdit.FillGapsWithBlack;

        var gapStart = _videoEdit.HasSelection
            ? _videoEdit.PackedStartOf(_videoEdit.SelectedIndex)
            : GetPlayhead();
        // Source End-Start ignores Speed. After 2x the visible clip is half
        // width; a source-length black card covered the rest of Export
        // ("picture is not black").
        var gapDur = _videoEdit.HasSelection
            ? _videoEdit.PackedEndOf(_videoEdit.SelectedIndex) - _videoEdit.PackedStartOf(_videoEdit.SelectedIndex)
            : TimeSpan.FromSeconds(1);

        var ok = _videoEdit.DeleteSelectedOrAt(GetPlayhead());
        if (ok)
            SyncTimelineFromSession();

        if (ok && choice == "black")
        {
            // Empty black title card — add text later via right-click → Add text
            _timeline.AddOverlay(OverlayKind.Black, gapStart,
                gapDur < TimeSpan.FromMilliseconds(200) ? TimeSpan.FromSeconds(1) : gapDur, null);
            var ov = _timeline.OverlayTrack?.Clips.LastOrDefault();
            if (ov is not null)
            {
                ov.FadeIn = TimeSpan.FromMilliseconds(280);
                ov.FadeOut = TimeSpan.FromMilliseconds(280);
                ov.TransitionIn = TransitionKind.Fade;
                ov.TransitionOut = TransitionKind.Fade;
            }
        }

        ConvertStatusText.Text = ok
            ? choice == "black"
                ? "Deleted — black scene ready. Right-click → Add text when you want a title."
                : "Deleted — ripple join (no gap)."
            : "Could not delete — select a clip first.";

        if (ok && !string.IsNullOrWhiteSpace(_currentFilePath))
            _timeline.EnsureAudioLinkedToVideo();

        if (!ok)
            return;

        if (_videoEdit.Kept.Count > 0)
        {
            _markIn = TimeSpan.Zero;
            _markOut = _videoEdit.PackedDuration(_videoEdit.FillGapsWithBlack);
        }
        else
        {
            _markIn = null;
            _markOut = null;
        }

        UpdateMarkLabel();
        RefreshDeleteVisibility();
        ScheduleTimelineLayout();
        if (TimelineHintText is not null)
            TimelineHintText.Text = _videoEdit.Describe();
    }

    private async void ExportEditButton_Click(object sender, RoutedEventArgs e) =>
        await ExportEditButton_ClickAsync();

    private async Task ExportEditButton_ClickAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath) || _videoEdit.Kept.Count == 0)
        {
            ConvertStatusText.Text = "Nothing to export.";
            return;
        }

        var output = PromptVideoSavePath("timeline");
        if (output is null)
            return;

        SyncTimelineFromSession();
        _timeline.ExportPreset = ReadExportPreset();
        _timeline.FillGapsWithBlack = _videoEdit.FillGapsWithBlack;

        var timelineSnapshot = _timeline.Clone();
        _exportCts = new CancellationTokenSource();
        _exportDetached = false;
        _busy = true;
        SetLoading(true, "Exporting timeline… 0%", showProgress: true);

        var progress = new Progress<ExportProgressReport>(r =>
        {
            SetLoading(true, r.Message, showProgress: true, percent: r.Percent);
        });

        try
        {
            var result = await BackgroundExportService.Instance.RunAsync(
                "Export timeline",
                output,
                (p, ct) => _ffmpeg.ExportTimelineAsync(timelineSnapshot, output, p, ct),
                progress,
                _exportCts.Token);

            if (!_exportDetached)
            {
                if (result.Success && IoFile.Exists(output))
                {
                    // Match the encode snapshot. Live _timeline can gain an SRT
                    // after Export clicked (whisper finish) and the toast would lie.
                    var burned = !string.IsNullOrWhiteSpace(timelineSnapshot.SubtitleBurnPath) &&
                                 IoFile.Exists(timelineSnapshot.SubtitleBurnPath);
                    ConvertStatusText.Text = burned
                        ? $"Saved timeline → {IoPath.GetFileName(output)} (captions burned in)"
                        : $"Saved timeline → {IoPath.GetFileName(output)}";
                    await LoadFileAsync(output);
                    PreviewVideo.Play();
                    PlayPauseButton.Content = "Pause";
                }
                else if (!result.Success)
                {
                    ConvertStatusText.Text = $"Export failed: {result.Error}";
                }
            }
            else
            {
                ConvertStatusText.Text = "Export continues in background — toast when done.";
            }
        }
        catch (Exception ex)
        {
            if (!_exportDetached)
                ConvertStatusText.Text = $"Export failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            SetLoading(false);
            _exportCts?.Dispose();
            _exportCts = null;
        }
    }

    private ExportPreset ReadExportPreset() => _exportPreset;

    private void ExportPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
            return;
        _exportPreset = tag switch
        {
            "Hd1080" => ExportPreset.Hd1080,
            "Uhd4K" => ExportPreset.Uhd4K,
            _ => ExportPreset.MatchSource
        };
        RefreshExportPresetButtons();
        ConvertStatusText.Text = _exportPreset switch
        {
            ExportPreset.Hd1080 => "Export preset: 1080p",
            ExportPreset.Uhd4K => "Export preset: 4K UHD",
            _ => "Export preset: Match source"
        };
    }

    private void RefreshExportPresetButtons()
    {
        void StyleItem(System.Windows.Controls.MenuItem? m, bool on)
        {
            if (m is null) return;
            m.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }

        StyleItem(VideoPresetMatchMenu, _exportPreset == ExportPreset.MatchSource);
        StyleItem(VideoPreset1080Menu, _exportPreset == ExportPreset.Hd1080);
        StyleItem(VideoPreset4KMenu, _exportPreset == ExportPreset.Uhd4K);

        if (VideoRibbonHint is not null)
        {
            VideoRibbonHint.Text = _exportPreset switch
            {
                ExportPreset.Hd1080 => "Preset 1080p · More for range/LUT",
                ExportPreset.Uhd4K => "Preset 4K · More for range/LUT",
                _ => "Export · + Clip · Order · More"
            };
        }
    }

    private void VideoMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.ContextMenu is null)
            return;
        btn.ContextMenu.PlacementTarget = btn;
        btn.ContextMenu.IsOpen = true;
    }

    private async void TrimVideoButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureMarks(out var a, out var b))
            return;

        var output = PromptVideoSavePath("trim");
        if (output is null)
            return;

        if (_editMode && _videoEdit.Kept.Count > 0)
        {
            SyncTimelineFromSession();
            var snap = _timeline.Clone();
            if (!snap.SlicePacked(a, b))
            {
                ConvertStatusText.Text = "Export range needs a span that covers a clip.";
                return;
            }

            await RunFfmpegAsync("Exporting selection...", () =>
                _ffmpeg.ExportTimelineAsync(snap, output));
            return;
        }

        await RunFfmpegAsync("Exporting selection...", () =>
            _ffmpeg.TrimAsync(_currentFilePath!, a, b, output));
    }

    private void PreviewWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_content?.Kind != PreviewKind.Pdf)
            return;

        if (e.IsSuccess)
        {
            OpenExternallyButton.Visibility = Visibility.Collapsed;
            ApplyZoom();
            return;
        }

        ShowPdfLoadFailure(e.WebErrorStatus.ToString());
    }

    private void ShowPdfLoadFailure(string detail)
    {
        StatusText.Text = "PDF preview failed — try Open externally.";
        ConvertStatusText.Text = string.IsNullOrWhiteSpace(detail) ? "" : detail;
        OpenExternallyButton.Visibility = Visibility.Visible;
        SummaryText.Text = "Could not display this PDF in the preview. Open it in your default PDF app instead.";
    }

    private void OpenExternallyButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath) || !IoFile.Exists(_currentFilePath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _currentFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Could not open externally: {ex.Message}";
        }
    }

    private async Task NavigatePdfJsAsync(string pdfPath)
    {
        await EnsureWebViewAsync();
        var core = PreviewWebView.CoreWebView2
                   ?? throw new InvalidOperationException("WebView2 not ready");

        var pdfDir = IoPath.GetDirectoryName(pdfPath)!;
        var pdfName = IoPath.GetFileName(pdfPath);
        core.SetVirtualHostNameToFolderMapping(
            "netie.pdfdoc",
            pdfDir,
            CoreWebView2HostResourceAccessKind.Allow);

        var cacheBust = DateTime.UtcNow.Ticks;
        var fileUrl = $"https://netie.pdfdoc/{Uri.EscapeDataString(pdfName)}?v={cacheBust}";
        var viewerUrl =
            $"https://netie.pdfjs/netie-viewer.html?file={Uri.EscapeDataString(fileUrl)}";
        PreviewWebView.Source = new Uri(viewerUrl);
    }

    private void PostFormFieldsToViewer(string pdfPath)
    {
        var core = PreviewWebView.CoreWebView2;
        if (core is null)
            return;

        try
        {
            var fields = PdfEditService.GetFormFieldRects(pdfPath)
                .Select(f => new
                {
                    page = f.Page,
                    x = f.X,
                    y = f.Y,
                    w = f.Width,
                    h = f.Height,
                    name = f.Name ?? ""
                })
                .ToList();

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "setFormFields",
                fields
            });
            core.PostWebMessageAsJson(payload);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Form field rects: {ex.Message}");
        }
    }

    private void PreviewWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json))
                json = e.WebMessageAsJson;
            if (!PdfJsViewerService.TryParseMessage(json, out var root))
                return;

            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "netieTyping")
            {
                _webViewTyping = root.TryGetProperty("typing", out var ty) && ty.ValueKind == System.Text.Json.JsonValueKind.True;
                return;
            }

            if (type == "activeSheet")
            {
                _activeSheetName = root.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                return;
            }

            if (type == "openPath")
            {
                var path = root.TryGetProperty("path", out var pEl) ? pEl.GetString() : null;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    path = path.Replace('/', '\\');
                    if (ExplorerFileService.PathExists(path))
                        _ = PushPreviewAsync(path);
                }
                return;
            }

            if (type == "textEdit")
            {
                var text = root.TryGetProperty("text", out var txEl) ? txEl.GetString() : null;
                if (text is not null)
                    ScheduleTextAutosave(text);
                return;
            }

            if (type == "htmlEdit")
            {
                if (_textEditMode && IsHtmlWordEditable())
                    ScheduleHtmlAutosave();
                return;
            }

            if (type == "regionClip")
            {
                HandleRegionClip(root);
                return;
            }

            if ((type == "stampPending" || type == "stampPlacement") && !string.IsNullOrWhiteSpace(_currentFilePath))
            {
                if (!root.TryGetProperty("page", out var pageEl) ||
                    !root.TryGetProperty("xPt", out var xEl) ||
                    !root.TryGetProperty("yFromBottom", out var yEl))
                {
                    ConvertStatusText.Text = "Stamp failed — incomplete placement data. Try Sign again.";
                    return;
                }

                var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString("N")
                    : Guid.NewGuid().ToString("N");
                var page = pageEl.GetInt32();
                var xPt = xEl.GetDouble();
                var yFromBottom = Math.Max(0, yEl.GetDouble());
                var stampW = root.TryGetProperty("stampW", out var swEl) && swEl.GetDouble() > 0
                    ? swEl.GetDouble()
                    : (double?)null;
                var stampH = root.TryGetProperty("stampH", out var shEl) && shEl.GetDouble() > 0
                    ? shEl.GetDouble()
                    : (double?)null;

                var existing = _pendingStamps.FirstOrDefault(s => s.Id == id);
                if (existing is null)
                {
                    existing = new PendingPdfStamp { Id = id };
                    _pendingStamps.Add(existing);
                }

                existing.Page = page;
                existing.XPt = xPt;
                existing.YFromBottom = yFromBottom;
                existing.StampW = stampW;
                existing.StampH = stampH;
                existing.ImagePath = _pendingStampImage;
                existing.TypedName = string.IsNullOrWhiteSpace(_pendingStampImage) ? _pendingStampName : null;
                existing.Font = _pendingStampFont ?? "Segoe Script";

                // Persist stamp pixels from viewer dataUrl when host path was lost
                if ((string.IsNullOrWhiteSpace(existing.ImagePath) || !IoFile.Exists(existing.ImagePath)) &&
                    root.TryGetProperty("dataUrl", out var duEl))
                {
                    var dataUrl = duEl.GetString();
                    if (!string.IsNullOrWhiteSpace(dataUrl) && dataUrl.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var comma = dataUrl.IndexOf(',');
                            if (comma > 0)
                            {
                                var b64 = dataUrl[(comma + 1)..];
                                var bytes = Convert.FromBase64String(b64);
                                var tmp = IoPath.Combine(IoPath.GetTempPath(), $"netie_stamp_{id}.png");
                                IoFile.WriteAllBytes(tmp, bytes);
                                existing.ImagePath = tmp;
                                _pendingStampImage = tmp;
                                existing.TypedName = null;
                            }
                        }
                        catch (Exception pngEx)
                        {
                            AppLogger.Warn($"Stamp dataUrl save failed: {pngEx.Message}");
                        }
                    }
                }

                EnsureStampBaseLocked(_currentFilePath!);
                _pdfDirty = true;

                ConvertStatusText.Text = _pendingStamps.Count == 1
                    ? "Signature placed — drag the box anytime · autosaving…"
                    : $"{_pendingStamps.Count} signatures — drag boxes · autosaving…";

                ScheduleStampAutosave();
            }
            else if (type == "stampMoved")
            {
                var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var stamp = _pendingStamps.FirstOrDefault(s => s.Id == id);
                if (stamp is null) return;
                if (root.TryGetProperty("page", out var pEl)) stamp.Page = pEl.GetInt32();
                if (root.TryGetProperty("xPt", out var xEl2)) stamp.XPt = xEl2.GetDouble();
                if (root.TryGetProperty("yFromBottom", out var yEl2)) stamp.YFromBottom = Math.Max(0, yEl2.GetDouble());
                if (root.TryGetProperty("stampW", out var sw) && sw.GetDouble() > 0) stamp.StampW = sw.GetDouble();
                if (root.TryGetProperty("stampH", out var sh) && sh.GetDouble() > 0) stamp.StampH = sh.GetDouble();
                _pdfDirty = true;
                ConvertStatusText.Text = "Signature moved — autosaving…";
                ScheduleStampAutosave();
            }
            else if (type == "stampDeleted")
            {
                var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) return;
                var removed = _pendingStamps.RemoveAll(s => s.Id == id);
                if (removed <= 0) return;
                // Stay dirty even at 0 stamps — the flush must run to restore the
                // clean base, otherwise the deleted stamp stays baked in the file.
                _pdfDirty = true;
                ConvertStatusText.Text = _pendingStamps.Count == 0
                    ? "Signature removed — autosaving…"
                    : $"Signature removed · {_pendingStamps.Count} left — autosaving…";
                ScheduleStampAutosave();
            }
            else if (type == "fillTexts" && !string.IsNullOrWhiteSpace(_currentFilePath))
            {
                _pendingFillTexts.Clear();
                if (root.TryGetProperty("texts", out var arr) &&
                    arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        var text = el.TryGetProperty("text", out var txt) ? txt.GetString() : null;
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        _pendingFillTexts.Add(new PendingPdfFillText
                        {
                            Id = el.TryGetProperty("id", out var fid) ? fid.GetString() ?? "" : "",
                            Page = el.TryGetProperty("page", out var pg) ? pg.GetInt32() : 1,
                            XPt = el.TryGetProperty("xPt", out var fx) ? fx.GetDouble() : 0,
                            YFromBottom = el.TryGetProperty("yFromBottom", out var fy) ? Math.Max(0, fy.GetDouble()) : 0,
                            WPt = el.TryGetProperty("wPt", out var fw) ? fw.GetDouble() : 120,
                            HPt = el.TryGetProperty("hPt", out var fh) ? fh.GetDouble() : 20,
                            FontPt = el.TryGetProperty("fontPt", out var fp) ? fp.GetDouble() : 11,
                            Text = text!,
                            CoverBackground = el.TryGetProperty("cover", out var cov) &&
                                             cov.ValueKind is System.Text.Json.JsonValueKind.True
                        });
                    }
                }

                EnsureStampBaseLocked(_currentFilePath!);
                _pdfDirty = true;
                // Quiet strip while typing — heavy "autosaving…" spam caused demo lag feel.
                ConvertStatusText.Text = _pendingFillTexts.Count == 0
                    ? ""
                    : $"{_pendingFillTexts.Count} text box(es)";
                ScheduleStampAutosave(fillHeavy: true);
            }
            else if ((type == "highlight" || type == "highlights") && !string.IsNullOrWhiteSpace(_currentFilePath))
            {
                AcceptViewerHighlights(root, replaceAll: type == "highlights");
            }
            else if (type == "flushReady")
            {
                _viewerFlushTcs?.TrySetResult(true);
            }
            else if (type == "pagePng")
            {
                static int IntProp(System.Text.Json.JsonElement el, string name, int fallback)
                {
                    if (!el.TryGetProperty(name, out var p) || p.ValueKind != System.Text.Json.JsonValueKind.Number)
                        return fallback;
                    return (int)Math.Round(p.GetDouble());
                }
                static double DblProp(System.Text.Json.JsonElement el, string name)
                {
                    if (!el.TryGetProperty(name, out var p) || p.ValueKind != System.Text.Json.JsonValueKind.Number)
                        return 0;
                    return p.GetDouble();
                }

                _pagePngTcs?.TrySetResult(new PagePngCapture
                {
                    Page = IntProp(root, "page", 1),
                    DataUrl = root.TryGetProperty("dataUrl", out var du) ? du.GetString() : null,
                    Error = root.TryGetProperty("error", out var errEl) ? errEl.GetString() : null,
                    WidthPt = DblProp(root, "widthPt"),
                    HeightPt = DblProp(root, "heightPt"),
                    WidthPx = IntProp(root, "widthPx", 0),
                    HeightPx = IntProp(root, "heightPx", 0)
                });
            }
            else if (type == "requestTextBoxes" && !string.IsNullOrWhiteSpace(_currentFilePath))
            {
                var page = root.TryGetProperty("page", out var pEl) ? pEl.GetInt32() : 1;
                PostTextBoxesToViewer(_currentFilePath!, page);
            }
            else if (type == "textBoxSelected")
            {
                var text = root.TryGetProperty("text", out var te) ? te.GetString() : "";
                ConvertStatusText.Text = string.IsNullOrWhiteSpace(text)
                    ? "Text box selected"
                    : $"Text: {text}";
            }
            else if (type == "ready")
            {
                // Keep status quiet for demos — long hint text was noisy and overlapped the strip.
                ConvertStatusText.Text = _pdfDirty
                    ? $"{PendingPdfEditCount()} edit(s) pending"
                    : "";
                if (!string.IsNullOrWhiteSpace(_currentFilePath))
                    PostFormFieldsToViewer(_currentFilePath);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"pdf.js message: {ex.Message}");
        }
    }

    private void AcceptViewerHighlights(System.Text.Json.JsonElement root, bool replaceAll)
    {
        var parsed = PdfJsViewerService.ReadHighlights(root);
        if (replaceAll)
        {
            _pendingHighlights.Clear();
            foreach (var h in parsed)
                _pendingHighlights.Add(ToPendingHighlight(h));
        }
        else
        {
            foreach (var h in parsed)
            {
                var existing = !string.IsNullOrWhiteSpace(h.Id)
                    ? _pendingHighlights.FirstOrDefault(x => x.Id == h.Id)
                    : null;
                if (existing is null)
                    _pendingHighlights.Add(ToPendingHighlight(h));
                else
                {
                    existing.Page = h.Page;
                    existing.XPt = h.XPt;
                    existing.YFromBottom = h.YFromBottom;
                    existing.WPt = h.WidthPt;
                    existing.HPt = h.HeightPt;
                    existing.Kind = h.Kind;
                }
            }
        }

        EnsureStampBaseLocked(_currentFilePath!);
        _pdfDirty = true;
        ConvertStatusText.Text = _pendingHighlights.Count == 0
            ? ""
            : $"{_pendingHighlights.Count} mark(s) — autosaving…";
        ScheduleStampAutosave();
    }

    private static PendingPdfHighlight ToPendingHighlight(PdfJsHighlight h) => new()
    {
        Id = string.IsNullOrWhiteSpace(h.Id) ? Guid.NewGuid().ToString("N") : h.Id,
        Page = h.Page,
        XPt = h.XPt,
        YFromBottom = h.YFromBottom,
        WPt = h.WidthPt,
        HPt = h.HeightPt,
        Kind = string.IsNullOrWhiteSpace(h.Kind) ? "highlight" : h.Kind
    };

    private int PendingPdfEditCount() =>
        _pendingStamps.Count + _pendingFillTexts.Count + _pendingHighlights.Count;

    private bool HasPendingPdfEdits() => PendingPdfEditCount() > 0;

    private string PendingPdfEditsLabel()
    {
        var parts = new List<string>();
        if (_pendingStamps.Count > 0) parts.Add($"{_pendingStamps.Count} stamp(s)");
        if (_pendingFillTexts.Count > 0) parts.Add($"{_pendingFillTexts.Count} text box(es)");
        if (_pendingHighlights.Count > 0) parts.Add($"{_pendingHighlights.Count} highlight(s)");
        return parts.Count == 0 ? "edits" : string.Join(" + ", parts);
    }

    private void TryPostViewerPersistResult(bool ok, string? message, bool final = false)
    {
        var core = PreviewWebView.CoreWebView2;
        if (core is null) return;
        try
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                type = ok ? "persistOk" : "persistFailed",
                message = message ?? "",
                final
            });
            core.PostWebMessageAsJson(payload);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"persist result post: {ex.Message}");
        }
    }

    private void PostTextBoxesToViewer(string pdfPath, int page)
    {
        var core = PreviewWebView.CoreWebView2;
        if (core is null) return;
        try
        {
            var boxes = PdfEditService.GetTextBoxes(pdfPath, page)
                .Select(b => new { page = b.Page, x = b.X, y = b.Y, w = b.Width, h = b.Height, text = b.Text })
                .ToList();
            var payload = System.Text.Json.JsonSerializer.Serialize(new { type = "setTextBoxes", boxes });
            core.PostWebMessageAsJson(payload);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Text boxes: {ex.Message}");
        }
    }

    private void EnsureStampBaseLocked(string currentPdf)
    {
        if (!string.IsNullOrWhiteSpace(_pdfStampBasePath) && IoFile.Exists(_pdfStampBasePath))
            return;
        try
        {
            _pdfStampBasePath = IoPath.Combine(IoPath.GetTempPath(), $"netie_sigbase_{Guid.NewGuid():N}.pdf");
            IoFile.Copy(currentPdf, _pdfStampBasePath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Stamp base copy failed: {ex.Message}");
            _pdfStampBasePath = null;
        }
    }

    private void ClearStampSession()
    {
        _stampAutosaveCts?.Cancel();
        _pendingStamps.Clear();
        _pendingFillTexts.Clear();
        _pendingHighlights.Clear();
        _pdfDirty = false;
        _pendingStampImage = null;
        _pendingStampName = null;
        _pendingStampFont = null;
        if (!string.IsNullOrWhiteSpace(_pdfStampBasePath))
        {
            try { IoFile.Delete(_pdfStampBasePath); } catch { /* ignore */ }
            _pdfStampBasePath = null;
        }
    }

    /// <param name="fillHeavy">
    /// Fill typing posts often; debounce longer so we do not rewrite the whole PDF every keystroke pause
    /// (that was lagging / quitting demos). Stamp place/move stays snappy at 250ms.
    /// </param>
    private void ScheduleStampAutosave(bool fillHeavy = false)
    {
        _stampAutosaveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _stampAutosaveCts = cts;
        var delayMs = fillHeavy ? 1600 : 250;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, cts.Token);
                await Dispatcher.InvokeAsync(async () =>
                {
                    // Still flush when dirty with 0 stamps (last stamp deleted → restore clean base)
                    if (cts.IsCancellationRequested || !_pdfDirty)
                        return;
                    await FlushPendingPdfStampsAsync(keepOverlaysEditable: true);
                });
            }
            catch (OperationCanceledException) { /* debounced */ }
        });
    }

    private async Task<bool> FlushPendingPdfStampsAsync(bool keepOverlaysEditable = false)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            _pdfDirty = false;
            return true;
        }

        // Nothing left to bake: restore clean base if we had baked any earlier.
        // Live-annotation model: the viewer canvas always shows the clean base, so
        // no reload is needed — the overlays that were deleted are already gone.
        if (!HasPendingPdfEdits())
        {
            if (!string.IsNullOrWhiteSpace(_pdfStampBasePath) && IoFile.Exists(_pdfStampBasePath))
            {
                try
                {
                    SetLoading(true, keepOverlaysEditable ? "Removing signature…" : "Restoring PDF…");
                    var dest = _currentFilePath!;
                    var tmp = IoPath.Combine(IoPath.GetTempPath(), $"netie_restore_{Guid.NewGuid():N}.pdf");
                    IoFile.Copy(_pdfStampBasePath!, tmp, overwrite: true);
                    IoFile.Copy(tmp, dest, overwrite: true);
                    try { IoFile.Delete(tmp); } catch { /* ignore */ }
                    _pdfDirty = false;
                    ConvertStatusText.Text = "Removed — file back to clean copy";
                }
                catch (Exception ex)
                {
                    ConvertStatusText.Text = $"Restore failed: {ex.Message}";
                    return false;
                }
                finally
                {
                    SetLoading(false);
                }
            }
            else
            {
                _pdfDirty = false;
            }
            return true;
        }

        var originalPath = _currentFilePath!;
        try
        {
            // Live overlay path must not flash a loading veil — that felt like the app was quitting mid-type.
            if (!keepOverlaysEditable)
                SetLoading(true, "Saving stamps into file…");
            EnsureStampBaseLocked(originalPath);
            var basePath = (!string.IsNullOrWhiteSpace(_pdfStampBasePath) && IoFile.Exists(_pdfStampBasePath))
                ? _pdfStampBasePath!
                : originalPath;

            // Work from clean base so move+resave never doubles stamps
            var work = IoPath.Combine(IoPath.GetTempPath(), $"netie_work_{Guid.NewGuid():N}.pdf");
            IoFile.Copy(basePath, work, overwrite: true);
            var path = work;

            // Release WebView lock only when we must fully reload
            if (!keepOverlaysEditable)
            {
                var core = PreviewWebView.CoreWebView2;
                if (core is not null)
                {
                    PreviewWebView.Source = new Uri("about:blank");
                    await Task.Delay(120);
                }
            }

            foreach (var s in _pendingStamps.ToList())
            {
                if (!string.IsNullOrWhiteSpace(s.ImagePath) && IoFile.Exists(s.ImagePath))
                {
                    path = await Task.Run(() => _pdfEdit.StampSignatureImage(
                        path, s.ImagePath!, s.Page, PdfEditService.DefaultStampMaxWidthPt,
                        s.XPt, s.YFromBottom, s.StampW, s.StampH));
                }
                else if (!string.IsNullOrWhiteSpace(s.TypedName))
                {
                    path = await Task.Run(() => _pdfEdit.StampTypedSignature(
                        path, s.TypedName!, s.Font ?? "Segoe Script", null, s.Page,
                        s.XPt, s.YFromBottom, s.StampW, s.StampH));
                }
            }

            if (_pendingFillTexts.Count > 0)
            {
                var fillItems = _pendingFillTexts
                    .Select(f => new PdfFillTextItem(f.Page, f.XPt, f.YFromBottom, f.WPt, f.HPt, f.FontPt, f.Text, f.CoverBackground))
                    .ToList();
                path = await Task.Run(() => _pdfEdit.DrawFillTexts(path, fillItems));
            }

            if (_pendingHighlights.Count > 0)
            {
                var hlItems = _pendingHighlights
                    .Select(h => new PdfHighlightItem(h.Page, h.XPt, h.YFromBottom, h.WPt, h.HPt, h.Kind))
                    .ToList();
                path = await Task.Run(() => _pdfEdit.DrawHighlights(path, hlItems));
            }

            // Write into the same file the user opened
            if (!string.Equals(path, originalPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!keepOverlaysEditable)
                {
                    // Viewer released — direct overwrite OK
                    IoFile.Copy(path, originalPath, overwrite: true);
                }
                else
                {
                    // Viewer still mapping the folder — try overwrite; fall back to brief unload
                    try
                    {
                        IoFile.Copy(path, originalPath, overwrite: true);
                    }
                    catch
                    {
                        var core = PreviewWebView.CoreWebView2;
                        if (core is not null)
                        {
                            PreviewWebView.Source = new Uri("about:blank");
                            await Task.Delay(150);
                        }
                        IoFile.Copy(path, originalPath, overwrite: true);
                        keepOverlaysEditable = false; // must reload
                    }
                }

                try { if (IoFile.Exists(path) && !string.Equals(path, originalPath, StringComparison.OrdinalIgnoreCase)) IoFile.Delete(path); }
                catch { /* ignore */ }
                try { if (IoFile.Exists(work) && !string.Equals(work, originalPath, StringComparison.OrdinalIgnoreCase) && !string.Equals(work, path, StringComparison.OrdinalIgnoreCase)) IoFile.Delete(work); }
                catch { /* ignore */ }
            }

            _pdfDirty = false;
            path = originalPath;
            _currentFilePath = path;

            if (keepOverlaysEditable)
            {
                // Live-annotation model (how Acrobat behaves): the HTML overlays stay
                // the visual truth while the session is open — no reload, no flicker,
                // no page jump, no ghost. Disk already holds the baked copy, re-baked
                // from the clean base on every change.
                ConvertStatusText.Text = "";
                TryPostViewerPersistResult(true, "", final: false);
            }
            else
            {
                // Final save: session ends, show the flattened file.
                ClearStampSession();
                await LoadFileAsync(path);
                ConvertStatusText.Text = $"Saved · {IoPath.GetFileName(path)}";
                TryPostViewerPersistResult(true, "", final: true);
            }

            return true;
        }
        catch (Exception ex)
        {
            // Fail closed: original file is only replaced after a successful bake-to-temp.
            // Keep the session overlay so the user does not lose marks; do not reload.
            ConvertStatusText.Text = $"Save failed: {ex.Message} — marks kept in session";
            TryPostViewerPersistResult(false, ex.Message);
            return false;
        }
        finally
        {
            SetLoading(false);
        }
    }

    private async Task RequestViewerFlushAsync()
    {
        var core = PreviewWebView.CoreWebView2;
        if (core is null) return;

        _viewerFlushTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        core.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "requestFlush" }));
        var completed = await Task.WhenAny(_viewerFlushTcs.Task, Task.Delay(1500));
        if (completed != _viewerFlushTcs.Task)
            AppLogger.Warn("Viewer flush timed out — saving whatever the host already has");
        // Let fillTexts / stampMoved messages land on the UI thread
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        await Task.Delay(80);
    }

    private async Task<bool> PromptSavePendingPdfAsync()
    {
        await RequestViewerFlushAsync();
        if (!_pdfDirty || !HasPendingPdfEdits())
            return true;

        var pendingLabel = PendingPdfEditsLabel();
        var choice = GlassChoiceDialog.Show(this,
            "Unsaved edits",
            $"{pendingLabel} not written yet. Save before closing?",
            [
                ("save", "Save PDF", true),
                ("discard", "Discard", false),
                ("cancel", "Cancel", false)
            ]);

        if (choice is null or "cancel")
            return false;
        if (choice == "discard")
        {
            ClearStampSession();
            return true;
        }

        return await FlushPendingPdfStampsAsync(keepOverlaysEditable: false);
    }

    private void LutButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose LUT (.cube)",
            Filter = "LUT|*.cube;*.3dl|All|*.*"
        };
        if (dlg.ShowDialog(this) != true)
            return;
        _timeline.GlobalLutPath = dlg.FileName;
        ConvertStatusText.Text = $"LUT set · {IoPath.GetFileName(dlg.FileName)} (applied on Export timeline)";
    }

    private void PreviewVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        PlayPauseButton.Content = "Play";
        PreviewVideo.Stop();
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlayPauseButton.Content?.ToString() == "Pause")
        {
            PreviewVideo.Pause();
            PlayPauseButton.Content = "Play";
        }
        else
        {
            PreviewVideo.Play();
            PlayPauseButton.Content = "Pause";
        }
    }

    private TimeSpan GetPlayhead() => PreviewVideo.Position;

    /// <summary>
    /// Overlay TimelineStart and the clip-track playhead line are packed.
    /// After + Clip the open file's <see cref="GetPlayhead"/> is still
    /// clip-1 source time.
    /// </summary>
    private TimeSpan PackedPlayhead()
    {
        if (_editMode && _videoEdit.Kept.Count > 0)
            return TimeSpan.FromSeconds(VideoSlider.Value);
        return GetPlayhead();
    }

    private void VideoSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        BeginScrub();
        // IsMoveToPointEnabled updates Value after PreviewMouseDown on some themes —
        // commit from click X so first seek is never the old playhead.
        if (sender is Slider slider && slider.ActualWidth > 1)
        {
            var t = Math.Clamp(e.GetPosition(slider).X / slider.ActualWidth, 0, 1);
            var sec = slider.Minimum + t * (slider.Maximum - slider.Minimum);
            slider.Value = sec;
            SeekTimelineSeconds(sec, force: true);
            ScheduleTimelineLayout();
        }
    }

    private void VideoSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e) => EndScrub(resume: true);

    private void VideoSlider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_sliderDragging)
            EndScrub(resume: true);
    }

    private double TimelineMaxSeconds()
    {
        if (_editMode && _content?.Kind == PreviewKind.Video && _videoEdit.Kept.Count > 0)
        {
            var packed = _videoEdit.PackedDuration(_videoEdit.FillGapsWithBlack).TotalSeconds;
            if (packed > 0)
                return packed;
        }

        return VideoSlider.Maximum > 0 ? VideoSlider.Maximum : 0.001;
    }

    private void SeekTimelineSeconds(double seconds, bool force = true)
    {
        if (_editMode && _content?.Kind == PreviewKind.Video && _videoEdit.Kept.Count > 0)
        {
            var source = _videoEdit.PackedToSource(TimeSpan.FromSeconds(seconds)).TotalSeconds;
            // PackedToSource(clip 2) is that file's source in. SkipTo would then
            // SourceToPacked on the open file (clip 1) and snap the slider back.
            // Split uses VideoSlider.Value as packed -- keep the packed time.
            SkipTo(source, force, updateSlider: false);
            _suppressSliderValueChanged = true;
            try { VideoSlider.Value = seconds; }
            finally { _suppressSliderValueChanged = false; }
            var packedMax = TimelineMaxSeconds();
            VideoTimeText.Text =
                $"{FormatTs(TimeSpan.FromSeconds(seconds))} / {FormatTs(TimeSpan.FromSeconds(packedMax))}";
            ScheduleTimelineLayout();
            return;
        }

        SkipTo(seconds, force);
    }

    private void VideoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSliderValueChanged || !_sliderDragging || PreviewVideo.Source is null)
            return;

        SeekTimelineSeconds(e.NewValue, force: false);
        ScheduleTimelineLayout();
    }

    /// <summary>Pause + mute while scrubbing so seeks skip cleanly without audio glitches.
    /// Does not seek here — click/drag updates Value first; seeking old Value + throttle blocked click-to-time.</summary>
    private void BeginScrub()
    {
        if (_sliderDragging)
            return;

        _sliderDragging = true;
        _wasPlayingBeforeScrub = PlayPauseButton.Content?.ToString() == "Pause";
        _volumeBeforeScrub = PreviewVideo.Volume;
        try
        {
            PreviewVideo.Pause();
            PreviewVideo.IsMuted = true;
            PreviewVideo.Volume = 0;
        }
        catch
        {
            // MediaElement may not be ready
        }

        PlayPauseButton.Content = "Play";
    }

    private void EndScrub(bool resume)
    {
        if (!_sliderDragging)
            return;

        _sliderDragging = false;
        SeekTimelineSeconds(VideoSlider.Value, force: true);

        var shouldResume = resume && _wasPlayingBeforeScrub && PreviewVideo.Source is not null;
        _wasPlayingBeforeScrub = false;

        // MediaElement seeks asynchronously — Play() too early snaps back to old time.
        // Hold sync longer and wait until Position nears the pending seek before Play().
        _suppressPositionSyncUntilUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(SeekSettleMs, 900));
        PreviewVideo.IsMuted = false;
        PreviewVideo.Volume = _volumeBeforeScrub;

        if (!shouldResume)
        {
            UpdateVideoUi();
            ScheduleTimelineLayout();
            return;
        }

        _seekResumeTimer?.Stop();
        _seekResumeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        var attempts = 0;
        var target = _pendingSeekSourceSeconds;
        _seekResumeTimer.Tick += (_, _) =>
        {
            attempts++;
            var pos = PreviewVideo.Position.TotalSeconds;
            var ready = target is null || Math.Abs(pos - target.Value) <= SeekToleranceSec || attempts >= 24;
            if (!ready)
                return;

            _seekResumeTimer.Stop();
            try
            {
                PreviewVideo.Play();
                PlayPauseButton.Content = "Pause";
            }
            catch
            {
                // ignore
            }

            UpdateVideoUi();
            ScheduleTimelineLayout();
        };
        _seekResumeTimer.Start();
    }

    /// <summary>Direct Position seek while paused/muted (source seconds). Holds UI on target until settle.</summary>
    private void SkipTo(double seconds, bool force = true, bool updateSlider = true)
    {
        if (PreviewVideo.Source is null)
            return;

        var sourceMax = PreviewVideo.NaturalDuration.HasTimeSpan
            ? PreviewVideo.NaturalDuration.TimeSpan.TotalSeconds
            : Math.Max(0.001, VideoSlider.Maximum);
        var clamped = Math.Clamp(seconds, 0, sourceMax);
        var now = DateTime.UtcNow;
        if (!force && (now - _lastScrubSeekUtc).TotalMilliseconds < 45)
            return;
        _lastScrubSeekUtc = now;

        _pendingSeekSourceSeconds = clamped;
        _suppressPositionSyncUntilUtc = now.AddMilliseconds(SeekSettleMs);

        try
        {
            PreviewVideo.Pause();
            PreviewVideo.Position = TimeSpan.FromSeconds(clamped);
        }
        catch
        {
            // MediaElement can throw during rapid seek
        }

        if (!updateSlider)
            return;

        var displayMax = TimelineMaxSeconds();
        var displayPos = _editMode && _content?.Kind == PreviewKind.Video && _videoEdit.Kept.Count > 0
            ? _videoEdit.SourceToPacked(TimeSpan.FromSeconds(clamped)).TotalSeconds
            : clamped;

        _suppressSliderValueChanged = true;
        try
        {
            if (displayMax > 0)
                VideoSlider.Value = Math.Min(displayPos, displayMax);
        }
        finally
        {
            _suppressSliderValueChanged = false;
        }

        VideoTimeText.Text =
            $"{FormatTs(TimeSpan.FromSeconds(displayPos))} / {FormatTs(TimeSpan.FromSeconds(displayMax))}";
        ScheduleTimelineLayout();
    }

    /// <summary>
    /// In edit mode marks are packed seconds (Export range slices the
    /// timeline). After + Clip, source times from two files cannot be
    /// compared or TrimAsync'd on the open file.
    /// </summary>
    private TimeSpan MarkFromPlayhead()
    {
        if (_editMode && _videoEdit.Kept.Count > 0)
            return PackedPlayhead();
        return GetPlayhead();
    }

    private void MarkInButton_Click(object sender, RoutedEventArgs e)
    {
        _markIn = MarkFromPlayhead();
        if (_markOut.HasValue && _markOut < _markIn)
            _markOut = _markIn;
        UpdateMarkLabel();
    }

    private void MarkOutButton_Click(object sender, RoutedEventArgs e)
    {
        _markOut = MarkFromPlayhead();
        if (_markIn.HasValue && _markOut < _markIn)
            _markIn = _markOut;
        UpdateMarkLabel();
    }

    private void UpdateMarkLabel()
    {
        var a = _markIn.HasValue ? FormatTs(_markIn.Value) : "—";
        var b = _markOut.HasValue ? FormatTs(_markOut.Value) : "—";
        MarkRangeText.Text = $"In {a} · Out {b}";
        UpdateMarkRangeTint();
        ScheduleTimelineLayout();
    }

    private void VideoSliderHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateMarkRangeTint();

    private void TimelineTrackHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ScheduleTimelineLayout();

    /// <summary>
    /// Coalesce timeline canvas rebuilds onto Loaded priority so we never mutate
    /// Canvas.Children during SizeChanged / UpdateLayout (was crashing Edit on video).
    /// </summary>
    private void ScheduleTimelineLayout()
    {
        if (_timelineLayoutPending)
            return;
        _timelineLayoutPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _timelineLayoutPending = false;
            try
            {
                UpdateTimelineLayout();
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "UpdateTimelineLayout");
            }
        }, DispatcherPriority.Background);
    }

    private void UpdateMarkRangeTint()
    {
        if (MarkRangeTint is null || VideoSlider is null)
            return;

        var max = TimelineMaxSeconds();
        if (max <= 0 || !_markIn.HasValue || !_markOut.HasValue || _markOut <= _markIn)
        {
            MarkRangeTint.Visibility = Visibility.Collapsed;
            MarkRangeTint.Width = 0;
            return;
        }

        var trackWidth = Math.Max(0, VideoSlider.ActualWidth - 12);
        if (trackWidth <= 1)
        {
            MarkRangeTint.Visibility = Visibility.Collapsed;
            return;
        }

        var start = Math.Clamp(_markIn.Value.TotalSeconds / max, 0, 1);
        var end = Math.Clamp(_markOut.Value.TotalSeconds / max, 0, 1);
        if (end <= start)
        {
            MarkRangeTint.Visibility = Visibility.Collapsed;
            return;
        }

        MarkRangeTint.Visibility = Visibility.Visible;
        MarkRangeTint.Margin = new Thickness(6 + start * trackWidth, 0, 0, 0);
        MarkRangeTint.Width = Math.Max(2, (end - start) * trackWidth);
    }

    private void UpdateTimelineLayout()
    {
        if (!_editMode || _content?.Kind != PreviewKind.Video)
            return;
        if (VideoPanel?.Visibility != Visibility.Visible)
            return;
        if (TimelineTrackHost is null || TimelineKeepRange is null || VideoSlider is null)
            return;

        var w = TimelineTrackHost.ActualWidth;
        var max = TimelineMaxSeconds();
        if (w < 8 || max <= 0)
            return;

        var inSec = Math.Clamp((_markIn ?? TimeSpan.Zero).TotalSeconds, 0, max);
        var outSec = Math.Clamp((_markOut ?? TimeSpan.FromSeconds(max)).TotalSeconds, 0, max);
        if (outSec < inSec)
            (inSec, outSec) = (outSec, inSec);

        var xIn = inSec / max * w;
        var xOut = outSec / max * w;
        // Packed slider is the clip-track ruler. GetPlayhead after + Clip
        // (and after ripple) is source time on that packed width.
        var posSec = PackedPlayhead().TotalSeconds;
        var xPos = Math.Clamp(posSec / max, 0, 1) * w;

        TimelineKeepRange.Margin = new Thickness(xIn, 0, 0, 0);
        TimelineKeepRange.Width = Math.Max(0, xOut - xIn);

        if (TimelineDimLeft is not null)
        {
            TimelineDimLeft.Width = Math.Max(0, xIn);
            TimelineDimLeft.Margin = new Thickness(0, 0, 0, 0);
        }

        if (TimelineDimRight is not null)
        {
            TimelineDimRight.Width = Math.Max(0, w - xOut);
            TimelineDimRight.Margin = new Thickness(xOut, 0, 0, 0);
        }

        if (TimelinePlayhead is not null)
            TimelinePlayhead.Margin = new Thickness(Math.Clamp(xPos - 1, 0, Math.Max(0, w - 2)), 0, 0, 0);

        const double handleW = 10;
        if (TimelineInHandle is not null)
            TimelineInHandle.Margin = new Thickness(Math.Clamp(xIn - handleW / 2, 0, Math.Max(0, w - handleW)), 0, 0, 0);
        if (TimelineOutHandle is not null)
            TimelineOutHandle.Margin = new Thickness(Math.Clamp(xOut - handleW / 2, 0, Math.Max(0, w - handleW)), 0, 0, 0);

        RenderKeptClipSegments(w, max);
        RenderAudioTrackClips(AudioTrack1Canvas, AudioTrack1Host, "A1", w, max);
        RenderAudioTrackClips(AudioTrack2Canvas, AudioTrack2Host, "A2", w, max);
        RenderCaptionTrack(w, max);
    }

    private void RenderKeptClipSegments(double w, double max)
    {
        if (TimelineClipCanvas is null)
            return;

        TimelineClipCanvas.Children.Clear();
        TimelineClipCanvas.Width = w;
        // CapCut-style: pack clips left-to-right; black fillers for holes in source time
        var packed = _videoEdit.BuildPackedTimeline(_videoEdit.FillGapsWithBlack);
        var total = _videoEdit.PackedDuration(_videoEdit.FillGapsWithBlack);
        if (total <= TimeSpan.Zero)
            total = TimeSpan.FromSeconds(Math.Max(max, 0.001));

        foreach (var seg in packed)
        {
            var x0 = seg.TimelineIn.TotalSeconds / total.TotalSeconds * w;
            var x1 = seg.TimelineOut.TotalSeconds / total.TotalSeconds * w;
            var selected = !seg.IsBlack && seg.ClipIndex == _videoEdit.SelectedIndex;
            var fill = seg.IsBlack
                ? new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x18))
                : selected
                    ? new SolidColorBrush(Color.FromRgb(0x5E, 0xC4, 0xB8))
                    : new SolidColorBrush(Color.FromRgb(0x3A, 0x7A, 0xC0));
            var width = Math.Max(4, x1 - x0 - 2);
            // Skip tiny black fillers that clutter the track when ripple-merge is the default
            if (seg.IsBlack && width < 10)
                continue;
            var border = new Border
            {
                Width = width,
                Height = 34,
                CornerRadius = new CornerRadius(6),
                Background = fill,
                BorderBrush = selected
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x4A))
                    : new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(selected ? 3 : 1),
                Cursor = seg.IsBlack ? Cursors.Arrow : Cursors.SizeAll,
                Tag = seg.IsBlack ? -1 : seg.ClipIndex,
                Opacity = selected ? 1.0 : 0.92,
                IsHitTestVisible = !seg.IsBlack,
                // No DropShadowEffect — per-clip effects + AllowsTransparency caused stack overflow (0xc00000fd)
                ToolTip = seg.IsBlack
                    ? $"Black gap {FormatTs(seg.SourceOut - seg.SourceIn)} (auto-filled)"
                    : selected
                        ? $"SELECTED Clip {seg.ClipIndex + 1} — Delete removes it · drag to reorder"
                        : $"Clip {seg.ClipIndex + 1}: click to select · drag to reorder · Delete"
            };
            var label = new TextBlock
            {
                Text = ClipBlockLabel(seg, selected),
                Foreground = Brushes.White,
                FontSize = selected ? 12 : 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0),
                IsHitTestVisible = false
            };
            border.Child = label;
            border.PreviewMouseLeftButtonDown += ClipBlock_PreviewMouseLeftButtonDown;
            border.PreviewMouseMove += ClipBlock_PreviewMouseMove;
            border.PreviewMouseLeftButtonUp += ClipBlock_PreviewMouseLeftButtonUp;
            border.LostMouseCapture += ClipBlock_LostMouseCapture;
            Canvas.SetLeft(border, x0 + 1);
            Canvas.SetTop(border, 1);
            TimelineClipCanvas.Children.Add(border);

            // Premiere-style fade triangles on clip ends (drag to set duration)
            if (!seg.IsBlack && _timeline.VideoTrack?.Clips.ElementAtOrDefault(seg.ClipIndex) is { } tclip)
            {
                var durSec = Math.Max(0.01, (seg.TimelineOut - seg.TimelineIn).TotalSeconds);
                var fadeInPx = Math.Clamp(tclip.FadeIn.TotalSeconds / durSec * width, 0, width * 0.45);
                var fadeOutPx = Math.Clamp(tclip.FadeOut.TotalSeconds / durSec * width, 0, width * 0.45);
                if (fadeInPx > 2 || tclip.FadeIn > TimeSpan.Zero)
                {
                    var fin = new System.Windows.Shapes.Polygon
                    {
                        Points = new PointCollection { new(0, 34), new(fadeInPx, 34), new(0, 0) },
                        Fill = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xD6, 0x4A)),
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(fin, x0 + 1);
                    Canvas.SetTop(fin, 1);
                    TimelineClipCanvas.Children.Add(fin);
                }

                if (fadeOutPx > 2 || tclip.FadeOut > TimeSpan.Zero)
                {
                    var fout = new System.Windows.Shapes.Polygon
                    {
                        Points = new PointCollection { new(0, 34), new(fadeOutPx, 34), new(fadeOutPx, 0) },
                        Fill = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xD6, 0x4A)),
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(fout, x1 - fadeOutPx - 1);
                    Canvas.SetTop(fout, 1);
                    TimelineClipCanvas.Children.Add(fout);
                }

                // Drag handles on edges
                var hi = new Border
                {
                    Width = 8,
                    Height = 34,
                    Background = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)),
                    Cursor = Cursors.SizeWE,
                    Tag = $"fi:{seg.ClipIndex}",
                    ToolTip = "Drag fade in"
                };
                hi.PreviewMouseLeftButtonDown += FadeHandle_PreviewMouseLeftButtonDown;
                hi.PreviewMouseMove += FadeHandle_PreviewMouseMove;
                hi.PreviewMouseLeftButtonUp += FadeHandle_PreviewMouseLeftButtonUp;
                hi.LostMouseCapture += FadeHandle_LostMouseCapture;
                Canvas.SetLeft(hi, x0 + 1);
                Canvas.SetTop(hi, 1);
                TimelineClipCanvas.Children.Add(hi);

                var ho = new Border
                {
                    Width = 8,
                    Height = 34,
                    Background = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)),
                    Cursor = Cursors.SizeWE,
                    Tag = $"fo:{seg.ClipIndex}",
                    ToolTip = "Drag fade out"
                };
                ho.PreviewMouseLeftButtonDown += FadeHandle_PreviewMouseLeftButtonDown;
                ho.PreviewMouseMove += FadeHandle_PreviewMouseMove;
                ho.PreviewMouseLeftButtonUp += FadeHandle_PreviewMouseLeftButtonUp;
                ho.LostMouseCapture += FadeHandle_LostMouseCapture;
                Canvas.SetLeft(ho, x1 - 9);
                Canvas.SetTop(ho, 1);
                TimelineClipCanvas.Children.Add(ho);
            }
        }
    }

    private int _fadeDragClip = -1;
    private bool _fadeDragIn;
    private Point _fadeDragStart;
    private TimeSpan _fadeDragBase;
    private readonly List<(TimeSpan Start, TimeSpan End, string Text)> _captionCues = [];
    private int _captionDragIndex = -1;
    private Point _captionDragStart;
    private TimeSpan _captionDragBaseStart;
    private TimeSpan _captionDragBaseEnd;

    private void FadeHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string tag } b)
            return;
        var parts = tag.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var idx))
            return;
        if (!string.IsNullOrWhiteSpace(_currentFilePath))
            SyncTimelineFromSession();
        var clip = _timeline.VideoTrack?.Clips.ElementAtOrDefault(idx);
        if (clip is null) return;

        _fadeDragClip = idx;
        _fadeDragIn = parts[0] == "fi";
        _fadeDragStart = e.GetPosition(TimelineClipCanvas);
        _fadeDragBase = _fadeDragIn ? clip.FadeIn : clip.FadeOut;
        PushVideoUndo();
        _videoEdit.Select(idx);
        b.CaptureMouse();
        e.Handled = true;
    }

    private void FadeHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_fadeDragClip < 0 || e.LeftButton != MouseButtonState.Pressed)
            return;
        var clip = _timeline.VideoTrack?.Clips.ElementAtOrDefault(_fadeDragClip);
        if (clip is null || TimelineClipCanvas is null) return;
        var pos = e.GetPosition(TimelineClipCanvas);
        var dx = pos.X - _fadeDragStart.X;
        var max = Math.Max(0.001, TimelineMaxSeconds());
        var w = Math.Max(1, TimelineClipCanvas.Width);
        var deltaSec = dx / w * max;
        if (!_fadeDragIn) deltaSec = -deltaSec;
        var next = TimeSpan.FromSeconds(Math.Clamp(_fadeDragBase.TotalSeconds + deltaSec, 0, 5));
        if (_fadeDragIn) clip.FadeIn = next;
        else clip.FadeOut = next;
        ConvertStatusText.Text =
            $"Fade {(_fadeDragIn ? "in" : "out")} {next.TotalSeconds:0.0}s on clip {_fadeDragClip + 1}";
        ScheduleTimelineLayout();
    }

    private void FadeHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndFadeHandleDrag(sender as Border);
        e.Handled = true;
    }

    private void FadeHandle_LostMouseCapture(object sender, MouseEventArgs e) =>
        EndFadeHandleDrag(sender as Border);

    /// <summary>
    /// MouseDown always PushVideoUndo. Alt-tab / capture-loss used to skip
    /// MouseUp, so the no-op frame stayed and Ctrl+Z popped + Clip.
    /// Clear the drag index before ReleaseMouseCapture so LostMouseCapture
    /// does not apply+discard a second time (that would undo a real fade).
    /// </summary>
    private void EndFadeHandleDrag(Border? handle)
    {
        if (_fadeDragClip < 0)
            return;

        var changed = false;
        var clip = _timeline.VideoTrack?.Clips.ElementAtOrDefault(_fadeDragClip);
        if (clip is not null)
        {
            // Export SyncTimelineFromSession() overwrites timeline fades from session.
            if (_fadeDragIn)
                changed = _videoEdit.ApplyFadeAt(_fadeDragClip, clip.FadeIn, null);
            else
                changed = _videoEdit.ApplyFadeAt(_fadeDragClip, null, clip.FadeOut);
        }

        if (!changed)
            DiscardLastVideoUndo();

        _fadeDragClip = -1;
        handle?.ReleaseMouseCapture();
    }

    private void RenderCaptionTrack(double w, double max)
    {
        if (CaptionTrackCanvas is null)
            return;
        CaptionTrackCanvas.Children.Clear();
        CaptionTrackCanvas.Width = w;
        if (w < 8 || max <= 0 || _captionCues.Count == 0)
            return;

        for (var i = 0; i < _captionCues.Count; i++)
        {
            var cue = _captionCues[i];
            var x0 = cue.Start.TotalSeconds / max * w;
            var x1 = cue.End.TotalSeconds / max * w;
            var bdr = new Border
            {
                Width = Math.Max(8, x1 - x0 - 1),
                Height = 20,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(0xBB, 0x5E, 0xC4, 0xB8)),
                Cursor = Cursors.SizeAll,
                Tag = i,
                ToolTip = cue.Text
            };
            var tb = new TextBlock
            {
                Text = cue.Text.Length > 24 ? cue.Text[..24] + "…" : cue.Text,
                FontSize = 10,
                Foreground = Brushes.White,
                Margin = new Thickness(4, 2, 4, 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false
            };
            bdr.Child = tb;
            bdr.PreviewMouseLeftButtonDown += CaptionCue_PreviewMouseLeftButtonDown;
            bdr.PreviewMouseMove += CaptionCue_PreviewMouseMove;
            bdr.PreviewMouseLeftButtonUp += CaptionCue_PreviewMouseLeftButtonUp;
            bdr.PreviewMouseRightButtonUp += CaptionCue_PreviewMouseRightButtonUp;
            Canvas.SetLeft(bdr, x0);
            Canvas.SetTop(bdr, 2);
            CaptionTrackCanvas.Children.Add(bdr);
        }
    }

    private void CaptionCue_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: int idx } b || idx < 0 || idx >= _captionCues.Count)
            return;
        _captionDragIndex = idx;
        _captionDragStart = e.GetPosition(CaptionTrackHost);
        var cue = _captionCues[idx];
        _captionDragBaseStart = cue.Start;
        _captionDragBaseEnd = cue.End;
        b.CaptureMouse();
        e.Handled = true;
    }

    private void CaptionCue_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_captionDragIndex < 0 || e.LeftButton != MouseButtonState.Pressed || CaptionTrackHost is null)
            return;
        var max = Math.Max(0.001, TimelineMaxSeconds());
        var w = Math.Max(1, CaptionTrackHost.ActualWidth);
        var dx = e.GetPosition(CaptionTrackHost).X - _captionDragStart.X;
        var delta = TimeSpan.FromSeconds(dx / w * max);
        var dur = _captionDragBaseEnd - _captionDragBaseStart;
        var start = TimeSpan.FromSeconds(Math.Max(0, (_captionDragBaseStart + delta).TotalSeconds));
        var end = start + dur;
        _captionCues[_captionDragIndex] = (start, end, _captionCues[_captionDragIndex].Text);
        ScheduleTimelineLayout();
    }

    private void CaptionCue_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b)
            b.ReleaseMouseCapture();
        if (_captionDragIndex >= 0)
            PersistCaptionCuesToSrt();
        _captionDragIndex = -1;
        e.Handled = true;
    }

    private void CaptionCue_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: int idx } || idx < 0 || idx >= _captionCues.Count)
            return;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem
        {
            Header = "Merge with next",
            IsEnabled = idx + 1 < _captionCues.Count
        });
        if (menu.Items[0] is MenuItem mergeNext)
        {
            mergeNext.Click += (_, _) =>
            {
                if (idx + 1 >= _captionCues.Count) return;
                var a = _captionCues[idx];
                var b = _captionCues[idx + 1];
                _captionCues[idx] = (a.Start, b.End, (a.Text + " " + b.Text).Trim());
                _captionCues.RemoveAt(idx + 1);
                PersistCaptionCuesToSrt();
                ScheduleTimelineLayout();
            };
        }
        menu.Items.Add(new MenuItem
        {
            Header = "Merge with previous",
            IsEnabled = idx > 0
        });
        if (menu.Items[1] is MenuItem mergePrev)
        {
            mergePrev.Click += (_, _) =>
            {
                if (idx <= 0) return;
                var a = _captionCues[idx - 1];
                var b = _captionCues[idx];
                _captionCues[idx - 1] = (a.Start, b.End, (a.Text + " " + b.Text).Trim());
                _captionCues.RemoveAt(idx);
                PersistCaptionCuesToSrt();
                ScheduleTimelineLayout();
            };
        }
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void PersistCaptionCuesToSrt()
    {
        try
        {
            var path = _timeline.SubtitleBurnPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                if (string.IsNullOrWhiteSpace(_currentFilePath)) return;
                path = IoPath.Combine(
                    IoPath.GetDirectoryName(_currentFilePath) ?? "",
                    IoPath.GetFileNameWithoutExtension(_currentFilePath) + ".srt");
                _timeline.SubtitleBurnPath = path;
            }

            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < _captionCues.Count; i++)
            {
                var c = _captionCues[i];
                sb.AppendLine((i + 1).ToString());
                sb.AppendLine($"{FormatSrtTs(c.Start)} --> {FormatSrtTs(c.End)}");
                sb.AppendLine(c.Text);
                sb.AppendLine();
            }
            IoFile.WriteAllText(path, sb.ToString());
            ConvertStatusText.Text = $"Captions updated · {IoPath.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Caption save failed: {ex.Message}";
        }
    }

    private static string FormatSrtTs(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
    }

    private void LoadCaptionCuesFromSrt(string srtPath)
    {
        _captionCues.Clear();
        try
        {
            if (!IoFile.Exists(srtPath)) return;
            var blocks = IoFile.ReadAllText(srtPath)
                .Replace("\r\n", "\n")
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
            foreach (var block in blocks)
            {
                var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 2) continue;
                var arrow = lines.FirstOrDefault(l => l.Contains("-->", StringComparison.Ordinal));
                if (arrow is null) continue;
                var parts = arrow.Split("-->", StringSplitOptions.TrimEntries);
                if (parts.Length != 2) continue;
                if (!TryParseSrtTs(parts[0], out var start) || !TryParseSrtTs(parts[1], out var end))
                    continue;
                var textIdx = Array.IndexOf(lines, arrow) + 1;
                var text = string.Join(" ", lines.Skip(Math.Max(1, textIdx))).Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;
                _captionCues.Add((start, end, text));
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"SRT load: {ex.Message}");
        }
    }

    private static bool TryParseSrtTs(string raw, out TimeSpan ts)
    {
        ts = TimeSpan.Zero;
        raw = raw.Trim().Replace(',', '.');
        return TimeSpan.TryParse(raw, out ts);
    }

    private int _dragClipIndex = -1;
    private Point _dragStart;
    private bool _dragMoved;

    private void ClipBlock_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: int idx } b || idx < 0)
            return;
        _dragClipIndex = idx;
        _dragStart = e.GetPosition(TimelineClipCanvas);
        _dragMoved = false;
        _videoEdit.Select(idx);
        RefreshDeleteVisibility();
        b.CaptureMouse();
        ScheduleTimelineLayout();
        e.Handled = true;
    }

    private void ClipBlock_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragClipIndex < 0 || e.LeftButton != MouseButtonState.Pressed)
            return;
        if (sender is not Border b)
            return;
        var pos = e.GetPosition(TimelineClipCanvas);
        var dx = pos.X - _dragStart.X;
        if (Math.Abs(dx) > 6)
            _dragMoved = true;
        if (_dragMoved)
            b.RenderTransform = new TranslateTransform(dx, 0);
    }

    private void ClipBlock_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndClipDrag(sender as Border, e.GetPosition(TimelineClipCanvas));
        e.Handled = true;
    }

    private void ClipBlock_LostMouseCapture(object sender, MouseEventArgs e) =>
        EndClipDrag(sender as Border, e.GetPosition(TimelineClipCanvas));

    /// <summary>
    /// Playbook drag+drop. Alt-tab used to skip MouseUp, so the ghost
    /// transform stayed and the session never MoveClip'd. Clear the drag
    /// index before ReleaseMouseCapture so LostMouseCapture is a no-op.
    /// </summary>
    private void EndClipDrag(Border? b, Point pos)
    {
        if (b is not null)
            b.RenderTransform = null;
        if (_dragClipIndex < 0)
            return;

        if (_dragMoved && TimelineClipCanvas is not null)
        {
            var w = Math.Max(1, TimelineClipCanvas.ActualWidth);
            var packed = _videoEdit.BuildPackedTimeline(true);
            var total = _videoEdit.PackedDuration(true);
            var t = TimeSpan.FromSeconds(Math.Clamp(pos.X / w, 0, 1) * total.TotalSeconds);
            var target = 0;
            for (var i = 0; i < packed.Count; i++)
            {
                if (packed[i].IsBlack) continue;
                if (t >= packed[i].TimelineIn)
                    target = packed[i].ClipIndex;
            }

            if (_dragClipIndex != target)
                PushVideoUndo();
            if (_videoEdit.MoveClip(_dragClipIndex, target))
            {
                if (!string.IsNullOrWhiteSpace(_currentFilePath))
                    SyncTimelineFromSession();
                ConvertStatusText.Text = $"Moved clip → position {target + 1}. A1 audio rebuilt to stay locked.";
                if (!string.IsNullOrWhiteSpace(_currentFilePath))
                    _timeline.EnsureAudioLinkedToVideo();
                if (TimelineHintText is not null)
                    TimelineHintText.Text = _videoEdit.Describe();
            }
            else if (_dragClipIndex != target)
                DiscardLastVideoUndo();
        }

        _dragClipIndex = -1;
        _dragMoved = false;
        b?.ReleaseMouseCapture();
        RefreshDeleteVisibility();
        ScheduleTimelineLayout();
    }

    private void FadeInButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_videoEdit.HasSelection)
        {
            ConvertStatusText.Text = "Select a clip first, then Fade In.";
            return;
        }

        ApplyFadeToSelected(fadeIn: TimeSpan.FromSeconds(0.5), fadeOut: null);
    }

    private void FadeOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_videoEdit.HasSelection)
        {
            ConvertStatusText.Text = "Select a clip first, then Fade Out.";
            return;
        }

        ApplyFadeToSelected(fadeIn: null, fadeOut: TimeSpan.FromSeconds(0.5));
    }

    private void WipeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_videoEdit.HasSelection)
        {
            ConvertStatusText.Text = "Select a clip first, then Wipe.";
            return;
        }

        var left = _videoEdit.WipeJoinIndex();
        if (left is null)
        {
            ConvertStatusText.Text = "Wipe needs two clips — Split or + Clip, then click Wipe on either side of the join.";
            return;
        }

        // Playbook Ctrl+Z after Split/Delete. Fade/Wipe used to skip the
        // undo stack, so one extra Ctrl+Z popped + Clip and Export was 1.6s.
        PushVideoUndo();
        if (!_videoEdit.ApplyWipeAtJoin())
        {
            DiscardLastVideoUndo();
            ConvertStatusText.Text = "Wipe already on that join.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_currentFilePath))
            SyncTimelineFromSession();
        var clip = _timeline.VideoTrack?.Clips.ElementAtOrDefault(left.Value);
        var dur = clip?.FadeOut.TotalSeconds ?? 0.5;
        ConvertStatusText.Text =
            $"Wipe (xfade wipeleft {dur:0.#}s) on clip {left.Value + 1} → next. Either side of the join works. Burns on Export.";
        ScheduleTimelineLayout();
    }

    private void OverlayTextButton_Click(object sender, RoutedEventArgs e)
    {
        var before = _timeline.OverlayTrack?.Clips.Count ?? 0;
        AddTextOverlayInteractive();
        if ((_timeline.OverlayTrack?.Clips.Count ?? 0) > before)
        {
            ConvertStatusText.Text = "Text overlay on canvas — burns into Export (drawtext / SRT fallback).";
            ScheduleTimelineLayout();
        }
    }

    private void ApplyFadeToSelected(TimeSpan? fadeIn, TimeSpan? fadeOut)
    {
        if (!string.IsNullOrWhiteSpace(_currentFilePath))
            SyncTimelineFromSession();
        var clip = _timeline.VideoTrack?.Clips.ElementAtOrDefault(_videoEdit.SelectedIndex);
        if (clip is null)
        {
            ConvertStatusText.Text = "No timeline clip — Split once, then select.";
            return;
        }

        PushVideoUndo();
        if (!_videoEdit.ApplyFadeAt(_videoEdit.SelectedIndex, fadeIn, fadeOut))
        {
            DiscardLastVideoUndo();
            ConvertStatusText.Text = "Fade already set on that clip.";
            return;
        }
        if (fadeIn.HasValue) clip.FadeIn = fadeIn.Value;
        if (fadeOut.HasValue) clip.FadeOut = fadeOut.Value;
        ConvertStatusText.Text =
            $"Fade set on clip {_videoEdit.SelectedIndex + 1}: in={clip.FadeIn.TotalSeconds:0.#}s out={clip.FadeOut.TotalSeconds:0.#}s (applied on Export timeline).";
    }

    private void ApplyCaptionsFromBackground(string srtPath, int cueCount)
    {
        _timeline.SubtitleBurnPath = srtPath;
        LoadCaptionCuesFromSrt(srtPath);
        ScheduleTimelineLayout();
        ConvertStatusText.Text =
            $"Captions ready · {cueCount} cues · continues after Space · burns on Export";
    }

    private async void AutoCaptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        if (WhisperSubtitleService.FindWhisperCli() is null || WhisperSubtitleService.FindModel() is null)
        {
            ConvertStatusText.Text = "Captions need whisper.cpp — run scripts\\download-whisper.ps1";
            return;
        }

        if (BackgroundExportService.Instance.IsCaptionGenerating)
        {
            ConvertStatusText.Text = "Caption generation already running…";
            return;
        }

        var videoPath = _currentFilePath;
        _captionDetached = false;
        ConvertStatusText.Text = "Generating local captions (whisper.cpp)…";
        SetLoading(true, "Generating captions…", showProgress: true, percent: 8);

        var progress = new Progress<ExportProgressReport>(r =>
        {
            SetLoading(true, r.Message, showProgress: true, percent: r.Percent);
        });

        try
        {
            var result = await BackgroundExportService.Instance.RunCaptionAsync(
                videoPath,
                _ffmpeg,
                progress,
                CancellationToken.None);

            if (_captionDetached)
                return;

            if (!result.Success)
            {
                ConvertStatusText.Text = result.Error
                    ?? "Captions need whisper.cpp — run scripts\\download-whisper.ps1";
                return;
            }

            ApplyCaptionsFromBackground(result.SrtPath!, result.CueCount);
        }
        catch (Exception ex)
        {
            if (!_captionDetached)
            {
                AppLogger.Error(ex, "AutoCaptions");
                ConvertStatusText.Text = $"Captions failed: {ex.Message}";
            }
        }
        finally
        {
            if (!_captionDetached)
                SetLoading(false);
        }
    }

    private void RenderAudioTrackClips(Canvas? canvas, FrameworkElement? host, string trackName, double fallbackW, double max)
    {
        if (canvas is null)
            return;

        canvas.Children.Clear();
        var w = host?.ActualWidth > 8 ? host.ActualWidth : fallbackW;
        canvas.Width = w;
        if (w < 8 || max <= 0)
            return;

        var track = _timeline.Tracks.FirstOrDefault(t => t.Name == trackName);
        if (track is null || track.Clips.Count == 0)
        {
            // Mirror V1 packed layout onto A1 (source Kept ÷ packed max was desynced)
            if (trackName == "A1")
            {
                var packed = _videoEdit.BuildPackedTimeline(_videoEdit.FillGapsWithBlack);
                var totalPacked = _videoEdit.PackedDuration(_videoEdit.FillGapsWithBlack);
                var denom = totalPacked.TotalSeconds > 0 ? totalPacked.TotalSeconds : Math.Max(max, 0.001);
                foreach (var seg in packed.Where(s => !s.IsBlack))
                {
                    var x0 = seg.TimelineIn.TotalSeconds / denom * w;
                    var x1 = seg.TimelineOut.TotalSeconds / denom * w;
                    var bdr = new Border
                    {
                        Width = Math.Max(3, x1 - x0 - 1),
                        Height = 14,
                        CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush(Color.FromArgb(0x77, 0x80, 0x90, 0xA0))
                    };
                    Canvas.SetLeft(bdr, x0);
                    Canvas.SetTop(bdr, 2);
                    canvas.Children.Add(bdr);
                }
            }

            return;
        }

        // Clips store packed timeline times — scale against packed max (same as V1), not source duration
        var total = Math.Max(max, 0.001);
        foreach (var clip in track.Clips)
        {
            var x0 = clip.TimelineStart.TotalSeconds / total * w;
            var x1 = clip.TimelineEnd.TotalSeconds / total * w;
            var muted = clip.Muted || track.Muted;
            var bdr = new Border
            {
                Width = Math.Max(3, x1 - x0 - 1),
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = muted
                    ? new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x80, 0x60))
                    : new SolidColorBrush(Color.FromArgb(0x88, 0x70, 0xB0, 0xC0)),
                ToolTip = muted ? $"{trackName} muted" : trackName
            };
            Canvas.SetLeft(bdr, x0);
            Canvas.SetTop(bdr, 2);
            canvas.Children.Add(bdr);
        }
    }

    private void TimelineTrack_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TimelineTrackHost is null || VideoSlider.Maximum <= 0)
            return;
        if (PreviewVideo.Source is null)
            return;

        var x = e.GetPosition(TimelineTrackHost).X;
        var mode = HitTestTimeline(x);

        // Click on clip body (not handles) → select clip
        if (mode == TimelineDragMode.Seek)
        {
            var t01 = Math.Clamp(x / Math.Max(1, TimelineTrackHost.ActualWidth), 0, 1);
            var idx = _editMode && _videoEdit.Kept.Count > 0
                ? _videoEdit.IndexAtPackedNormalized(t01)
                : _videoEdit.IndexAtNormalized(t01);
                if (idx >= 0)
                {
                    _videoEdit.Select(idx);
                    // Keep Mark In/Out as-is so timeline click-to-seek stays reliable
                    RefreshDeleteVisibility();
                    ScheduleTimelineLayout();
                    if (TimelineHintText is not null)
                        TimelineHintText.Text = _videoEdit.Describe();
                    ConvertStatusText.Text = $"Selected clip {idx + 1}. Delete removes it; Split blades at playhead.";

                    if (_bladeMode)
                    {
                        ApplyTimelinePointer(x);
                        e.Handled = true;
                        return;
                    }
                }
            else
            {
                _videoEdit.ClearSelection();
                RefreshDeleteVisibility();
                ScheduleTimelineLayout();
            }
        }

        _timelineDrag = mode;
        TimelineTrackHost.CaptureMouse();

        if (mode == TimelineDragMode.Seek)
            BeginScrub();

        ApplyTimelinePointer(x);
        e.Handled = true;
    }

    private void AudioTrack_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || VideoSlider.Maximum <= 0)
            return;

        var trackName = fe.Tag as string ?? "A1";
        var track = _timeline.Tracks.FirstOrDefault(t => t.Name == trackName);
        if (track is null || !_videoEdit.HasSelection || string.IsNullOrWhiteSpace(_currentFilePath))
        {
            ConvertStatusText.Text = "Select a V1 clip first, then click A1/A2 to move/mute audio there.";
            return;
        }

        // Move linked audio representation: mute on A2, unmute on A1
        if (trackName == "A2")
        {
            foreach (var c in track.Clips)
                c.Muted = true;
            track.Muted = false;
            // Ensure a clip exists on A2 mirroring selection
            if (track.Clips.Count == 0 && _videoEdit.HasSelection)
            {
                var kept = _videoEdit.Clips[_videoEdit.SelectedIndex];
                var src = kept.SourcePath;
                if (string.IsNullOrWhiteSpace(src))
                    src = _currentFilePath;
                // KeptClip.Start is source in (0 on + Clip). Same trap as
                // Leave-black title card -- A2 must sit on packed time.
                track.Clips.Add(new TimelineClip
                {
                    SourcePath = src,
                    SourceIn = kept.Start,
                    SourceOut = kept.End,
                    TimelineStart = _videoEdit.PackedStartOf(_videoEdit.SelectedIndex),
                    Muted = false
                });
            }

            // Mute A1 for that range visually
            var a1 = _timeline.Tracks.FirstOrDefault(t => t.Name == "A1");
            if (a1 is not null)
                foreach (var c in a1.Clips)
                    c.Muted = true;

            ConvertStatusText.Text =
                "A2 lane is preview-only. Export still uses V1/A1 (no amix).";
        }
        else
        {
            track.Muted = false;
            foreach (var c in track.Clips)
                c.Muted = false;
            var a2 = _timeline.Tracks.FirstOrDefault(t => t.Name == "A2");
            if (a2 is not null)
            {
                a2.Clips.Clear();
                a2.Muted = false;
            }

            ConvertStatusText.Text = "Audio on A1 (primary).";
        }

        ScheduleTimelineLayout();
        e.Handled = true;
    }

    private void TimelineTrack_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_videoEdit.HasSelection)
        {
            ConvertStatusText.Text = "Right-click: select a clip first for effects.";
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(MakeEffectItem("Fade in (0.5s)", () => ApplyFadeToSelected(fadeIn: true)));
        menu.Items.Add(MakeEffectItem("Fade out (0.5s)", () => ApplyFadeToSelected(fadeIn: false)));
        menu.Items.Add(MakeEffectItem("Speed 2×", () => ApplySpeedToSelected(2)));
        menu.Items.Add(MakeEffectItem("Speed 0.5×", () => ApplySpeedToSelected(0.5)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeEffectItem("Add black screen (1s)", () =>
        {
            PushVideoUndo();
            _timeline.AddOverlay(OverlayKind.Black, PackedPlayhead(), TimeSpan.FromSeconds(1));
        }));
        menu.Items.Add(MakeEffectItem("Add white screen (1s)", () =>
        {
            PushVideoUndo();
            _timeline.AddOverlay(OverlayKind.White, PackedPlayhead(), TimeSpan.FromSeconds(1));
        }));
        menu.Items.Add(MakeEffectItem("Add text…", AddTextOverlayInteractive));
        menu.IsOpen = true;
        e.Handled = true;
    }

    private MenuItem MakeEffectItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) =>
        {
            action();
            ConvertStatusText.Text = $"{header} — applied (export with Export timeline).";
            ScheduleTimelineLayout();
        };
        return item;
    }

    private void ApplyFadeToSelected(bool fadeIn)
    {
        // Same write path as the ribbon. Timeline-only used to vanish on Export
        // (SyncTimelineFromSession copies session fades onto _timeline).
        if (fadeIn)
            ApplyFadeToSelected(fadeIn: TimeSpan.FromMilliseconds(500), fadeOut: null);
        else
            ApplyFadeToSelected(fadeIn: null, fadeOut: TimeSpan.FromMilliseconds(500));
    }

    private void ApplySpeedToSelected(double speed)
    {
        PushVideoUndo();
        if (!_videoEdit.ApplySpeedAt(_videoEdit.SelectedIndex, speed))
        {
            DiscardLastVideoUndo();
            ConvertStatusText.Text = "Speed already set on that clip.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_currentFilePath))
            SyncTimelineFromSession();
        // Packed UI used source duration, so Speed 2x left the clip full
        // width. Export was ~2.3s with no visible cue to Ctrl+Z.
        SyncPackedSlider();
        ScheduleTimelineLayout();
    }

    private void AddTextOverlayInteractive()
    {
        var text = PromptDialog.Ask(this, "Timeline text", "Text to show (appear = clip In→Out):", "Title");
        if (string.IsNullOrWhiteSpace(text))
            return;
        PushVideoUndo();
        var start = PackedPlayhead();
        _timeline.AddOverlay(OverlayKind.Text, start, TimeSpan.FromSeconds(3), text);
    }

    private void TimelineTrack_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_timelineDrag == TimelineDragMode.None || TimelineTrackHost is null)
            return;

        ApplyTimelinePointer(e.GetPosition(TimelineTrackHost).X);
    }

    private void TimelineTrack_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        EndTimelineDrag();

    private void TimelineTrack_LostMouseCapture(object sender, MouseEventArgs e) =>
        EndTimelineDrag();

    private void EndTimelineDrag()
    {
        if (_timelineDrag == TimelineDragMode.None)
            return;

        var wasSeek = _timelineDrag == TimelineDragMode.Seek;
        _timelineDrag = TimelineDragMode.None;
        TimelineTrackHost?.ReleaseMouseCapture();
        if (wasSeek)
            EndScrub(resume: true);
        else
            UpdateMarkLabel();
    }

    private TimelineDragMode HitTestTimeline(double x)
    {
        if (TimelineTrackHost is null || VideoSlider.Maximum <= 0)
            return TimelineDragMode.Seek;

        var w = TimelineTrackHost.ActualWidth;
        var max = TimelineMaxSeconds();
        var inSec = Math.Clamp((_markIn ?? TimeSpan.Zero).TotalSeconds, 0, max);
        var outSec = Math.Clamp((_markOut ?? TimeSpan.FromSeconds(max)).TotalSeconds, 0, max);
        var xIn = inSec / max * w;
        var xOut = outSec / max * w;
        // Small hit only — large zones stole click-to-seek (especially after select snapped marks)
        const double hit = 6;

        if (Math.Abs(x - xIn) <= hit)
            return TimelineDragMode.MarkIn;
        if (Math.Abs(x - xOut) <= hit)
            return TimelineDragMode.MarkOut;
        return TimelineDragMode.Seek;
    }

    private void ApplyTimelinePointer(double x)
    {
        if (TimelineTrackHost is null || VideoSlider.Maximum <= 0)
            return;

        var w = Math.Max(1, TimelineTrackHost.ActualWidth);
        var max = TimelineMaxSeconds();
        var sec = Math.Clamp(x / w, 0, 1) * max;
        var t = TimeSpan.FromSeconds(sec);

        switch (_timelineDrag)
        {
            case TimelineDragMode.MarkIn:
                _markIn = t;
                if (_markOut.HasValue && _markOut < _markIn)
                    _markOut = _markIn;
                UpdateMarkLabel();
                break;
            case TimelineDragMode.MarkOut:
                _markOut = t;
                if (_markIn.HasValue && _markOut < _markIn)
                    _markIn = _markOut;
                UpdateMarkLabel();
                break;
            case TimelineDragMode.Seek:
                VideoSlider.Value = sec;
                // force: click-to-time must not lose to the 45ms scrub throttle
                SeekTimelineSeconds(sec, force: true);
                ScheduleTimelineLayout();
                break;
        }
    }

    private void UpdateVideoUi()
    {
        if (PreviewVideo.Source is null)
            return;

        var now = DateTime.UtcNow;
        var pos = GetPlayhead();

        // Hold UI on the seek target until MediaElement catches up (prevents snap-back)
        if (_pendingSeekSourceSeconds is double target && now < _suppressPositionSyncUntilUtc)
        {
            if (Math.Abs(pos.TotalSeconds - target) > SeekToleranceSec)
            {
                var displayMax = TimelineMaxSeconds();
                var displayPos = _editMode && _content?.Kind == PreviewKind.Video && _videoEdit.Kept.Count > 0
                    ? (_videoEdit.HasMultipleSources
                        ? VideoSlider.Value
                        : _videoEdit.SourceToPacked(TimeSpan.FromSeconds(target)).TotalSeconds)
                    : target;
                VideoTimeText.Text =
                    $"{FormatTs(TimeSpan.FromSeconds(displayPos))} / {FormatTs(TimeSpan.FromSeconds(displayMax))}";
                if (_editMode)
                    ScheduleTimelineLayout();
                return;
            }

            _pendingSeekSourceSeconds = null;
        }
        else if (_pendingSeekSourceSeconds is not null && now >= _suppressPositionSyncUntilUtc)
        {
            _pendingSeekSourceSeconds = null;
        }

        var dur = PreviewVideo.NaturalDuration.HasTimeSpan
            ? PreviewVideo.NaturalDuration.TimeSpan
            : TimeSpan.Zero;

        if (_editMode && _content?.Kind == PreviewKind.Video && _videoEdit.Kept.Count > 0)
        {
            var packedMax = _videoEdit.PackedDuration(_videoEdit.FillGapsWithBlack).TotalSeconds;
            if (packedMax > 0)
                VideoSlider.Maximum = packedMax;
            if (!_sliderDragging && _timelineDrag == TimelineDragMode.None &&
                !_videoEdit.HasMultipleSources)
            {
                _suppressSliderValueChanged = true;
                try { VideoSlider.Value = _videoEdit.SourceToPacked(pos).TotalSeconds; }
                finally { _suppressSliderValueChanged = false; }
            }
        }
        else if (!_sliderDragging && _timelineDrag == TimelineDragMode.None && dur.TotalSeconds > 0)
        {
            VideoSlider.Maximum = dur.TotalSeconds;
            _suppressSliderValueChanged = true;
            try { VideoSlider.Value = pos.TotalSeconds; }
            finally { _suppressSliderValueChanged = false; }
        }

        var displayMax2 = TimelineMaxSeconds();
        var displayPos2 = _editMode && _content?.Kind == PreviewKind.Video && _videoEdit.Kept.Count > 0
            ? (_videoEdit.HasMultipleSources
                ? VideoSlider.Value
                : _videoEdit.SourceToPacked(pos).TotalSeconds)
            : pos.TotalSeconds;
        VideoTimeText.Text = $"{FormatTs(TimeSpan.FromSeconds(displayPos2))} / {FormatTs(TimeSpan.FromSeconds(displayMax2))}";
        if (_editMode)
            ScheduleTimelineLayout();
    }

    private async void AddVideoButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        var dlg = new OpenFileDialog
        {
            Title = "Add clip onto V1",
            Filter = "Video|*.mp4;*.webm;*.mkv;*.mov;*.avi;*.m4v;*.wmv|All|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) != true || dlg.FileNames.Length == 0)
            return;

        ConvertStatusText.Text = "Reading clip duration…";
        var added = 0;
        var lastIdx = -1;
        string? lastName = null;
        PushVideoUndo();
        foreach (var file in dlg.FileNames)
        {
            var dur = await _ffmpeg.ProbeDurationAsync(file);
            if (dur is null || dur <= TimeSpan.Zero)
                continue;
            var idx = _videoEdit.AppendClip(file, dur.Value);
            if (idx < 0)
                continue;
            added++;
            lastIdx = idx;
            lastName = IoPath.GetFileName(file);
        }

        if (added == 0)
        {
            DiscardLastVideoUndo();
            ConvertStatusText.Text =
                "Could not read those videos’ duration (need FFmpeg). Run scripts/download-ffmpeg.ps1.";
            return;
        }

        SyncTimelineFromSession();
        // MediaOpened set In/Out and slider max to clip-1 duration.
        // After + Clip that leftover max is still 1.6s, so a clip-2 seek
        // clamps Value to the join and Split fails as an edge. Marks
        // staying at 0..1.6s made Export range SlicePacked only clip 1.
        SyncPackedSlider(resetMarks: true);
        RefreshDeleteVisibility();
        ScheduleTimelineLayout();
        if (TimelineHintText is not null)
            TimelineHintText.Text = _videoEdit.Describe();
        ConvertStatusText.Text = added == 1
            ? $"Added clip {lastIdx + 1} on V1 ({lastName}) -- packed after existing clips. Order... to arrange. Fade/Wipe still join on Export."
            : $"Added {added} clips on V1 in dialog order (last: {lastName}). Order... to arrange. Fade/Wipe still join on Export.";
    }

    private void OrderClipsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_videoEdit.Clips.Count == 0)
        {
            ConvertStatusText.Text = "No clips on V1 to order.";
            return;
        }

        var ordered = VideoOrderWindow.Show(this, _videoEdit.Clips);
        if (ordered is null)
            return;

        PushVideoUndo();
        if (!_videoEdit.ApplyOrder(ordered))
        {
            DiscardLastVideoUndo();
            ConvertStatusText.Text = "Could not apply that clip order.";
            return;
        }

        SyncTimelineFromSession();
        RefreshDeleteVisibility();
        ScheduleTimelineLayout();
        if (TimelineHintText is not null)
            TimelineHintText.Text = _videoEdit.Describe();
        ConvertStatusText.Text = "V1 clip order applied -- top of the list plays first. Export uses this packed order.";
    }

    private async void RemoveBgButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrWhiteSpace(_currentFilePath) || _content?.Kind != PreviewKind.Image)
            return;

        if (!_bgRemoval.IsScriptPresent)
        {
            ConvertStatusText.Text = "BG removal script missing (tools/remove_bg.py).";
            return;
        }

        if (!await _bgRemoval.IsRembgAvailableAsync())
        {
            ConvertStatusText.Text = "Installing rembg (one-time)…";
            StatusText.Text = "Installing rembg…";
            var (ok, msg) = await _bgRemoval.EnsureInstalledAsync();
            if (!ok)
            {
                ConvertStatusText.Text =
                    $"rembg install failed: {msg}. Need .venv + pip. See optional/README.md " +
                    "(or publish.ps1 -IncludeVenv).";
                StatusText.Text = "rembg install failed";
                return;
            }

            ConvertStatusText.Text = msg;
        }

        var defaultOut = BackgroundRemovalService.SiblingNobgPng(_currentFilePath);
        var previewOut = IoPath.Combine(
            IoPath.GetTempPath(),
            $"netie_nobg_preview_{Guid.NewGuid():N}.png");

        _busy = true;
        SetLoading(true, "Removing background…");
        ConvertStatusText.Text = "Removing background (rembg) — preview before save…";
        try
        {
            var result = await _bgRemoval.RemoveAsync(_currentFilePath, previewOut);
            if (!result.Success || string.IsNullOrWhiteSpace(result.OutputPath))
            {
                ConvertStatusText.Text = result.Error ?? "Background removal failed.";
                return;
            }

            // Photos-style: show result first, then ask to keep
            await LoadFileAsync(result.OutputPath);
            if (ImageAlphaBoard is not null)
                ImageAlphaBoard.Visibility = Visibility.Visible;

            var keep = GlassChoiceDialog.Show(this,
                "Background removed",
                "Preview on checkerboard (transparent ≠ black). Save, copy, or discard.",
                [
                    ("keep", "Keep & save…", true),
                    ("copy", "Copy", false),
                    ("discard", "Discard", false)
                ]);

            if (keep == "copy")
            {
                RememberNobgTemp(result.OutputPath);
                if (TryCopyImageFile(result.OutputPath, out var copyErr))
                    ConvertStatusText.Text = "Copied transparent PNG — paste or drag the picture out.";
                else
                    ConvertStatusText.Text = $"Copy failed: {copyErr}";
                return;
            }

            if (keep != "keep")
            {
                await LoadFileAsync(_currentFilePath!);
                ConvertStatusText.Text = "Background removal discarded.";
                DeleteNobgTemp(result.OutputPath);
                return;
            }

            var dir = IoPath.GetDirectoryName(_currentFilePath) ?? "";
            var dlg = new SaveFileDialog
            {
                Title = "Save PNG with transparent background",
                Filter = "PNG image|*.png|All files|*.*",
                DefaultExt = ".png",
                AddExtension = true,
                FileName = IoPath.GetFileName(defaultOut),
                InitialDirectory = dir,
                OverwritePrompt = true
            };
            if (dlg.ShowDialog(this) != true)
            {
                await LoadFileAsync(_currentFilePath!);
                DeleteNobgTemp(result.OutputPath);
                return;
            }

            IoFile.Copy(result.OutputPath, dlg.FileName, overwrite: true);
            DeleteNobgTemp(result.OutputPath);
            ConvertStatusText.Text = $"Saved {IoPath.GetFileName(dlg.FileName)}";
            await LoadFileAsync(dlg.FileName);
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"BG removal failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            SetLoading(false);
        }
    }

    private void CopyImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRequireFile(out var path))
            return;

        if (TryCopyImageFile(path, out var err))
            ConvertStatusText.Text = $"Copied {IoPath.GetFileName(path)} — paste or drag the picture out.";
        else
            ConvertStatusText.Text = $"Copy failed: {err}";
    }

    private async void SendToComfyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_content?.Kind != PreviewKind.Image || !TryRequireFile(out var path))
            return;

        try
        {
            var client = new ComfyUiClient();
            ConvertStatusText.Text = "Checking local ComfyUI...";
            var up = await client.IsAvailableAsync();
            if (!up)
            {
                var msg = ComfyUiClient.OfflineMessage(client.BaseUrl);
                ConvertStatusText.Text = msg;
                if (StatusText is not null)
                    StatusText.Text = "ComfyUI offline";
                try
                {
                    ToastHud.Show("ComfyUI is not running -- rembg / Copy still work");
                }
                catch
                {
                    // Toast must never crash Space.
                }
                return;
            }

            ConvertStatusText.Text = "Sending to ComfyUI...";
            var uploaded = await client.UploadImageAsync(path);
            var fileName = uploaded ?? IoPath.GetFileName(path);
            var workflow = ComfyUiClient.BuildLoadImageWorkflow(fileName);
            var result = await client.QueuePromptAsync(workflow);
            if (result.Success)
            {
                var saved = await client.DownloadFirstOutputAsync(path, result.OutputJson);
                if (!string.IsNullOrWhiteSpace(saved) && IoFile.Exists(saved))
                {
                    ConvertStatusText.Text =
                        $"ComfyUI wrote {IoPath.GetFileName(saved)} -- Copy / drag-out still work.";
                    await LoadFileAsync(saved);
                    return;
                }
            }

            // Sidecar only when upload / queue / download fails (playbook).
            string? sidecar = null;
            try { sidecar = ComfyUiClient.WriteInputSidecar(path); }
            catch (Exception ex)
            {
                ConvertStatusText.Text =
                    $"{result.Message} Could not write Comfy sidecar: {ex.Message}";
                return;
            }

            ConvertStatusText.Text =
                $"Wrote {IoPath.GetFileName(sidecar)} -- run that workflow in ComfyUI. rembg / Copy still work.";
            try { ToastHud.Show("Comfy sidecar written -- run the workflow locally"); }
            catch { /* toast is optional */ }
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"ComfyUI: {ex.Message}. rembg / Copy still work.";
        }
    }

    private void WindowsAiButton_Click(object sender, RoutedEventArgs e)
    {
        if (_content?.Kind != PreviewKind.Image || !TryRequireFile(out var path))
            return;

        try
        {
            if (!WindowsAiClient.IsAvailable())
            {
                // Sidecar only when WinML is down (matches Comfy: no leftover on success).
                string? sidecar = null;
                try { sidecar = WindowsAiClient.WriteInputSidecar(path); }
                catch (Exception ex)
                {
                    ConvertStatusText.Text = $"Could not write Windows AI sidecar: {ex.Message}";
                    return;
                }

                var msg = WindowsAiClient.OfflineMessage();
                ConvertStatusText.Text = string.IsNullOrWhiteSpace(sidecar)
                    ? msg
                    : $"{msg} Wrote {IoPath.GetFileName(sidecar)}.";
                if (StatusText is not null)
                    StatusText.Text = "Windows AI offline";
                try { ToastHud.Show("Windows AI is not available -- rembg / Copy still work"); }
                catch { /* toast must never crash Space */ }
                return;
            }

            ConvertStatusText.Text =
                "Windows AI runtime present. Live enhance is still human. rembg / Copy still work.";
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Windows AI: {ex.Message}. rembg / Copy still work.";
        }
    }

    private void PreviewImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _imageDragArmed = _content?.Kind == PreviewKind.Image && !string.IsNullOrWhiteSpace(_currentFilePath);
        _imageDragStart = e.GetPosition(PreviewImage);
    }

    private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_imageDragArmed || e.LeftButton != MouseButtonState.Pressed)
            return;
        if (_content?.Kind != PreviewKind.Image || string.IsNullOrWhiteSpace(_currentFilePath))
            return;
        if (!IoFile.Exists(_currentFilePath))
            return;

        var delta = e.GetPosition(PreviewImage) - _imageDragStart;
        if (Math.Abs(delta.X) < 10 && Math.Abs(delta.Y) < 10)
            return;

        _imageDragArmed = false;
        var data = new DataObject(DataFormats.FileDrop, new[] { _currentFilePath });
        DragDrop.DoDragDrop(PreviewImage, data, DragDropEffects.Copy);
    }

    private void PreviewImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        _imageDragArmed = false;

    private void PreviewImage_MouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            _imageDragArmed = false;
    }

    private bool TryCopyImageFile(string path, out string error)
    {
        error = "";
        try
        {
            if (!IoFile.Exists(path))
            {
                error = "file missing";
                return false;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();

            var data = new DataObject();
            data.SetImage(bmp);
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { path });

            using (var ms = new MemoryStream())
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                enc.Save(ms);
                data.SetData("PNG", ms.ToArray());
            }

            Clipboard.SetDataObject(data, true);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void RememberNobgTemp(string path)
    {
        if (!string.Equals(_nobgTempPath, path, StringComparison.OrdinalIgnoreCase))
            DeleteNobgTemp(_nobgTempPath);
        _nobgTempPath = path;
    }

    private void DeleteNobgTemp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (IoFile.Exists(path))
                IoFile.Delete(path);
        }
        catch { /* temp file */ }

        if (string.Equals(_nobgTempPath, path, StringComparison.OrdinalIgnoreCase))
            _nobgTempPath = null;
    }

    private void FileNameText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;
        if (!IoFile.Exists(_currentFilePath) && !IoDirectory.Exists(_currentFilePath))
            return;
        if (FileNameEditBox is null)
            return;

        var isDir = IoDirectory.Exists(_currentFilePath);
        var stem = isDir
            ? IoPath.GetFileName(_currentFilePath.TrimEnd('\\', '/'))
            : IoPath.GetFileNameWithoutExtension(_currentFilePath);
        FileNameText.Visibility = Visibility.Collapsed;
        FileNameEditBox.Visibility = Visibility.Visible;
        FileNameEditBox.Text = stem;
        FileNameEditBox.SelectAll();
        FileNameEditBox.Focus();
        e.Handled = true;
    }

    private void FileNameEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitInlineRename();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelInlineRename();
        }
    }

    private void FileNameEditBox_LostFocus(object sender, RoutedEventArgs e) => CommitInlineRename();

    private void CancelInlineRename()
    {
        if (FileNameEditBox is null) return;
        FileNameEditBox.Visibility = Visibility.Collapsed;
        FileNameText.Visibility = Visibility.Visible;
    }

    private void CommitInlineRename()
    {
        if (FileNameEditBox is null || FileNameEditBox.Visibility != Visibility.Visible)
            return;
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            CancelInlineRename();
            return;
        }

        var isDir = IoDirectory.Exists(_currentFilePath);
        var dir = isDir
            ? IoPath.GetDirectoryName(_currentFilePath.TrimEnd('\\', '/')) ?? ""
            : IoPath.GetDirectoryName(_currentFilePath) ?? "";
        var ext = isDir ? "" : IoPath.GetExtension(_currentFilePath);
        var stem = isDir
            ? IoPath.GetFileName(_currentFilePath.TrimEnd('\\', '/'))
            : IoPath.GetFileNameWithoutExtension(_currentFilePath);
        var next = FileNameEditBox.Text?.Trim() ?? "";
        CancelInlineRename();
        if (string.IsNullOrWhiteSpace(next) || string.Equals(next, stem, StringComparison.Ordinal))
            return;

        foreach (var c in IoPath.GetInvalidFileNameChars())
            next = next.Replace(c, '_');
        next = next.Trim();
        if (string.IsNullOrWhiteSpace(next))
            return;

        var dest = IoPath.Combine(dir, next + ext);
        if (IoFile.Exists(dest) || IoDirectory.Exists(dest))
        {
            ConvertStatusText.Text = "A file or folder with that name already exists.";
            return;
        }

        try
        {
            if (isDir)
                IoDirectory.Move(_currentFilePath, dest);
            else
                IoFile.Move(_currentFilePath, dest);
            _currentFilePath = dest;
            FileNameText.Text = IoPath.GetFileName(dest.TrimEnd('\\', '/'));
            ConvertStatusText.Text = $"Renamed → {FileNameText.Text}";
            _ = LoadFileAsync(dest);
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Rename failed: {ex.Message}";
        }
    }

    private void SummaryToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _summaryBarEnabled = !_summaryBarEnabled;
        UserPrefs.SummaryBarEnabled = _summaryBarEnabled;
        if (SummaryToggleButton is not null)
            SummaryToggleButton.Content = _summaryBarEnabled ? "Summary" : "Summary off";
        ApplySummaryBarVisibility();

        if (_summaryBarEnabled && _content is not null && _content.Kind != PreviewKind.Video)
        {
            SummaryText.Text = "Generating summary...";
            _ = LoadSummaryAsync(_content, _cts?.Token ?? CancellationToken.None);
        }
        else if (!_summaryBarEnabled)
        {
            SummaryText.Text = "Summary hidden — AI calls paused.";
        }
    }

    // ── Summary chrome: collapse, dock-left, floating handle ───────────────────

    /// <summary>True when the summary should occupy chrome at all (editor ribbons win the row).</summary>
    private bool SummaryChromeAllowed =>
        _summaryBarEnabled && !(_editMode && _content?.Kind is PreviewKind.Video or PreviewKind.Pdf);

    private void ApplySummaryBarVisibility() => ApplySummaryLayout(animate: false);

    /// <summary>Single place that decides where the summary lives and whether it is showing.</summary>
    private void ApplySummaryLayout(bool animate)
    {
        if (PreviewSummaryHost is null || SummaryRail is null)
            return;

        var allowed = SummaryChromeAllowed;
        var showTop = allowed && !_summaryCollapsed && !_summaryDocked;
        var showRail = allowed && !_summaryCollapsed && _summaryDocked;

        // Top strip
        if (showTop)
        {
            if (PreviewSummaryHost.Visibility != Visibility.Visible)
            {
                if (animate)
                    AnimationHelper.ExpandHeight(PreviewSummaryHost, _summaryExpandedHeight, _animationMs);
                else
                {
                    PreviewSummaryHost.Visibility = Visibility.Visible;
                    PreviewSummaryHost.Height = double.NaN;
                    PreviewSummaryHost.Opacity = 1;
                }
            }
        }
        else if (PreviewSummaryHost.Visibility == Visibility.Visible)
        {
            if (animate)
            {
                _ = CollapseSummaryStripAsync();
            }
            else
            {
                PreviewSummaryHost.Visibility = Visibility.Collapsed;
                PreviewSummaryHost.Height = 0;
            }
        }

        // Left rail
        var railTarget = showRail ? SummaryRailWidth : 0;
        if (showRail)
            SummaryRail.Visibility = Visibility.Visible;

        if (animate)
        {
            AnimationHelper.AnimateColumn(SummaryRailColumn, SummaryRailColumn.Width.Value, railTarget,
                _animationMs, completed: () =>
                {
                    if (!showRail) SummaryRail.Visibility = Visibility.Collapsed;
                });
            if (showRail)
                AnimationHelper.AnimateOpenFadeOnly(SummaryRail, _animationMs);
        }
        else
        {
            SummaryRailColumn.Width = new GridLength(railTarget);
            SummaryRail.Visibility = showRail ? Visibility.Visible : Visibility.Collapsed;
            SummaryRail.Opacity = 1;
        }

        UpdateSummaryHandles(allowed);
    }

    private async Task CollapseSummaryStripAsync()
    {
        var was = await AnimationHelper.CollapseHeightAsync(PreviewSummaryHost, _animationMs - 40);
        if (was > 1)
            _summaryExpandedHeight = was;
    }

    private void UpdateSummaryHandles(bool allowed)
    {
        if (SummaryFloatChip is null) return;

        // The handle only makes sense while the summary is a thing this file can have.
        SummaryFloatChip.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
        SummaryFloatChip.Content = _summaryCollapsed
            ? "⌄ Summary"
            : _summaryDocked ? "⌃ Summary · left" : "⌃ Summary";
        SummaryFloatChip.ToolTip = _summaryCollapsed
            ? "Show the summary panel"
            : "Hide the summary panel";

        if (SummaryDockButton is not null)
        {
            SummaryDockButton.Content = _summaryDocked ? "⇥" : "⇤";
            SummaryDockButton.ToolTip = _summaryDocked
                ? "Put the summary back on top"
                : "Pull the summary to a rail on the left";
        }
    }

    private void SummaryCollapseButton_Click(object sender, RoutedEventArgs e) => ToggleSummaryCollapsed();

    private void SummaryFloatChip_Click(object sender, RoutedEventArgs e) => ToggleSummaryCollapsed();

    private void ToggleSummaryCollapsed()
    {
        if (!SummaryChromeAllowed)
        {
            // Collapsed because the summary is switched off entirely — turn it back on instead.
            SummaryToggleButton_Click(SummaryToggleButton, new RoutedEventArgs());
            return;
        }

        if (!_summaryCollapsed && PreviewSummaryHost.Visibility == Visibility.Visible)
            _summaryExpandedHeight = PreviewSummaryHost.ActualHeight;

        _summaryCollapsed = !_summaryCollapsed;
        UserPrefs.SummaryCollapsed = _summaryCollapsed;
        ApplySummaryLayout(animate: true);
    }

    private void SummaryDockButton_Click(object sender, RoutedEventArgs e) => SetSummaryDocked(!_summaryDocked);

    private void SummaryUndockButton_Click(object sender, RoutedEventArgs e) => SetSummaryDocked(false);

    private void SetSummaryDocked(bool docked)
    {
        if (_summaryDocked == docked)
            return;

        if (!_summaryDocked && PreviewSummaryHost.Visibility == Visibility.Visible)
            _summaryExpandedHeight = PreviewSummaryHost.ActualHeight;

        _summaryDocked = docked;
        _summaryCollapsed = false; // docking is a move, never a hide
        UserPrefs.SummaryDocked = docked;
        UserPrefs.SummaryCollapsed = false;
        ApplySummaryLayout(animate: true);

        ConvertStatusText.Text = docked
            ? "Summary docked left — ⇥ puts it back on top."
            : "Summary back on top.";
    }

    private async void TextEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsHtmlWordEditable())
        {
            await ToggleHtmlEditModeAsync();
            return;
        }

        if (_content?.Kind is not (PreviewKind.Text or PreviewKind.Markdown))
            return;

        _textEditMode = !_textEditMode;
        UpdateActionButtons(_content.Kind);
        await EnsureWebViewAsync();
        if (PreviewWebView.CoreWebView2 is not { } core)
            return;

        if (_textEditMode)
        {
            // Prefer source <pre> for text/code; for markdown use a source pane from RawText
            var isMd = _content.Kind == PreviewKind.Markdown;
            if (isMd && !string.IsNullOrEmpty(_content.RawText))
            {
                var escaped = System.Text.Json.JsonSerializer.Serialize(_content.RawText);
                var script =
                    "(function(){" +
                    "var body=document.body;body.innerHTML='';" +
                    "var pre=document.createElement('pre');pre.id='netie-md-source';" +
                    "pre.style.cssText='margin:0;padding:18px 16px 40px;white-space:pre-wrap;color:#f2f4f8;font:13px/1.55 Cascadia Code,Consolas,monospace;outline:1px solid rgba(94,196,184,0.45);border-radius:8px;min-height:60vh';" +
                    "pre.setAttribute('contenteditable','true');pre.textContent=" + escaped + ";" +
                    "body.appendChild(pre);" +
                    "function notify(typing){try{chrome.webview.postMessage(JSON.stringify({type:'netieTyping',typing:!!typing}));}catch(e){}}" +
                    "function save(){try{chrome.webview.postMessage(JSON.stringify({type:'textEdit',text:pre.innerText||''}));}catch(e){}}" +
                    "window.__netieSave=save;" +
                    "pre.addEventListener('focus',function(){notify(true);});" +
                    "pre.addEventListener('blur',function(){notify(false);save();});" +
                    "pre.addEventListener('input',function(){notify(true);clearTimeout(window.__netieSaveT);window.__netieSaveT=setTimeout(save,400);});" +
                    "pre.focus();})();";
                await core.ExecuteScriptAsync(script);
            }
            else
            {
                await core.ExecuteScriptAsync("""
                    (function(){
                      var pre=document.querySelector('pre code')||document.querySelector('pre')||document.body;
                      if(!pre) return;
                      pre.setAttribute('contenteditable','true');
                      pre.style.outline='1px solid rgba(94,196,184,0.45)';
                      pre.style.borderRadius='8px';
                      function notify(typing){
                        try{chrome.webview.postMessage(JSON.stringify({type:'netieTyping',typing:!!typing}));}catch(e){}
                      }
                      function save(){
                        try{
                          var text=pre.innerText||pre.textContent||'';
                          chrome.webview.postMessage(JSON.stringify({type:'textEdit',text:text}));
                        }catch(e){}
                      }
                      window.__netieSave=save;
                      pre.addEventListener('focus',function(){notify(true);});
                      pre.addEventListener('blur',function(){notify(false);save();});
                      pre.addEventListener('input',function(){
                        notify(true);
                        clearTimeout(window.__netieSaveT);
                        window.__netieSaveT=setTimeout(save,400);
                      });
                      pre.focus();
                    })();
                    """);
            }
            ConvertStatusText.Text = "Editing — autosaves as you type.";
            _webViewTyping = true;
        }
        else
        {
            await core.ExecuteScriptAsync("""
                (function(){
                  try{ if(typeof window.__netieSave==='function') window.__netieSave(); }catch(e){}
                  var pre=document.querySelector('[contenteditable="true"]');
                  if(pre){ pre.removeAttribute('contenteditable'); pre.style.outline=''; }
                  try{chrome.webview.postMessage(JSON.stringify({type:'netieTyping',typing:false}));}catch(e){}
                })();
                """);
            _webViewTyping = false;
            ConvertStatusText.Text = "Edit closed.";
            if (_content is not null)
                await LoadFileAsync(_currentFilePath!);
        }
    }

    /// <summary>Save dialog prefilled with sibling default; cancel aborts the edit.</summary>
    private string? PromptVideoSavePath(string tag)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return null;

        var dir = IoPath.GetDirectoryName(_currentFilePath) ?? "";
        var srcExt = IoPath.GetExtension(_currentFilePath).ToLowerInvariant();
        var defaultExt = srcExt is ".mp4" or ".m4v" or ".webm" or ".mkv" or ".mov" ? srcExt : ".mp4";
        var name = $"{IoPath.GetFileNameWithoutExtension(_currentFilePath)}_{tag}_{DateTime.Now:HHmmss}{defaultExt}";
        var dlg = new SaveFileDialog
        {
            Title = "Save video as",
            Filter =
                "MP4 video|*.mp4|MPEG-4|*.m4v|WebM video|*.webm|Matroska|*.mkv|QuickTime|*.mov|All files|*.*",
            DefaultExt = defaultExt.TrimStart('.'),
            FilterIndex = defaultExt switch
            {
                ".m4v" => 2,
                ".webm" => 3,
                ".mkv" => 4,
                ".mov" => 5,
                _ => 1
            },
            AddExtension = true,
            FileName = name,
            InitialDirectory = dir,
            OverwritePrompt = true
        };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private void SyncTimelineFromSession()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;
        _videoEdit.ApplyTo(_timeline, _currentFilePath);
    }

    private string ClipBlockLabel(TimelineSegment seg, bool selected)
    {
        if (seg.IsBlack)
            return "Black";
        var name = "";
        if (_videoEdit.HasMultipleSources &&
            seg.ClipIndex >= 0 &&
            seg.ClipIndex < _videoEdit.Clips.Count)
        {
            var path = _videoEdit.Clips[seg.ClipIndex].SourcePath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var stem = IoPath.GetFileNameWithoutExtension(path);
                if (stem.Length > 14)
                    stem = stem[..12] + "…";
                name = " " + stem;
            }
        }

        return selected ? $"▸ {seg.ClipIndex + 1}{name}" : $"{seg.ClipIndex + 1}{name}";
    }

    /// <summary>
    /// Slider max and marks follow packed output duration (Speed shrinks it).
    /// </summary>
    private void SyncPackedSlider(bool resetMarks = false)
    {
        var packed = _videoEdit.PackedDuration(_videoEdit.FillGapsWithBlack);
        _suppressSliderValueChanged = true;
        try { VideoSlider.Maximum = Math.Max(0.001, packed.TotalSeconds); }
        finally { _suppressSliderValueChanged = false; }
        if (resetMarks)
        {
            _markIn = TimeSpan.Zero;
            _markOut = packed;
        }
        else if (_markOut is { } mo && mo > packed)
            _markOut = packed;
        UpdateMarkLabel();
        UpdateVideoUi();
    }

    private void PushVideoUndo() => _videoEdit.PushUndo(_timeline);

    private void DiscardLastVideoUndo() => _videoEdit.TryUndo(_timeline);

    private void UndoVideoEdit()
    {
        if (!_videoEdit.TryUndo(_timeline))
        {
            ConvertStatusText.Text = "Nothing to undo";
            return;
        }

        SyncTimelineFromSession();
        SyncPackedSlider();
        RefreshDeleteVisibility();
        ScheduleTimelineLayout();
        ConvertStatusText.Text = "Undid last timeline edit";
    }

    private async void PdfSaveStamps_Click(object sender, RoutedEventArgs e)
    {
        ConvertStatusText.Text = "Saving… pulling edits from the viewer";
        await RequestViewerFlushAsync();

        if (!HasPendingPdfEdits())
        {
            ConvertStatusText.Text = "Nothing to save — place a signature, type in Fill, or add a highlight first.";
            return;
        }

        // Full bake + reload so reopen shows exactly what you see (no ghost overlays)
        var ok = await FlushPendingPdfStampsAsync(keepOverlaysEditable: false);
        if (ok)
            ConvertStatusText.Text = $"Saved into {IoPath.GetFileName(_currentFilePath)} — reopen to confirm";
    }

    private void EnsurePdfSignFontsLoaded()
    {
        if (_pdfSignFontComboReady || PdfSignFontCombo is null) return;
        try
        {
            var fonts = PdfEditService.GetAvailableSignatureFonts();
            PdfSignFontCombo.ItemsSource = fonts;
            var pick = fonts.FirstOrDefault(f =>
                string.Equals(f, _preferredSignFont, StringComparison.OrdinalIgnoreCase)) ?? fonts.FirstOrDefault();
            if (pick is not null)
            {
                PdfSignFontCombo.SelectedItem = pick;
                _preferredSignFont = pick;
            }
            _pdfSignFontComboReady = true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Sign fonts: {ex.Message}");
        }
    }

    private void SetPdfToolMode(string mode)
    {
        // Toggle off if same mode pressed again
        if (string.Equals(_pdfToolMode, mode, StringComparison.OrdinalIgnoreCase))
            mode = "";
        _pdfToolMode = mode ?? "";

        EnsurePdfSignFontsLoaded();

        void show(Border? b, bool on)
        {
            if (b is not null)
                b.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        show(PdfOrganizePanel, _pdfToolMode == "organize");
        show(PdfSignPanel, _pdfToolMode == "sign");
        show(PdfFillPanel, _pdfToolMode == "fill");
        show(PdfCommentPanel, _pdfToolMode == "comment");

        void styleMode(System.Windows.Controls.Button? btn, string id)
        {
            if (btn is null) return;
            btn.Style = (Style)FindResource(_pdfToolMode == id ? "EditorChipPrimary" : "EditorChip");
        }
        styleMode(PdfModeOrganizeBtn, "organize");
        styleMode(PdfModeSignBtn, "sign");
        styleMode(PdfModeFillBtn, "fill");
        styleMode(PdfModeCommentBtn, "comment");

        if (PdfRibbonHint is not null)
        {
            PdfRibbonHint.Text = _pdfToolMode switch
            {
                "organize" => "Page tools below",
                "sign" => "New signature · then click the page to place",
                "fill" => "Click a cell · type · OCR seeds scans · Save",
                "comment" => "Drag to highlight",
                _ => "Pick a mode · tweaks appear below"
            };
        }

        var core = PreviewWebView.CoreWebView2;
        if (core is null) return;

        if (_pdfToolMode != "fill")
            core.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "exitFillMode" }));
        if (_pdfToolMode != "sign")
            core.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "exitStampMode" }));
        if (_pdfToolMode != "comment")
            core.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "setHighlightMode", on = false }));
    }

    private void PdfModeOrganize_Click(object sender, RoutedEventArgs e) => SetPdfToolMode("organize");

    private void PdfModeSign_Click(object sender, RoutedEventArgs e)
    {
        SetPdfToolMode("sign");
        ConvertStatusText.Text = "Sign — New signature, library, or stamp image · then click the page";
    }

    private void PdfModeFill_Click(object sender, RoutedEventArgs e)
    {
        SetPdfToolMode("fill");
        PdfFillForm_Click(sender, e);
    }

    private void PdfModeComment_Click(object sender, RoutedEventArgs e)
    {
        SetPdfToolMode("comment");
        PdfCommentHighlight_Click(sender, e);
    }

    private void PdfMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.ContextMenu is null)
            return;
        btn.ContextMenu.PlacementTarget = btn;
        btn.ContextMenu.IsOpen = true;
    }

    private void PdfExportAsMenu_Click(object sender, RoutedEventArgs e) =>
        ShareExportAs_Click(sender, e);

    private async void PdfWatermark_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRequirePdf(out var path))
            return;
        var text = PromptDialog.Ask(this, "Watermark", "Text to stamp across every page:", "DRAFT");
        if (string.IsNullOrWhiteSpace(text))
            return;
        var outPath = SiblingPdf(path, "watermarked");
        await RunPdfOpAsync("Watermark", outPath,
            ct => _conversion.PdfWatermarkAsync(path, outPath, text.Trim(), ct: ct));
    }

    private async void PdfProtect_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRequirePdf(out var path))
            return;
        var password = PromptDialog.Ask(this, "Protect PDF", "Password to open this PDF:", "");
        if (string.IsNullOrWhiteSpace(password))
            return;
        var outPath = SiblingPdf(path, "protected");
        await RunPdfOpAsync("Protect", outPath,
            ct => _conversion.PdfProtectAsync(path, outPath, password, ct: ct));
    }

    private async void PdfUnlock_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRequirePdf(out var path))
            return;
        var password = PromptDialog.Ask(this, "Unlock PDF", "Password for this PDF:", "");
        if (string.IsNullOrWhiteSpace(password))
            return;
        var outPath = SiblingPdf(path, "unlocked");
        await RunPdfOpAsync("Unlock", outPath,
            ct => _conversion.PdfUnlockAsync(path, outPath, password, ct: ct));
    }

    private async void PdfOcr_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRequirePdf(out var path))
            return;
        var outPath = SiblingPdf(path, "ocr");
        await RunPdfOpAsync("OCR", outPath,
            ct => _conversion.PdfOcrAsync(path, outPath, ct: ct));
    }

    private void PdfCertSign_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRequirePdf(out var path))
            return;

        var dlg = new OpenFileDialog
        {
            Title = "PKCS#12 certificate (PFX / P12)",
            Filter = "PKCS#12|*.pfx;*.p12|All files|*.*"
        };
        if (dlg.ShowDialog(this) != true)
            return;

        var password = PromptDialog.Ask(this, "Cert sign", "PFX password (empty if none):", "") ?? "";
        var backup = IoFile.ReadAllBytes(path);
        try
        {
            using var cert = PdfCmsSigner.LoadPfx(dlg.FileName, password);
            var rewritten = false;
            try { rewritten = _pdfEdit.RewriteClassicXref(path); }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Could not rewrite xref-stream/hybrid PDF to classic xref. " + ex.Message, ex);
            }
            if (PdfCmsSigner.NeedsClassicXrefRewrite(IoFile.ReadAllBytes(path)))
                throw new InvalidOperationException(
                    "Cert sign needs a classic xref PDF. Save from Space (PdfSharp) and retry.");
            PdfCmsSigner.SignFile(path, cert);
            ConvertStatusText.Text = rewritten
                ? "Cert sign written (rewrote xref-stream, CMS self-check passed). Open in Adobe Reader to check the Signature panel."
                : "Cert sign written (CMS self-check passed). Open in Adobe Reader to check the Signature panel.";
            _ = LoadFileAsync(path);
        }
        catch (Exception ex)
        {
            try { IoFile.WriteAllBytes(path, backup); } catch { /* keep backup attempt best-effort */ }
            ConvertStatusText.Text = $"Cert sign failed — original bytes restored. {ex.Message}";
        }
    }

    private bool TryRequirePdf(out string path)
    {
        path = _currentFilePath ?? "";
        if (string.IsNullOrWhiteSpace(path) || !IoFile.Exists(path))
        {
            ConvertStatusText.Text = "Open a PDF first.";
            return false;
        }
        if (!IoPath.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            ConvertStatusText.Text = "These tools work on PDF files.";
            return false;
        }
        return true;
    }

    private static string SiblingPdf(string sourcePath, string tag)
    {
        var dir = IoPath.GetDirectoryName(sourcePath) ?? ".";
        var stem = IoPath.GetFileNameWithoutExtension(sourcePath);
        var dest = IoPath.Combine(dir, $"{stem}_{tag}.pdf");
        var i = 1;
        while (IoFile.Exists(dest))
        {
            dest = IoPath.Combine(dir, $"{stem}_{tag}_{i}.pdf");
            i++;
        }
        return dest;
    }

    private async Task RunPdfOpAsync(string label, string outputPath, Func<CancellationToken, Task<ConversionResult>> work)
    {
        if (_busy)
            return;
        _busy = true;
        SetLoading(true, $"{label}…");
        ConvertStatusText.Text = $"{label}…";
        try
        {
            var result = await work(_cts?.Token ?? CancellationToken.None);
            if (result.Success && !string.IsNullOrWhiteSpace(result.OutputPath))
            {
                ConvertStatusText.Text =
                    $"{label} → {IoPath.GetFileName(result.OutputPath)} via {result.Tool} · {FormatSize(result.Bytes)}";
                var open = GlassChoiceDialog.Show(this, label,
                    $"Saved beside the original:\n{result.OutputPath}",
                    [
                        ("open", "Open it", true),
                        ("ok", "Done", false)
                    ]);
                if (open == "open")
                    await LoadFileAsync(result.OutputPath!);
            }
            else
            {
                ConvertStatusText.Text = string.IsNullOrWhiteSpace(result.Error)
                    ? $"{label} failed."
                    : $"{label}: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"{label} failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            SetLoading(false);
        }
    }

    private void PdfSignFontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PdfSignFontCombo?.SelectedItem is string font && !string.IsNullOrWhiteSpace(font))
            _preferredSignFont = font;
    }

    private void PdfSizeDown_Click(object sender, RoutedEventArgs e) => PostScaleSelected(0.88);
    private void PdfSizeUp_Click(object sender, RoutedEventArgs e) => PostScaleSelected(1.14);

    private void PostScaleSelected(double factor)
    {
        var core = PreviewWebView.CoreWebView2;
        core?.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "scaleSelected", factor }));
    }

    private void PdfExitFill_Click(object sender, RoutedEventArgs e)
    {
        SetPdfToolMode("");
        ConvertStatusText.Text = "Fill closed";
    }

    private void PdfTextBoxes_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath)) return;
        var core = PreviewWebView.CoreWebView2;
        if (core is null) return;
        core.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "gotoPage", page = 1 }));
        PostTextBoxesToViewer(_currentFilePath!, 1);
        ConvertStatusText.Text = "Text boxes — use viewer “Text boxes” for the page you’re on.";
    }

    /// <summary>Probe native text layer (not full OCR). Kept for diagnostics.</summary>
    private void PdfOcrProbe_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath)) return;
        try
        {
            var boxes = PdfEditService.GetTextBoxes(_currentFilePath!, 1, 800);
            if (boxes.Count > 0)
            {
                PostTextBoxesToViewer(_currentFilePath!, 1);
                ConvertStatusText.Text = $"Native text layer · {boxes.Count} boxes (page 1). Use More → Scan & OCR for searchable PDF.";
            }
            else
            {
                ConvertStatusText.Text = "No text layer — page looks scanned. Use More → Scan & OCR (needs Tesseract).";
            }
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"OCR probe failed: {ex.Message}";
        }
    }

    private async void PdfMerge_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        // Merge window owns Add files / folder / convert — open directly with current PDF
        var ordered = PdfMergeWindow.Show(this, _currentFilePath!);
        if (ordered is null || ordered.Count < 2)
            return;

        await RunPdfEditAsync("Merging…", () => _pdfEdit.MergeOrdered(ordered), openResult: true);
    }

    private async void PdfSplit_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        await RunPdfEditAsync("Splitting PDF...", () => _pdfEdit.SplitAllPages(_currentFilePath!), openResult: false,
            onSuccess: path =>
            {
                ConvertStatusText.Text = $"Split into folder · {IoPath.GetFileName(path)}";
                try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
                catch { /* ignore */ }
            });
    }

    private async void PdfExtract_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        int pageCount;
        try { pageCount = _pdfEdit.GetPageCount(_currentFilePath); }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"PDF read failed: {ex.Message}";
            return;
        }

        var spec = PromptDialog.Ask(this, "Extract pages",
            $"Pages to extract (1–{pageCount}). Examples: 1-3,5 or 2",
            pageCount <= 1 ? "1" : $"1-{pageCount}");
        if (spec is null)
            return;

        await RunPdfEditAsync("Extracting pages...", () => _pdfEdit.ExtractPages(_currentFilePath!, spec), openResult: true);
    }

    private async void PdfRotate90_Click(object sender, RoutedEventArgs e) =>
        await RotatePdfAsync(90);

    private async void PdfRotate180_Click(object sender, RoutedEventArgs e) =>
        await RotatePdfAsync(180);

    private async Task RotatePdfAsync(int degrees)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        int pageCount;
        try { pageCount = _pdfEdit.GetPageCount(_currentFilePath); }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"PDF read failed: {ex.Message}";
            return;
        }

        var spec = PromptDialog.Ask(this, $"Rotate {degrees}° CW",
            $"Pages to rotate (1–{pageCount}). Leave blank for all.",
            "");
        if (spec is null)
            return;

        await RunPdfEditAsync($"Rotating {degrees}°...",
            () => _pdfEdit.RotatePages(_currentFilePath!, degrees, string.IsNullOrWhiteSpace(spec) ? null : spec),
            openResult: true);
    }

    private async void PdfReverse_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        await RunPdfEditAsync("Reversing pages...", () => _pdfEdit.ReversePages(_currentFilePath!), openResult: true);
    }

    private async void PdfOrganize_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        var result = PdfOrganizeWindow.Show(this, _currentFilePath);
        if (string.IsNullOrWhiteSpace(result) || !IoFile.Exists(result))
            return;

        ConvertStatusText.Text = $"Saved · {IoPath.GetFileName(result)}";
        await LoadFileAsync(result);
    }

    private async void PdfSignType_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        EnsurePdfSignFontsLoaded();
        if (_pdfToolMode != "sign")
            SetPdfToolMode("sign");

        var dlg = SignatureDialog.Show(this);
        if (dlg is null)
            return;

        if (dlg.Action == SignatureDialogAction.SavePng)
        {
            ConvertStatusText.Text = dlg.SavedPngPath is null
                ? "Signature PNG saved"
                : $"Saved signature · {IoPath.GetFileName(dlg.SavedPngPath)}";
            return;
        }

        if (dlg.Action != SignatureDialogAction.Stamp)
            return;

        try
        {
            var font = string.IsNullOrWhiteSpace(_preferredSignFont) ? dlg.FontFamilyName : _preferredSignFont;
            var tempPng = IoPath.Combine(IoPath.GetTempPath(), $"netie_sig_{Guid.NewGuid():N}.png");
            PdfEditService.RenderTypedSignaturePng(dlg.SignatureName, font, tempPng);
            try { _sigLibrary.AddTyped(dlg.SignatureName, font, tempPng); }
            catch { /* non-fatal */ }
            _pendingStampImage = tempPng;
            _pendingStampName = dlg.SignatureName;
            _pendingStampFont = font;
            await PushStampToViewerAsync(tempPng);
            ConvertStatusText.Text = "Click the PDF page to place your signature";
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Sign failed: {ex.Message}";
        }
    }

    private async void PdfSignStamp_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        var dlg = new OpenFileDialog
        {
            Title = "Choose signature image (PNG/JPG)",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*"
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try { _sigLibrary.AddImage(IoPath.GetFileNameWithoutExtension(dlg.FileName), dlg.FileName); }
        catch { /* non-fatal */ }
        _pendingStampImage = dlg.FileName;
        _pendingStampName = null;
        await PushStampToViewerAsync(dlg.FileName);
        ConvertStatusText.Text = "Click the PDF page to place the stamp";
    }

    private async void PdfSignLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        if (_pdfToolMode != "sign")
            SetPdfToolMode("sign");

        var entries = _sigLibrary.List();
        if (entries.Count == 0)
        {
            ConvertStatusText.Text = "No saved signatures yet — Sign or Stamp image first.";
            return;
        }

        var choices = new List<(string Id, string Label, bool Primary)>();
        for (var i = 0; i < entries.Count; i++)
        {
            var x = entries[i];
            choices.Add((x.Id, $"{x.Label} ({x.Kind})", i == 0));
        }
        choices.Add(("delete", "Delete one…", false));
        choices.Add(("cancel", "Cancel", false));

        var pick = GlassChoiceDialog.Show(this, "My signatures",
            "Pick a saved signature to place, or delete one.", choices);
        if (pick is null or "cancel") return;

        if (pick == "delete")
        {
            var delChoices = new List<(string Id, string Label, bool Primary)>();
            foreach (var x in entries)
                delChoices.Add((x.Id, $"Delete · {x.Label}", false));
            delChoices.Add(("cancel", "Cancel", true));
            var del = GlassChoiceDialog.Show(this, "Delete signature", "Remove from library:", delChoices);
            if (del is null or "cancel") return;
            _sigLibrary.Delete(del);
            ConvertStatusText.Text = "Signature removed from library.";
            return;
        }

        var entry = entries.FirstOrDefault(x => x.Id == pick);
        if (entry?.ImagePath is null || !IoFile.Exists(entry.ImagePath))
        {
            ConvertStatusText.Text = "Signature file missing.";
            return;
        }

        _pendingStampImage = entry.ImagePath;
        _pendingStampName = entry.Label;
        _pendingStampFont = entry.Font;
        await PushStampToViewerAsync(entry.ImagePath);
        ConvertStatusText.Text = $"Using “{entry.Label}” — click the page to place";
    }

    private void PdfCommentHighlight_Click(object sender, RoutedEventArgs e)
    {
        var core = PreviewWebView.CoreWebView2;
        if (core is null) return;
        core.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "setHighlightMode", on = true, kind = "highlight" }));
        ConvertStatusText.Text = "Highlight on — drag across text. Save writes marks into the PDF.";
    }

    private void PdfCommentMark_Click(object sender, RoutedEventArgs e)
    {
        var core = PreviewWebView.CoreWebView2;
        if (core is null) return;
        core.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "setHighlightMode", on = true, kind = "mark" }));
        ConvertStatusText.Text = "Mark on — drag to draw. Save writes marks into the PDF.";
    }

    private void PdfEditTextMode_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath)) return;
        if (!string.Equals(_pdfToolMode, "fill", StringComparison.OrdinalIgnoreCase))
            SetPdfToolMode("fill");
        var core = PreviewWebView.CoreWebView2;
        if (core is null) return;
        PostFormFieldsToViewer(_currentFilePath!);
        core.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "enterTextEditMode" }));
        PostTextBoxesToViewer(_currentFilePath!, 1);
        ConvertStatusText.Text =
            "Edit text — click a word to change it, or OCR this page for scans. Save writes into the file.";
    }

    private void PdfFillForm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath)) return;
        var core = PreviewWebView.CoreWebView2;
        if (core is null) return;
        PostFormFieldsToViewer(_currentFilePath!);
        core.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "enterFillMode" }));
        ConvertStatusText.Text =
            "Fill — click a table cell or blank line, type, then press Save";
    }

    private async void PdfOcrSeed_Click(object sender, RoutedEventArgs e)
    {
        await SeedOcrFillTextsAsync();
    }

    private async void OcrImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrWhiteSpace(_currentFilePath) || _content?.Kind != PreviewKind.Image)
            return;

        _busy = true;
        SetLoading(true, "Reading text…");
        ConvertStatusText.Text = ImageOcrService.IsLocalAvailable
            ? "OCR (local)…"
            : ImageOcrService.IsCloudEnabled
                ? "OCR (cloud, opt-in)…"
                : "OCR…";
        try
        {
            var result = await ImageOcrService.RecognizeLayoutAsync(_currentFilePath);
            if (!result.Ok || string.IsNullOrWhiteSpace(result.FullText))
            {
                ConvertStatusText.Text = result.Error
                    ?? (ImageOcrService.IsCloudEnabled
                        ? "No text found."
                        : "No local OCR. Install Tesseract, or set NETIE_SPACE_OCR_CLOUD=1.");
                return;
            }

            try { Clipboard.SetText(result.FullText); } catch { /* ignore */ }
            var shown = PromptDialog.Ask(this, $"OCR · {result.Engine}",
                "Text read from this image (copied). Edit if you like, then OK.",
                result.FullText);
            if (!string.IsNullOrWhiteSpace(shown))
            {
                try { Clipboard.SetText(shown); } catch { /* ignore */ }
                if (!_chatOpen) OpenChatPanel();
                ChatPanel.StagePackedContext(
                    $"OCR · {IoPath.GetFileName(_currentFilePath)}",
                    shown,
                    $"OCR · {result.Engine}");
            }

            ConvertStatusText.Text = $"OCR · {result.Engine} · {result.Lines.Count} line(s)";
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"OCR failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            SetLoading(false);
        }
    }

    private async Task SeedOcrFillTextsAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath) || _busy)
            return;
        if (!IoPath.GetExtension(_currentFilePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            ConvertStatusText.Text = "OCR fill works on a PDF page.";
            return;
        }

        var core = PreviewWebView.CoreWebView2;
        if (core is null)
        {
            ConvertStatusText.Text = "PDF viewer not ready.";
            return;
        }

        _busy = true;
        SetLoading(true, "OCR this page…");
        ConvertStatusText.Text = ImageOcrService.IsLocalAvailable
            ? "Reading page (local OCR)…"
            : ImageOcrService.IsCloudEnabled
                ? "Reading page (cloud OCR)…"
                : "Reading page…";
        string? tmpPng = null;
        try
        {
            if (!string.Equals(_pdfToolMode, "fill", StringComparison.OrdinalIgnoreCase))
                SetPdfToolMode("fill");
            core.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new { type = "enterFillMode" }));

            var shot = await RequestViewerPagePngAsync();
            if (shot is null || string.IsNullOrWhiteSpace(shot.DataUrl))
            {
                ConvertStatusText.Text = shot?.Error ?? "Could not capture the page for OCR.";
                return;
            }

            tmpPng = IoPath.Combine(IoPath.GetTempPath(), $"netie_ocr_{Guid.NewGuid():N}.png");
            if (!TryWriteDataUrlPng(shot.DataUrl, tmpPng))
            {
                ConvertStatusText.Text = "Page image decode failed.";
                return;
            }

            var layout = await ImageOcrService.RecognizeLayoutAsync(tmpPng);
            if (!layout.Ok || layout.Lines.Count == 0)
            {
                ConvertStatusText.Text = layout.Error
                    ?? (ImageOcrService.IsCloudEnabled
                        ? "No text on this page."
                        : "No local OCR. Install Tesseract, or set NETIE_SPACE_OCR_CLOUD=1.");
                return;
            }

            var page = shot.Page > 0 ? shot.Page : 1;
            var widthPt = shot.WidthPt > 0 ? shot.WidthPt : _pdfEdit.GetPageSize(_currentFilePath!, page).WidthPt;
            var heightPt = shot.HeightPt > 0 ? shot.HeightPt : _pdfEdit.GetPageSize(_currentFilePath!, page).HeightPt;
            if (shot.WidthPx > 0) layout = layout with { ImageWidth = shot.WidthPx };
            if (shot.HeightPx > 0) layout = layout with { ImageHeight = shot.HeightPx };

            var items = ImageOcrService.ToFillTexts(layout, page, widthPt, heightPt);
            if (items.Count == 0)
            {
                ConvertStatusText.Text = $"OCR · {layout.Engine} found text but no boxes.";
                return;
            }

            PostSeedFillTexts(items, replace: false);
            ConvertStatusText.Text =
                $"OCR · {layout.Engine} · {items.Count} box(es) — edit, then Save writes into this PDF";
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"OCR failed: {ex.Message}";
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tmpPng))
            {
                try { IoFile.Delete(tmpPng); } catch { /* ignore */ }
            }
            _busy = false;
            SetLoading(false);
        }
    }

    private void PostSeedFillTexts(IReadOnlyList<PdfFillTextItem> items, bool replace)
    {
        var core = PreviewWebView.CoreWebView2;
        if (core is null) return;
        var texts = items.Select(t => new
        {
            page = t.Page,
            xPt = t.XPt,
            yFromBottom = t.YFromBottom,
            wPt = t.WidthPt,
            hPt = t.HeightPt,
            fontPt = t.FontPt,
            text = t.Text,
            cover = t.CoverBackground,
            snapped = "ocr"
        }).ToList();
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "seedFillTexts",
            replace,
            texts
        });
        core.PostWebMessageAsJson(payload);
    }

    private async Task<PagePngCapture?> RequestViewerPagePngAsync(int page = 0)
    {
        var core = PreviewWebView.CoreWebView2;
        if (core is null) return null;

        _pagePngTcs = new TaskCompletionSource<PagePngCapture>(TaskCreationOptions.RunContinuationsAsynchronously);
        core.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "exportPagePng",
            page
        }));
        var completed = await Task.WhenAny(_pagePngTcs.Task, Task.Delay(4000));
        if (completed != _pagePngTcs.Task)
        {
            AppLogger.Warn("exportPagePng timed out");
            return null;
        }

        return await _pagePngTcs.Task;
    }

    private static bool TryWriteDataUrlPng(string dataUrl, string path)
    {
        try
        {
            var comma = dataUrl.IndexOf(',');
            if (comma < 0) return false;
            var bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
            IoFile.WriteAllBytes(path, bytes);
            return bytes.Length > 32;
        }
        catch
        {
            return false;
        }
    }

    private async Task PushStampToViewerAsync(string pngPath)
    {
        await EnsureWebViewAsync();
        var core = PreviewWebView.CoreWebView2;
        if (core is null || !IoFile.Exists(pngPath))
            return;

        if (!string.IsNullOrWhiteSpace(_currentFilePath))
            PostFormFieldsToViewer(_currentFilePath);

        var bytes = await IoFile.ReadAllBytesAsync(pngPath);
        var dataUrl = "data:image/png;base64," + Convert.ToBase64String(bytes);
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "enterStampMode",
            dataUrl
        });
        core.PostWebMessageAsJson(payload);
    }

    private void PdfChrome_DragOver(object sender, DragEventArgs e)
    {
        if (_content?.Kind != PreviewKind.Pdf || string.IsNullOrWhiteSpace(_currentFilePath))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var ok = TryGetDroppedImages(e, out _) || TryGetDroppedConvertibles(e, out _);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (ok) ShowPdfDropOverlay();
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (_content?.Kind != PreviewKind.Pdf || string.IsNullOrWhiteSpace(_currentFilePath))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            HidePdfDropOverlay();
            return;
        }

        var ok = TryGetDroppedImages(e, out _) || TryGetDroppedConvertibles(e, out _);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (ok)
            ShowPdfDropOverlay();
        else
            HidePdfDropOverlay();
    }

    private void PdfDropOverlay_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedImages(e, out _) || TryGetDroppedConvertibles(e, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void PdfDropOverlay_DragLeave(object sender, DragEventArgs e) => HidePdfDropOverlay();

    private void ShowPdfDropOverlay()
    {
        if (PdfDropOverlay.Visibility != Visibility.Visible)
            PdfDropOverlay.Visibility = Visibility.Visible;
    }

    private void HidePdfDropOverlay()
    {
        if (PdfDropOverlay.Visibility != Visibility.Collapsed)
            PdfDropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        HidePdfDropOverlay();
        if (_content?.Kind != PreviewKind.Pdf || string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        if (TryGetDroppedConvertibles(e, out var convertibles) && convertibles.Count > 0)
        {
            e.Handled = true;
            await InsertConvertiblesAsPdfPagesAsync(convertibles);
            return;
        }

        if (!TryGetDroppedImages(e, out var images) || images.Count == 0)
            return;

        e.Handled = true;
        var pdf = _currentFilePath!;
        var imgs = images.ToList();
        await RunPdfEditAsync(imgs.Count == 1
                ? $"Inserting {IoPath.GetFileName(imgs[0])}…"
                : $"Inserting {imgs.Count} images…",
            () =>
            {
                var count = _pdfEdit.GetPageCount(pdf);
                var slots = Enumerable.Range(0, count)
                    .Select(i => new PdfComposeSlot { SourcePageIndex = i })
                    .Concat(imgs.Select(p => new PdfComposeSlot { IsImage = true, ImagePath = p }))
                    .ToList();
                return _pdfEdit.ComposePages(pdf, slots, "imgpage");
            },
            openResult: true);
    }

    private async Task InsertConvertiblesAsPdfPagesAsync(List<string> paths)
    {
        var pdf = _currentFilePath!;
        var imageSlots = new List<string>();
        foreach (var path in paths)
        {
            var ext = IoPath.GetExtension(path).ToLowerInvariant();
            if (PdfOrganizeWindow.IsImagePath(path))
            {
                imageSlots.Add(path);
                continue;
            }

            ConvertStatusText.Text = $"Converting {IoPath.GetFileName(path)} → PDF…";
            var result = await _conversion.ConvertToPdfAsync(path, CancellationToken.None);
            if (!result.Success || string.IsNullOrWhiteSpace(result.OutputPath))
            {
                ConvertStatusText.Text = result.Error ?? $"Could not convert {IoPath.GetFileName(path)}";
                continue;
            }

            // Merge converted PDF pages onto end
            await RunPdfEditAsync($"Appending {IoPath.GetFileName(result.OutputPath)}…",
                () => _pdfEdit.Merge(pdf, [result.OutputPath]),
                openResult: true);
            pdf = _currentFilePath ?? pdf;
        }

        if (imageSlots.Count > 0)
        {
            var basePdf = _currentFilePath!;
            await RunPdfEditAsync("Inserting images…",
                () =>
                {
                    var count = _pdfEdit.GetPageCount(basePdf);
                    var slots = Enumerable.Range(0, count)
                        .Select(i => new PdfComposeSlot { SourcePageIndex = i })
                        .Concat(imageSlots.Select(p => new PdfComposeSlot { IsImage = true, ImagePath = p }))
                        .ToList();
                    return _pdfEdit.ComposePages(basePdf, slots, "imgpage");
                },
                openResult: true);
        }
    }

    private static bool TryGetDroppedConvertibles(DragEventArgs e, out List<string> files)
    {
        files = new List<string>();
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return false;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] dropped)
            return false;

        foreach (var f in dropped.Where(IoFile.Exists))
        {
            var ext = IoPath.GetExtension(f).ToLowerInvariant();
            if (PdfOrganizeWindow.IsImagePath(f) ||
                ext is ".pdf" or ".doc" or ".docx" or ".odt" or ".rtf" or ".txt" or ".html" or ".htm" or
                    ".pptx" or ".ppt" or ".xlsx" or ".xls" or ".png" or ".jpg")
                files.Add(f);
        }

        return files.Count > 0;
    }

    private static bool TryGetDroppedImages(DragEventArgs e, out List<string> images)
    {
        images = new List<string>();
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return false;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return false;

        images = files.Where(PdfOrganizeWindow.IsImagePath).Where(IoFile.Exists).ToList();
        return images.Count > 0;
    }

    private async Task RunPdfEditAsync(string status, Func<string> action, bool openResult, Action<string>? onSuccess = null)
    {
        if (_busy || string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        _busy = true;
        SetLoading(true, status);
        ConvertStatusText.Text = status;
        try
        {
            var output = await Task.Run(action);
            if (onSuccess is not null)
                onSuccess(output);
            else
                ConvertStatusText.Text = $"Saved · {IoPath.GetFileName(output)}";

            if (openResult && IoFile.Exists(output))
                await LoadFileAsync(output);
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"PDF edit failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            SetLoading(false);
        }
    }

    private bool EnsureMarks(out TimeSpan a, out TimeSpan b)
    {
        a = _markIn ?? TimeSpan.Zero;
        b = _markOut ?? TimeSpan.Zero;
        if (!_markIn.HasValue || !_markOut.HasValue)
        {
            ConvertStatusText.Text = "Set Mark In and Mark Out on the timeline first.";
            return false;
        }

        if (b <= a)
        {
            ConvertStatusText.Text = "Mark Out must be after Mark In.";
            return false;
        }

        return true;
    }

    private async Task RunFfmpegAsync(string status, Func<Task<FfmpegResult>> action)
    {
        if (_busy || string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        if (!_ffmpeg.IsAvailable)
        {
            ConvertStatusText.Text = "FFmpeg not found — run scripts/download-ffmpeg.ps1";
            StatusText.Text = "FFmpeg not found — run scripts/download-ffmpeg.ps1";
            return;
        }

        _busy = true;
        StopVideo();
        SetLoading(true, status);
        ConvertStatusText.Text = status;
        try
        {
            var result = await action();
            if (result.Success && !string.IsNullOrWhiteSpace(result.OutputPath))
            {
                ConvertStatusText.Text = $"Saved {IoPath.GetFileName(result.OutputPath)}";
                await LoadFileAsync(result.OutputPath);
            }
            else
            {
                ConvertStatusText.Text = $"Failed: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            SetLoading(false);
        }
    }

    private void EnsurePreviewHandler() =>
        _previewHandler ??= new PreviewHandlerOverlay(this, ContentHost);

    private void HidePreviewHandler() => _previewHandler?.Hide();

    private void DisposePreviewHandler()
    {
        _previewHandler?.Dispose();
        _previewHandler = null;
    }

    private void StopVideo()
    {
        _videoTimer.Stop();
        _editMode = false;
        try
        {
            PreviewVideo.Stop();
            PreviewVideo.Source = null;
        }
        catch
        {
            // ignore
        }

        PreviewVideo.Visibility = Visibility.Visible;
    }

    private async void ConvertMdButton_Click(object sender, RoutedEventArgs e)
    {
        SharePopup.IsOpen = false;
        await RunConversionAsync("MD", _conversion.ConvertToMarkdownAsync);
    }

    private async void ConvertPdfButton_Click(object sender, RoutedEventArgs e)
    {
        SharePopup.IsOpen = false;
        await RunConversionAsync("PDF", _conversion.ConvertToPdfAsync);
    }

    private async void EditHtmlButton_Click(object sender, RoutedEventArgs e)
    {
        SharePopup.IsOpen = false;
        if (!IsHtmlWordEditable())
        {
            ConvertStatusText.Text = "Open a Word (.docx) or HTML file to edit in the preview.";
            return;
        }

        await ToggleHtmlEditModeAsync();
    }

    private bool IsHtmlWordEditable() =>
        _content is not null &&
        OfficePreviewService.IsHtmlWordEditable(_content.Kind, _currentFilePath);

    private string HtmlWordBrandLabel()
    {
        var ext = IoPath.GetExtension(_currentFilePath ?? "").ToLowerInvariant();
        return ext == ".docx" ? "Word Editor" : "HTML Editor";
    }

    private async Task ToggleHtmlEditModeAsync()
    {
        if (!IsHtmlWordEditable())
            return;

        _textEditMode = !_textEditMode;
        UpdateActionButtons(_content!.Kind);
        await EnsureWebViewAsync();
        if (PreviewWebView.CoreWebView2 is not { } core)
        {
            _textEditMode = false;
            UpdateActionButtons(_content.Kind);
            ConvertStatusText.Text = "HTML edit needs the preview WebView.";
            return;
        }

        try
        {
            if (_textEditMode)
            {
                await core.ExecuteScriptAsync("""
                    (function(){
                      var target = document.querySelector('article.office-body') || document.body;
                      if (!target) return 'no-target';
                      target.setAttribute('contenteditable','true');
                      target.style.outline = '1px solid rgba(94,196,184,0.45)';
                      target.style.borderRadius = '8px';
                      function notify(typing){
                        try{chrome.webview.postMessage(JSON.stringify({type:'netieTyping',typing:!!typing}));}catch(e){}
                      }
                      function save(){
                        try{chrome.webview.postMessage(JSON.stringify({type:'htmlEdit'}));}catch(e){}
                      }
                      window.__netieSave = save;
                      if (!target.__netieHtmlEditWired){
                        target.__netieHtmlEditWired = true;
                        target.addEventListener('focus', function(){ notify(true); });
                        target.addEventListener('blur', function(){ notify(false); save(); });
                        target.addEventListener('input', function(){
                          notify(true);
                          clearTimeout(window.__netieSaveT);
                          window.__netieSaveT = setTimeout(save, 400);
                        });
                      }
                      notify(true);
                      target.focus();
                      return 'editing';
                    })();
                    """);
                ConvertStatusText.Text = "Editing Word/HTML — autosaves as you type.";
                _webViewTyping = true;
            }
            else
            {
                _textAutosaveCts?.Cancel();
                await core.ExecuteScriptAsync("""
                    (function(){
                      var target = document.querySelector('[contenteditable="true"]')
                        || document.querySelector('article.office-body')
                        || document.body;
                      if (target){
                        target.removeAttribute('contenteditable');
                        target.style.outline = '';
                      }
                      try{chrome.webview.postMessage(JSON.stringify({type:'netieTyping',typing:false}));}catch(e){}
                    })();
                    """);
                _webViewTyping = false;
                await PersistHtmlEditAsync(CancellationToken.None);
                if (!ConvertStatusText.Text.StartsWith("Save failed", StringComparison.Ordinal))
                {
                    ConvertStatusText.Text = ConvertStatusText.Text.StartsWith("Saved", StringComparison.Ordinal)
                        ? ConvertStatusText.Text + " · edit closed."
                        : "Edit closed.";
                }
            }
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"HTML edit failed: {ex.Message}";
            _textEditMode = false;
            _webViewTyping = false;
            if (_content is not null)
                UpdateActionButtons(_content.Kind);
        }
    }

    private void ScheduleHtmlAutosave()
    {
        if (!IsHtmlWordEditable() || string.IsNullOrWhiteSpace(_currentFilePath))
            return;
        _textAutosaveCts?.Cancel();
        _textAutosaveCts = new CancellationTokenSource();
        var ct = _textAutosaveCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, ct);
                if (ct.IsCancellationRequested)
                    return;
                await Dispatcher.InvokeAsync(new Func<Task>(() => PersistHtmlEditAsync(ct))).Task.Unwrap();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => ConvertStatusText.Text = $"Save failed: {ex.Message}");
            }
        }, ct);
    }

    private async Task PersistHtmlEditAsync(CancellationToken ct)
    {
        if (!IsHtmlWordEditable() || string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        try
        {
            var fetched = await FetchEditableHtmlAsync();
            if (ct.IsCancellationRequested || fetched is null)
                return;

            var path = _currentFilePath!;
            var mediaDir = !string.IsNullOrWhiteSpace(_content?.HtmlFilePath)
                ? IoPath.GetDirectoryName(_content!.HtmlFilePath)
                : IoPath.GetDirectoryName(path);

            var result = await Task.Run(
                () => OfficePreviewService.SaveEditedPreview(path, fetched.DocumentHtml, fetched.ArticleHtml, mediaDir),
                ct);
            if (ct.IsCancellationRequested)
                return;

            ConvertStatusText.Text = result.Success ? result.Message : $"Save failed: {result.Message}";
            if (result.Success && _content is not null)
                _content.RawText = fetched.ArticleHtml;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    private sealed record HtmlEditPayload(string DocumentHtml, string ArticleHtml);

    private async Task<HtmlEditPayload?> FetchEditableHtmlAsync()
    {
        await EnsureWebViewAsync();
        if (PreviewWebView.CoreWebView2 is not { } core)
            return null;

        var quoted = await core.ExecuteScriptAsync("""
            (function(){
              var article = document.querySelector('article.office-body');
              var target = article || document.body;
              var ce = target ? target.getAttribute('contenteditable') : null;
              var outline = target ? target.style.outline : '';
              if (target){
                target.removeAttribute('contenteditable');
                target.style.outline = '';
              }
              var dt = document.doctype ? '<!DOCTYPE html>\n' : '';
              var doc = dt + document.documentElement.outerHTML;
              var articleHtml = article ? article.innerHTML : (document.body ? document.body.innerHTML : '');
              if (target && ce) target.setAttribute('contenteditable', ce);
              if (target) target.style.outline = outline;
              return JSON.stringify({doc: doc, article: articleHtml});
            })();
            """);
        var json = UnquoteJsString(quoted);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        var root = parsed.RootElement;
        var doc = root.TryGetProperty("doc", out var d) ? d.GetString() ?? "" : "";
        var article = root.TryGetProperty("article", out var a) ? a.GetString() ?? "" : "";
        return new HtmlEditPayload(doc, article);
    }

    private async Task FlushHtmlEditIfNeededAsync()
    {
        if (!_textEditMode || !IsHtmlWordEditable())
            return;
        _textAutosaveCts?.Cancel();
        try
        {
            await PersistHtmlEditAsync(CancellationToken.None);
        }
        catch
        {
            // closing — status already set if persist failed
        }
    }

    // ── Share sheet ────────────────────────────────────────────────────────────

    private async void ShareButton_Click(object sender, RoutedEventArgs e)
    {
        if (SharePopup.IsOpen)
        {
            SharePopup.IsOpen = false;
            return;
        }

        ShareSubjectText.Text = string.IsNullOrWhiteSpace(_currentFilePath)
            ? "Nothing open"
            : IoPath.GetFileName(_currentFilePath);

        var haveFile = !string.IsNullOrWhiteSpace(_currentFilePath) && IoFile.Exists(_currentFilePath);
        var formats = haveFile && _convertTargetsAvailable;
        ShareFormatsLabel.Visibility = formats ? Visibility.Visible : Visibility.Collapsed;
        ShareMdRow.Visibility = formats ? Visibility.Visible : Visibility.Collapsed;
        SharePdfRow.Visibility = formats ? Visibility.Visible : Visibility.Collapsed;
        ShareHtmlRow.Visibility =
            formats && IsHtmlWordEditable()
                ? Visibility.Visible
                : Visibility.Collapsed;
        ShareExportAsLabel.Visibility = formats ? Visibility.Visible : Visibility.Collapsed;
        ShareExportAsRow.Visibility = formats ? Visibility.Visible : Visibility.Collapsed;
        if (formats && !string.IsNullOrWhiteSpace(_currentFilePath))
            ShareExportAsHint.Text = DescribeExportFormats(_currentFilePath, _content?.Kind);

        SharePopup.IsOpen = true;

        // Fill the selection hint after the sheet is up — the script hop costs a frame.
        var selection = await TryGetPreviewSelectionAsync();
        if (string.IsNullOrWhiteSpace(selection))
            selection = SafeClipboardText();

        ShareSelectionHint.Text = string.IsNullOrWhiteSpace(selection)
            ? haveFile ? "Nothing highlighted — copies the parsed text instead" : "Nothing highlighted"
            : Ellipsize(selection.Replace('\n', ' ').Replace('\r', ' '), 46);
    }

    private async void ShareExportAs_Click(object sender, RoutedEventArgs e)
    {
        SharePopup.IsOpen = false;
        if (!TryRequireFile(out var path))
            return;
        if (!_conversion.IsAvailable)
        {
            ConvertStatusText.Text = "Conversion engine not found. Ensure tools/convert_engine.py exists.";
            return;
        }

        var (filter, defaultExt, filterIndex, defaultName) = BuildExportAsDialog(path, _content?.Kind);
        var dir = IoPath.GetDirectoryName(path) ?? "";
        var dlg = new SaveFileDialog
        {
            Title = "Export as",
            Filter = filter,
            DefaultExt = defaultExt,
            FilterIndex = filterIndex,
            AddExtension = true,
            FileName = defaultName,
            InitialDirectory = dir,
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) != true)
            return;

        await RunExportAsAsync(dlg.FileName);
    }

    private static string DescribeExportFormats(string sourcePath, PreviewKind? kind)
    {
        var ext = IoPath.GetExtension(sourcePath).ToLowerInvariant();
        return ext switch
        {
            ".docx" or ".doc" or ".odt" or ".rtf" => "PDF, Word, ODT, HTML…",
            ".xlsx" or ".xls" or ".ods" or ".csv" => "PDF, Excel, ODS, HTML…",
            ".pptx" or ".ppt" or ".odp" => "PDF, PowerPoint, HTML…",
            ".pdf" => "PDF, JPG, PNG, Word, HTML…",
            ".md" or ".markdown" => "PDF, Markdown, HTML, Word…",
            _ when kind == PreviewKind.Image => "JPG, PNG, WEBP, GIF, PDF…",
            _ when kind == PreviewKind.Video => "MP4, MOV, MKV, WEBM, GIF, MP3…",
            ".html" or ".htm" => "HTML, PDF, Word…",
            _ => "PDF, HTML, Word…"
        };
    }

    private static (string filter, string defaultExt, int filterIndex, string defaultName) BuildExportAsDialog(
        string sourcePath, PreviewKind? kind)
    {
        var ext = IoPath.GetExtension(sourcePath).ToLowerInvariant();
        var stem = IoPath.GetFileNameWithoutExtension(sourcePath);

        // Prefer keeping the source type as the first SaveFileDialog filter when exporting images.
        var imageDefaultExt = ext.TrimStart('.') switch
        {
            "jpeg" => "jpg",
            "tif" => "tif",
            "jpg" or "png" or "webp" or "gif" or "bmp" or "ico" => ext.TrimStart('.'),
            _ => "jpg"
        };
        var imageFilterIndex = imageDefaultExt switch
        {
            "jpg" => 1,
            "png" => 2,
            "webp" => 3,
            "gif" => 4,
            "bmp" => 5,
            "tif" or "tiff" => 6,
            "ico" => 7,
            _ => 1
        };

        return ext switch
        {
            ".docx" or ".doc" or ".odt" or ".rtf" => (
                "PDF document|*.pdf|Word document|*.docx|OpenDocument Text|*.odt|Web page|*.html|All files|*.*",
                "pdf", 1, stem),
            ".xlsx" or ".xls" or ".ods" or ".csv" => (
                "PDF document|*.pdf|Excel workbook|*.xlsx|OpenDocument Spreadsheet|*.ods|Web page|*.html|All files|*.*",
                "pdf", 1, stem),
            ".pptx" or ".ppt" or ".odp" => (
                "PDF document|*.pdf|PowerPoint presentation|*.pptx|Web page|*.html|All files|*.*",
                "pdf", 1, stem),
            ".pdf" => (
                "PDF document|*.pdf|JPG image|*.jpg|PNG image|*.png|Word document|*.docx|Web page|*.html|All files|*.*",
                "pdf", 1, stem),
            ".md" or ".markdown" => (
                "PDF document|*.pdf|Markdown|*.md|Web page|*.html|Word document|*.docx|All files|*.*",
                "pdf", 1, stem),
            ".html" or ".htm" => (
                "Web page|*.html|PDF document|*.pdf|Word document|*.docx|All files|*.*",
                "html", 1, stem),
            _ when kind == PreviewKind.Image => (
                "JPG image|*.jpg|PNG image|*.png|WEBP image|*.webp|GIF image|*.gif|BMP image|*.bmp|TIFF image|*.tif|Icon|*.ico|PDF document|*.pdf|All files|*.*",
                imageDefaultExt, imageFilterIndex, stem),
            _ when kind == PreviewKind.Video => (
                "MP4 video|*.mp4|MOV video|*.mov|MKV video|*.mkv|WEBM video|*.webm|Animated GIF|*.gif|MP3 audio|*.mp3|All files|*.*",
                "mp4", 1, stem),
            _ => (
                "PDF document|*.pdf|Web page|*.html|Word document|*.docx|Markdown|*.md|All files|*.*",
                "pdf", 1, stem)
        };
    }

    private async Task RunExportAsAsync(string outputPath)
    {
        if (_busy || string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        _busy = true;
        ShareButton.IsEnabled = false;
        var label = IoPath.GetExtension(outputPath).TrimStart('.').ToUpperInvariant();
        SetLoading(true, $"Exporting as {label}…");
        ConvertStatusText.Text = $"Exporting as {label}…";

        try
        {
            var result = await _conversion.ExportAsAsync(
                _currentFilePath,
                outputPath,
                _cts?.Token ?? CancellationToken.None);
            if (result.Success && !string.IsNullOrWhiteSpace(result.OutputPath))
            {
                ConvertStatusText.Text =
                    $"Exported {IoPath.GetFileName(outputPath)} via {result.Tool} · {FormatSize(result.Bytes)}";
            }
            else
            {
                ConvertStatusText.Text = string.IsNullOrWhiteSpace(result.Error)
                    ? "Export failed."
                    : result.Error;
            }
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Export failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            ShareButton.IsEnabled = true;
            SetLoading(false);
        }
    }

    private void ShareClose_Click(object sender, RoutedEventArgs e) => SharePopup.IsOpen = false;

    private async void ShareCopySelection_Click(object sender, RoutedEventArgs e)
    {
        SharePopup.IsOpen = false;

        var text = await TryGetPreviewSelectionAsync();
        if (string.IsNullOrWhiteSpace(text))
            text = _content?.RawText?.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            ConvertStatusText.Text = "Nothing to copy — highlight some text first.";
            return;
        }

        if (TrySetClipboardText(text))
            ConvertStatusText.Text = $"Copied {text.Length:N0} chars — paste anywhere.";
    }

    private void ShareCopyFile_Click(object sender, RoutedEventArgs e)
    {
        SharePopup.IsOpen = false;
        if (!TryRequireFile(out var path)) return;

        try
        {
            var files = new System.Collections.Specialized.StringCollection { path };
            Clipboard.SetFileDropList(files);
            ConvertStatusText.Text = $"Copied {IoPath.GetFileName(path)} — paste into Mail, Teams or a folder.";
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Copy failed: {ex.Message}";
        }
    }

    private async void ShareMailButton_Click(object sender, RoutedEventArgs e)
    {
        SharePopup.IsOpen = false;
        if (!TryRequireFile(out var path)) return;

        var body = await TryGetPreviewSelectionAsync();
        if (string.IsNullOrWhiteSpace(body))
            body = _content?.RawText?.Trim();
        // mailto URLs are capped by the shell — keep the body short and attach by copy instead.
        body = Ellipsize(body ?? "", 1400);

        var subject = Uri.EscapeDataString(IoPath.GetFileName(path));
        var encoded = Uri.EscapeDataString(
            string.IsNullOrWhiteSpace(body)
                ? $"Sharing {IoPath.GetFileName(path)}.\n\n(File is on the clipboard — paste to attach.)"
                : $"{body}\n\n— from {IoPath.GetFileName(path)} (file is on the clipboard — paste to attach.)");

        try
        {
            var files = new System.Collections.Specialized.StringCollection { path };
            Clipboard.SetFileDropList(files);
        }
        catch
        {
            // Clipboard is best-effort; the mail draft still opens.
        }

        try
        {
            Process.Start(new ProcessStartInfo($"mailto:?subject={subject}&body={encoded}") { UseShellExecute = true });
            ConvertStatusText.Text = "Mail draft opened — the file is on your clipboard, press Ctrl+V to attach.";
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"No mail app: {ex.Message}";
        }
    }

    private void ShareOpenWithButton_Click(object sender, RoutedEventArgs e)
    {
        SharePopup.IsOpen = false;
        if (!TryRequireFile(out var path)) return;

        try
        {
            Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {path}")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Open with failed: {ex.Message}";
        }
    }

    private void ShareRevealButton_Click(object sender, RoutedEventArgs e)
    {
        SharePopup.IsOpen = false;
        if (!TryRequireFile(out var path)) return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Reveal failed: {ex.Message}";
        }
    }

    private bool TryRequireFile(out string path)
    {
        path = _currentFilePath ?? "";
        if (!string.IsNullOrWhiteSpace(path) && IoFile.Exists(path))
            return true;

        ConvertStatusText.Text = "Open a file first.";
        return false;
    }

    private bool TrySetClipboardText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Clipboard busy: {ex.Message}";
            return false;
        }
    }

    private static string SafeClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    private static string Ellipsize(string text, int max)
    {
        var flat = text.Trim();
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    private async Task RunConversionAsync(string label, Func<string, CancellationToken, Task<ConversionResult>> action)
    {
        if (_busy || string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        _busy = true;
        ShareButton.IsEnabled = false;
        SetLoading(true, $"Converting to {label}...");
        ConvertStatusText.Text = $"Converting to {label}...";

        try
        {
            var result = await action(_currentFilePath, _cts?.Token ?? CancellationToken.None);
            if (result.Success && !string.IsNullOrWhiteSpace(result.OutputPath))
            {
                ConvertStatusText.Text = $"Saved {label} via {result.Tool} · {FormatSize(result.Bytes)}";
                if (label == "MD" && IoFile.Exists(result.OutputPath))
                    await LoadFileAsync(result.OutputPath);
            }
            else
            {
                ConvertStatusText.Text = $"Failed: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            ShareButton.IsEnabled = true;
            SetLoading(false);
        }
    }

    private DispatcherTimer? _loadingShowTimer;

    private void SetLoading(bool visible, string? message = null, bool showProgress = false, double percent = 0)
    {
        if (message is not null)
            LoadingText.Text = message;
        if (LoadingProgress is not null)
        {
            LoadingProgress.Visibility = visible && showProgress ? Visibility.Visible : Visibility.Collapsed;
            if (showProgress)
                LoadingProgress.Value = Math.Clamp(percent, 0, 100);
        }

        if (visible)
        {
            // Delay showing so fast paths (small .txt/.md, warm WebView) never flash the overlay.
            if (_loadingShowTimer is null)
            {
                _loadingShowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                _loadingShowTimer.Tick += (_, _) =>
                {
                    _loadingShowTimer!.Stop();
                    LoadingOverlay.Visibility = Visibility.Visible;
                    AnimationHelper.AnimateOpenFadeOnly(LoadingOverlay, 160);
                };
            }
            _loadingShowTimer.Stop();
            _loadingShowTimer.Start();
        }
        else
        {
            _loadingShowTimer?.Stop();
            if (LoadingOverlay.Visibility == Visibility.Visible)
            {
                _ = AnimationHelper.AnimateCloseFadeOnlyAsync(LoadingOverlay, 140)
                    .ContinueWith(_ =>
                    {
                        LoadingOverlay.Visibility = Visibility.Collapsed;
                        LoadingOverlay.BeginAnimation(OpacityProperty, null);
                        LoadingOverlay.Opacity = 1;
                    }, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }
    }

    // Window_Closing moved next to CloseButton_Click

    private async Task LoadSummaryAsync(PreviewContent content, CancellationToken ct)
    {
        if (!_summaryBarEnabled)
            return;
        if (content.Kind == PreviewKind.Video)
            return;

        var local = LocalSummaryService.Build(content);
        SummaryText.Text = local;

        try
        {
            var summary = await _ai.SummarizeAsync(content, ct);
            // SummarizeAsync already falls back to local on provider failure — never dump JSON here
            SummaryText.Text = string.IsNullOrWhiteSpace(summary) ? local : summary;
            ProviderButton.Content = $"AI: {_ai.ActiveProvider}";
        }
        catch (Exception ex)
        {
            SummaryText.Text = local;
            ConvertStatusText.Text = LocalSummaryService.SanitizeError(ex.Message);
        }
    }

    private async void OnChatSendRequested(string message)
    {
        if (_content is null) return;
        if (!_ai.IsConfigured)
        {
            ChatPanel.AddAssistantMessage(
                "No AI key yet — use Pin note for ideas (saved on this file), or open Setup for Netie plan / BYO keys.");
            return;
        }

        ChatPanel.SetBusy(true);
        try
        {
            var response = await _ai.ChatAsync(_content, ChatPanel.GetHistory(), message);
            ChatPanel.AddAssistantMessage(
                LocalSummaryService.LooksLikeProviderFailure(response)
                    ? LocalSummaryService.FriendlyChatFailure(response)
                    : response);
            ProviderButton.Content = $"AI: {_ai.ActiveProvider}";
        }
        catch (Exception ex)
        {
            ChatPanel.AddAssistantMessage(LocalSummaryService.FriendlyChatFailure(ex.Message));
        }
        finally
        {
            ChatPanel.SetBusy(false);
        }
    }

    private async void OnLookupRequested(string word) => await RunLookupAsync(word);

    private void OnAddToChatRequested(string text) => AddSelectionToChat(text);

    private async Task RunLookupAsync(string word, string? surroundingContext = null)
    {
        if (!_ai.IsConfigured)
        {
            _openApiSetup?.Invoke();
            return;
        }

        if (string.IsNullOrWhiteSpace(word))
            return;

        if (!_chatOpen) OpenChatPanel();

        // Enrich when host only passed the bare word (asset tray / legacy)
        if (string.IsNullOrWhiteSpace(surroundingContext) &&
            !word.StartsWith("[image]", StringComparison.OrdinalIgnoreCase))
        {
            var (_, ctx) = await TryGetLookupPayloadAsync(word);
            surroundingContext = ctx;
        }

        ChatPanel.SetBusy(true);
        ChatPanel.AddUserMessage($"Look up: {word}");

        try
        {
            var definition = await _ai.LookupWordAsync(word, surroundingContext);
            var clean = LocalSummaryService.LooksLikeProviderFailure(definition)
                ? LocalSummaryService.FriendlyChatFailure(definition)
                : definition;
            ChatPanel.AddAssistantMessage(clean);
            if (!LocalSummaryService.LooksLikeProviderFailure(definition))
                ChatPanel.AddNote($"📌 Look up: {word}\n\n{clean}");
            ProviderButton.Content = $"AI: {_ai.ActiveProvider}";
        }
        catch (Exception ex)
        {
            ChatPanel.AddAssistantMessage(LocalSummaryService.FriendlyChatFailure(ex.Message));
        }
        finally
        {
            ChatPanel.SetBusy(false);
        }
    }

    private void AddSelectionToChat(string text)
    {
        if (!_chatOpen) OpenChatPanel();
        ChatPanel.StageSelection(text);
    }

    private void OpenChatPanel()
    {
        _chatOpen = true;
        ChatPanel.Visibility = Visibility.Visible;
        ChatPanel.RenderTransform = null;
        ChatPanel.BeginAnimation(OpacityProperty, null);
        // Start from current opacity if mid-close (interruptible reopen)
        if (ChatPanel.Opacity < 0.05)
            ChatPanel.Opacity = 0;

        _chatAnimating = true;
        // Sheet response — from live column width (gesture / re-open safe)
        AnimationHelper.AnimateColumn(ChatColumn, ChatColumn.Width.Value, ChatPanelWidth,
            AppleMotion.SheetResponseMs + 20,
            completed: () =>
            {
                _chatAnimating = false;
                UpdateCornerMasks();
            });
        AppleMotion.SpringDouble(ChatPanel, OpacityProperty, 1.0, AppleMotion.SheetResponseMs,
            fromOverride: ChatPanel.Opacity);

        if (!string.IsNullOrWhiteSpace(_currentFilePath))
            ChatPanel.BindToDocument(_currentFilePath);
    }

    private void CloseChatPanel()
    {
        try { ChatPanel.Persist(); } catch { /* ignore */ }
        _chatOpen = false;
        _chatAnimating = true;

        // Symmetric path: sheet exits the way it entered (width → 0). Interruptible if reopened.
        AnimationHelper.AnimateColumn(ChatColumn, ChatColumn.Width.Value, 0, AppleMotion.SheetResponseMs,
            completed: () =>
            {
                _chatAnimating = false;
                if (!_chatOpen)
                {
                    ChatPanel.Visibility = Visibility.Collapsed;
                    ChatPanel.BeginAnimation(OpacityProperty, null);
                    ChatPanel.Opacity = 1;
                    ChatPanel.RenderTransform = null;
                }
                UpdateCornerMasks();
            });
        AppleMotion.SpringDouble(ChatPanel, OpacityProperty, 0.0,
            AppleMotion.SheetResponseMs - 40,
            fromOverride: ChatPanel.Opacity);
    }

    private void ContinueChatButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_ai.IsConfigured && !ChatPanel.NotesOnlyMode)
        {
            // Still open — notes work offline; nudge setup once
            ConvertStatusText.Text = "Chat open — Pin note works offline · Setup for AI keys / Netie plan";
        }

        if (_chatOpen)
        {
            ChatPanel.FocusInput();
            return;
        }

        OpenChatPanel();
        ChatPanel.FocusInput();
    }

    private void SetupHelpButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var win = new ApiSetupWindow(_ai, () =>
            {
                ProviderButton.Content = _ai.IsConfigured
                    ? $"AI: {_ai.ActiveProvider}"
                    : "AI: —";
            });
            win.Owner = this;
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Setup: {ex.Message}";
        }
    }

    private async void CompressImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath) || !IoFile.Exists(_currentFilePath))
            return;

        try
        {
            var dest = ImageEditService.PromptCompress(this, _currentFilePath!);
            if (dest is null) return;
            ConvertStatusText.Text =
                $"Compressed → {IoPath.GetFileName(dest)} · {FormatSize(new FileInfo(dest).Length)} (was {FormatSize(new FileInfo(_currentFilePath!).Length)})";
            var open = GlassChoiceDialog.Show(this, "Compressed",
                $"Saved:\n{dest}\n\nOpen the smaller file now?",
                [("open", "Open it", true), ("stay", "Stay here", false)]);
            if (open == "open")
                await LoadFileAsync(dest);
        }
        catch (Exception ex)
        {
            ConvertStatusText.Text = $"Compress failed: {ex.Message}";
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Re-center after DPI/layout is final (first Position may run pre-Show)
        PositionOnOwnerMonitor(_ownerHwnd);
        UpdateCornerMasks();
        AnimationHelper.AnimateOpen(RootBorder, _animationMs);
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        await FlushHtmlEditIfNeededAsync();

        if (!await PromptSavePendingPdfAsync())
            return;

        _textEditMode = false;

        _allowClose = true;
        _cts?.Cancel();
        StopVideo();
        HidePreviewHandler();
        await AnimationHelper.AnimateCloseAsync(RootBorder, _animationMs - 40);
        Close();
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_textEditMode && IsHtmlWordEditable())
        {
            e.Cancel = true;
            await FlushHtmlEditIfNeededAsync();
            _textEditMode = false;
            Close();
            return;
        }

        if (!_allowClose && _content?.Kind == PreviewKind.Pdf)
        {
            e.Cancel = true;
            if (!await PromptSavePendingPdfAsync())
                return;
            _allowClose = true;
            Close();
            return;
        }

        if (_exportCts is { IsCancellationRequested: false } && BackgroundExportService.Instance.IsExporting)
        {
            _exportDetached = true;
            ConvertStatusText.Text = "Export continues in background…";
        }

        if (BackgroundExportService.Instance.IsCaptionGenerating && !_captionDetached)
        {
            _captionDetached = true;
            if (!_exportDetached)
                ConvertStatusText.Text = "Captions continue in background — toast when done.";
        }

        try { ChatPanel.Persist(); } catch { /* ignore */ }
        DisposePreviewHandler();
        if (!string.IsNullOrWhiteSpace(_nobgTempPath) &&
            !string.Equals(_nobgTempPath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
            DeleteNobgTemp(_nobgTempPath);
    }

    private void SummaryText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_ai.IsConfigured)
            _openApiSetup?.Invoke();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (ChatPanel.IsKeyboardFocusWithin)
            return;

        if (e.Key == Key.Escape)
        {
            if (FileNameEditBox?.Visibility == Visibility.Visible)
            {
                CancelInlineRename();
                e.Handled = true;
                return;
            }
            if (TryPopPreviewStack())
            {
                e.Handled = true;
                return;
            }
            CloseButton_Click(sender, e);
            return;
        }

        // Arrows are handled by the global hook while preview is open (WebView-safe).
        // Ctrl+/- / Ctrl+0 — also works for PDF via WebView2.ZoomFactor (toolbar mirrors this)
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            e.Handled = true;
            AdjustZoom(0.1);
        }
        else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            e.Handled = true;
            AdjustZoom(-0.1);
        }
        else if (ctrl && (e.Key == Key.D0 || e.Key == Key.NumPad0))
        {
            e.Handled = true;
            ResetZoom();
        }
        else if (ctrl && e.Key == Key.Z)
        {
            e.Handled = true;
            if (_content?.Kind == PreviewKind.Video && _editMode)
                UndoVideoEdit();
        }
        else if (e.Key == Key.Delete && !IsTypingOrEditCapture)
        {
            // Playbook: Press Delete (or the ribbon delete).
            if (_content?.Kind == PreviewKind.Video && _editMode)
            {
                e.Handled = true;
                CutMiddleButton_Click(sender, e);
            }
        }
        else if (e.Key == Key.Home)
        {
            e.Handled = true;
            _ = NavigateExtremeAsync(toEnd: false);
        }
        else if (e.Key == Key.End)
        {
            e.Handled = true;
            _ = NavigateExtremeAsync(toEnd: true);
        }
    }

    /// <summary>True when Space/Esc must not steal keys (chat, WPF text boxes, WebView inputs).</summary>
    public bool IsTypingOrEditCapture
    {
        get
        {
            if (_webViewTyping)
                return true;
            if (ChatPanel.IsKeyboardFocusWithin)
                return true;
            var focused = Keyboard.FocusedElement;
            return focused is System.Windows.Controls.Primitives.TextBoxBase
                   or PasswordBox
                   or RichTextBox;
        }
    }

    private async Task InjectWebViewTypingProbeAsync()
    {
        try
        {
            var core = PreviewWebView.CoreWebView2;
            if (core is null) return;
            await core.ExecuteScriptAsync("""
                (function(){
                  if (window.__netieTypingProbe) return;
                  window.__netieTypingProbe = true;
                  function isTypingTarget(el){
                    if (!el) return false;
                    var tag = (el.tagName || '').toUpperCase();
                    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true;
                    if (el.isContentEditable) return true;
                    return !!(el.closest && el.closest('[contenteditable="true"]'));
                  }
                  function report(){
                    try {
                      var t = isTypingTarget(document.activeElement);
                      chrome.webview.postMessage(JSON.stringify({type:'netieTyping', typing: !!t}));
                    } catch (e) {}
                  }
                  document.addEventListener('focusin', report, true);
                  document.addEventListener('focusout', function(){ setTimeout(report, 0); }, true);
                  report();
                })();
                """);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"WebView typing probe failed: {ex.Message}");
        }
    }

    /// <summary>Called from global hook — works even when WebView2 has focus.</summary>
    public async Task NavigateAdjacentAsync(int delta, uint vk = 0)
    {
        if (_busy || _travelInFlight || string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        // Edit workbench owns keys — no sibling travel (macOS QL vs Acrobat).
        if (_editMode || IsTypingOrEditCapture)
            return;

        var gen = ++_travelGeneration;
        _travelInFlight = true;
        try
        {
            if (vk == 0)
                vk = delta > 0 ? (uint)Win32Api.VkDown : (uint)Win32Api.VkUp;

            string? next = null;
            var shell = _explorer.TryPostArrowToExplorer(_currentFilePath, _ownerHwnd, vk);
            if (shell != IntPtr.Zero)
            {
                for (var i = 0; i < 16; i++)
                {
                    if (gen != _travelGeneration)
                        return;
                    await Task.Delay(30);
                    next = _explorer.ReadSelectionNear(shell, _currentFilePath);
                    if (!string.IsNullOrWhiteSpace(next))
                        break;
                }
            }

            if (gen != _travelGeneration)
                return;

            next ??= _explorer.NavigateAdjacent(_currentFilePath, delta, _ownerHwnd, vk);

            if (string.IsNullOrWhiteSpace(next))
            {
                ConvertStatusText.Text = delta > 0 ? "End of folder" : "Start of folder";
                Activate();
                return;
            }

            ConvertStatusText.Text = IoPath.GetFileName(next);
            Activate();
            ClearPreviewStack();
            await LoadFileAsync(next);
        }
        finally
        {
            if (gen == _travelGeneration)
                _travelInFlight = false;
        }
    }

    /// <summary>Home / End → first / last sibling in folder (Mac QL-ish).</summary>
    public async Task NavigateExtremeAsync(bool toEnd)
    {
        if (_busy || _travelInFlight || string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        if (_editMode || IsTypingOrEditCapture)
            return;

        var gen = ++_travelGeneration;
        _travelInFlight = true;
        try
        {
            var next = _explorer.GetExtremePath(_currentFilePath, toEnd);
            if (string.IsNullOrWhiteSpace(next) ||
                string.Equals(next, _currentFilePath, StringComparison.OrdinalIgnoreCase))
            {
                ConvertStatusText.Text = toEnd ? "End of folder" : "Start of folder";
                Activate();
                return;
            }

            _explorer.TrySelectPath(_ownerHwnd, next);
            ConvertStatusText.Text = IoPath.GetFileName(next);
            Activate();
            ClearPreviewStack();
            await LoadFileAsync(next);
        }
        finally
        {
            if (gen == _travelGeneration)
                _travelInFlight = false;
        }
    }

    private double _lastMaskW = -1;
    private double _lastMaskH = -1;

    private void PreviewWebView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The chat/summary slide moves this edge every frame; rebuild once in the Completed callback.
        if (_chatAnimating)
            return;
        UpdateCornerMasks();
    }

    private void UpdateCornerMasks()
    {
        if (ContentHost is null || CornerMaskLayer is null)
            return;

        var w = ContentHost.ActualWidth;
        var h = ContentHost.ActualHeight;
        if (w < 8 || h < 8)
            return;

        // Two PathGeometry allocations — skip when the well size is unchanged (fires a lot on resize).
        if (Math.Abs(w - _lastMaskW) < 0.5 && Math.Abs(h - _lastMaskH) < 0.5)
            return;
        _lastMaskW = w;
        _lastMaskH = h;

        // Round the bottom corners of the content well (title bar already rounded).
        // Match the well's own bottom radius (22) so the mask fully covers the WebView2 HWND's
        // square corner instead of leaving a 1px crescent that reads as "jagged".
        const double r = 22;
        CornerMaskLayer.Width = w;
        CornerMaskLayer.Height = h;

        // Quarter-disk cutouts: fill the square corner that sits outside the arc
        CornerTL.Data = null; // top is flush under chrome
        CornerTR.Data = null;
        CornerBL.Data = BuildCornerMask(r, flipX: false, flipY: true);
        CornerBR.Data = BuildCornerMask(r, flipX: true, flipY: true);
        System.Windows.Controls.Canvas.SetLeft(CornerBL, 0);
        System.Windows.Controls.Canvas.SetTop(CornerBL, h - r);
        System.Windows.Controls.Canvas.SetLeft(CornerBR, w - r);
        System.Windows.Controls.Canvas.SetTop(CornerBR, h - r);
    }

    private static System.Windows.Media.Geometry BuildCornerMask(double r, bool flipX, bool flipY)
    {
        // Square minus quarter-circle → masks sharp HWND corners
        var geo = new System.Windows.Media.PathGeometry();
        var fig = new System.Windows.Media.PathFigure
        {
            StartPoint = new System.Windows.Point(flipX ? r : 0, flipY ? r : 0),
            IsClosed = true
        };
        fig.Segments.Add(new System.Windows.Media.LineSegment(
            new System.Windows.Point(flipX ? r : 0, flipY ? 0 : r), true));
        fig.Segments.Add(new System.Windows.Media.LineSegment(
            new System.Windows.Point(flipX ? 0 : r, flipY ? 0 : r), true));
        fig.Segments.Add(new System.Windows.Media.ArcSegment(
            new System.Windows.Point(flipX ? r : 0, flipY ? r : 0),
            new System.Windows.Size(r, r),
            0,
            false,
            flipX == flipY
                ? System.Windows.Media.SweepDirection.Clockwise
                : System.Windows.Media.SweepDirection.Counterclockwise,
            true));
        geo.Figures.Add(fig);
        return geo;
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => AdjustZoom(0.15);
    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => AdjustZoom(-0.15);
    private void ZoomFitButton_Click(object sender, RoutedEventArgs e) => FitImageToViewport(resetUserZoom: true);

    private void ImageScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ImageScroll.Visibility != Visibility.Visible)
            return;
        SyncImageCanvasMinSize();
        if (_zoomAtFit)
            FitImageToViewport(resetUserZoom: true);
    }

    private void ImageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            AdjustZoom(e.Delta > 0 ? 0.1 : -0.1);
            return;
        }

        // Two-finger horizontal swipe (Precision Touchpad reports as horizontal wheel)
        if (e.Delta != 0 && Math.Abs(e.Delta) < 120 && Keyboard.Modifiers == ModifierKeys.None)
        {
            // Vertical by default; Shift+wheel or horizontal delta via MouseWheelEventArgs doesn't expose X —
            // use Left/Right tilt via PreviewMouseWheel when Shift held for horizontal pan.
        }

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            ImageScroll.ScrollToHorizontalOffset(ImageScroll.HorizontalOffset - e.Delta);
        }
    }

    private void ImageScroll_ManipulationDelta(object? sender, ManipulationDeltaEventArgs e)
    {
        if (ImageScroll.Visibility != Visibility.Visible)
            return;

        if (Math.Abs(e.DeltaManipulation.Scale.X - 1) > 0.001 ||
            Math.Abs(e.DeltaManipulation.Scale.Y - 1) > 0.001)
        {
            var scale = (e.DeltaManipulation.Scale.X + e.DeltaManipulation.Scale.Y) / 2.0;
            AdjustZoom((scale - 1.0) * _zoom);
            e.Handled = true;
        }

        if (Math.Abs(e.DeltaManipulation.Translation.X) > 0.5 ||
            Math.Abs(e.DeltaManipulation.Translation.Y) > 0.5)
        {
            ImageScroll.ScrollToHorizontalOffset(ImageScroll.HorizontalOffset - e.DeltaManipulation.Translation.X);
            ImageScroll.ScrollToVerticalOffset(ImageScroll.VerticalOffset - e.DeltaManipulation.Translation.Y);
            e.Handled = true;
        }
    }

    private void ImageScroll_ManipulationInertiaStarting(object? sender, ManipulationInertiaStartingEventArgs e)
    {
        e.TranslationBehavior.DesiredDeceleration = 0.001;
        e.ExpansionBehavior.DesiredDeceleration = 0.01;
    }

    private void AdjustZoom(double delta)
    {
        var minZ = Math.Min(0.05, _fitZoom * 0.25);
        var maxZ = Math.Max(4.0, _fitZoom * 8);
        _zoom = Math.Clamp(_zoom + delta, minZ, maxZ);
        _zoomAtFit = Math.Abs(_zoom - _fitZoom) < 0.01;
        ApplyZoom();
    }

    /// <summary>Trackpad pinch / Ctrl+wheel inside the WebView bubbles up here — mirror it on the toolbar.</summary>
    private void PreviewWebView_ZoomFactorChanged(object? sender, EventArgs e)
    {
        // PDF zooms through the pdf.js canvas scale, not the WebView factor — leave its % alone.
        if (_content?.Kind == PreviewKind.Pdf)
            return;
        try
        {
            _zoom = PreviewWebView.ZoomFactor;
            _zoomAtFit = false;
            if (ZoomLabel is not null)
                ZoomLabel.Text = $"{_zoom * 100:0}%";
        }
        catch { /* control tearing down */ }
    }

    private void ResetZoom() => FitImageToViewport(resetUserZoom: true);

    private void FitImageToViewport(bool resetUserZoom)
    {
        if (ImageScroll.Visibility != Visibility.Visible || PreviewImage.Source is null)
        {
            _zoom = 1.0;
            _fitZoom = 1.0;
            _zoomAtFit = true;
            ApplyZoom();
            return;
        }

        SyncImageCanvasMinSize();
        var vw = Math.Max(1, ImageScroll.ViewportWidth - 32);
        var vh = Math.Max(1, ImageScroll.ViewportHeight - 32);
        double iw = PreviewImage.Source.Width;
        double ih = PreviewImage.Source.Height;
        if (PreviewImage.Source is BitmapSource bs)
        {
            iw = bs.PixelWidth;
            ih = bs.PixelHeight;
        }

        if (iw < 1 || ih < 1)
        {
            _fitZoom = 1.0;
        }
        else
        {
            _fitZoom = Math.Clamp(Math.Min(vw / iw, vh / ih), 0.05, 8.0);
        }

        if (resetUserZoom || _zoomAtFit)
        {
            _zoom = _fitZoom;
            _zoomAtFit = true;
        }

        ApplyZoom();
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var contentW = iw * _zoom + 32;
                var contentH = ih * _zoom + 32;
                if (contentW > ImageScroll.ViewportWidth)
                    ImageScroll.ScrollToHorizontalOffset((contentW - ImageScroll.ViewportWidth) / 2);
                else
                    ImageScroll.ScrollToHorizontalOffset(0);
                if (contentH > ImageScroll.ViewportHeight)
                    ImageScroll.ScrollToVerticalOffset((contentH - ImageScroll.ViewportHeight) / 2);
                else
                    ImageScroll.ScrollToVerticalOffset(0);
            }
            catch { /* ignore */ }
        }, DispatcherPriority.Loaded);
    }

    private void SyncImageCanvasMinSize()
    {
        if (ImageCanvas is null || ImageScroll.Visibility != Visibility.Visible)
            return;
        ImageCanvas.MinWidth = Math.Max(0, ImageScroll.ViewportWidth);
        ImageCanvas.MinHeight = Math.Max(0, ImageScroll.ViewportHeight);
    }

    private void ApplyZoom()
    {
        ZoomLabel.Text = $"{_zoom * 100:0}%";

        if (ImageScroll.Visibility == Visibility.Visible)
        {
            ImageScale.ScaleX = _zoom;
            ImageScale.ScaleY = _zoom;
            SyncImageCanvasMinSize();
            return;
        }

        // Covers PDF / Office / HTML / Markdown — PreviewKind.Pdf is in the zoomable set
        if (PreviewWebView.Visibility == Visibility.Visible)
        {
            try
            {
                if (PreviewWebView.CoreWebView2 is not { } core)
                    return;

                // PDF: never stack WebView ZoomFactor on pdf.js canvas scale (OOM / freeze)
                if (_content?.Kind == PreviewKind.Pdf)
                {
                    PreviewWebView.ZoomFactor = 1.0;
                    var scale = Math.Clamp(_zoom * 1.25, 0.4, 4.0);
                    core.PostWebMessageAsJson(
                        System.Text.Json.JsonSerializer.Serialize(new { type = "setZoom", scale }));
                    return;
                }

                PreviewWebView.ZoomFactor = _zoom;
            }
            catch
            {
                // WebView may not be ready
            }
        }
    }

    /// <summary>Push current path and open a nested preview (folder drill-down).</summary>
    public async Task PushPreviewAsync(string childPath)
    {
        if (string.IsNullOrWhiteSpace(childPath) || !ExplorerFileService.PathExists(childPath))
            return;
        if (!string.IsNullOrWhiteSpace(_currentFilePath))
            _previewStack.Push(_currentFilePath);
        ConvertStatusText.Text = $"Opened · Space backs out ({_previewStack.Count} deep)";
        await LoadFileAsync(childPath);
    }

    /// <summary>Pop nested preview. Returns true if a parent was restored; false if stack empty.</summary>
    public bool TryPopPreviewStack()
    {
        if (_previewStack.Count == 0)
            return false;
        var parent = _previewStack.Pop();
        ConvertStatusText.Text = _previewStack.Count > 0
            ? $"Back · {_previewStack.Count} level(s) left"
            : "Back to first preview";
        _ = LoadFileAsync(parent);
        return true;
    }

    public void ClearPreviewStack() => _previewStack.Clear();

    private void ScheduleTextAutosave(string text)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath) || !IoFile.Exists(_currentFilePath))
            return;
        _textAutosaveCts?.Cancel();
        _textAutosaveCts = new CancellationTokenSource();
        var ct = _textAutosaveCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, ct);
                await IoFile.WriteAllTextAsync(_currentFilePath!, text, ct);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        ConvertStatusText.Text = "Saved";
                        if (_content is not null)
                            _content.RawText = text;
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => ConvertStatusText.Text = $"Save failed: {ex.Message}");
            }
        }, ct);
    }

    private static string UnquoteJsString(string jsQuoted)
    {
        if (string.IsNullOrWhiteSpace(jsQuoted) || jsQuoted == "null")
            return string.Empty;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string>(jsQuoted) ?? string.Empty;
        }
        catch
        {
            return jsQuoted.Trim('"');
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }

    private static string FormatTs(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
}
