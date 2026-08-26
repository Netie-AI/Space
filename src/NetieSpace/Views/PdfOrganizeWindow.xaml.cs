using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using NetieSpace.Helpers;
using NetieSpace.Services;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace NetieSpace.Views;

public partial class PdfOrganizeWindow : Window
{
    private readonly string _pdfPath;
    private readonly PdfEditService _pdfEdit = new();
    private readonly ConversionService _conversion = new();
    private readonly ObservableCollection<PageRow> _pages = new();
    private Point _dragStart;
    private PageRow? _dragItem;
    private readonly DispatcherTimer _peekTimer;
    private PageRow? _peekRow;

    /// <summary>Output PDF path after Apply, or null if cancelled.</summary>
    public string? ResultPath { get; private set; }

    public PdfOrganizeWindow(string pdfPath)
    {
        InitializeComponent();
        _pdfPath = pdfPath;
        GlassHelper.ApplyRoundedDialog(this, 0.92, 18);
        PageList.ItemsSource = _pages;
        _peekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        _peekTimer.Tick += PeekTimer_Tick;
        LoadPages();
        _ = PrefetchThumbsAsync();
    }

    public static string? Show(Window owner, string pdfPath)
    {
        var win = new PdfOrganizeWindow(pdfPath) { Owner = owner };
        return win.ShowDialog() == true ? win.ResultPath : null;
    }

    private void LoadPages()
    {
        _pages.Clear();
        var count = _pdfEdit.GetPageCount(_pdfPath);
        var rotations = _pdfEdit.GetPageRotations(_pdfPath);
        for (var i = 0; i < count; i++)
        {
            _pages.Add(new PageRow
            {
                SourcePageIndex = i,
                ExtraRotateDegrees = 0,
                ExistingRotate = i < rotations.Length ? rotations[i] : 0,
                IsImageInsert = false,
                SourcePdfPath = _pdfPath
            });
        }

        RefreshLabels();
        StatusText.Text = $"{count} page{(count == 1 ? "" : "s")} · 4-up grid";
        if (_pages.Count > 0)
            PageList.SelectedIndex = 0;
    }

    private async Task PrefetchThumbsAsync()
    {
        List<(int Index, string[] Lines)>? pagesText = null;
        await Task.Run(() =>
        {
            try
            {
                using var doc = PdfPigDocument.Open(_pdfPath);
                pagesText = new List<(int, string[])>(_pages.Count);
                for (var i = 0; i < _pages.Count; i++)
                {
                    var row = _pages[i];
                    if (row.IsImageInsert) continue;
                    var pageNum = row.SourcePageIndex + 1;
                    if (pageNum < 1 || pageNum > doc.NumberOfPages) continue;
                    var page = doc.GetPage(pageNum);
                    var lines = page.GetWords()
                        .GroupBy(wd => Math.Round(wd.BoundingBox.BottomLeft.Y, 0))
                        .OrderByDescending(g => g.Key)
                        .Take(18)
                        .Select(g => string.Join(" ", g.Select(x => x.Text)))
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToArray();
                    pagesText.Add((i, lines));
                }
            }
            catch
            {
                pagesText = null;
            }
        });

        if (pagesText is null) return;

        // Render on UI thread (DrawingVisual / Freezable rules)
        foreach (var (index, lines) in pagesText)
        {
            if (index < 0 || index >= _pages.Count) continue;
            var src = PageRow.RenderLinesPreview(lines, _pages[index].SourcePageIndex + 1, 120, 150);
            if (src is not null)
                _pages[index].SetThumb(src);
            if (index % 12 == 11)
                await Dispatcher.Yield(DispatcherPriority.Background);
        }
    }

    private void RefreshLabels()
    {
        for (var i = 0; i < _pages.Count; i++)
            _pages[i].NotifyPosition(i + 1, _pages.Count);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        AnimationHelper.AnimateOpen(RootBorder, 240);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var i = PageList.SelectedIndex;
        if (i <= 0) return;
        _pages.Move(i, i - 1);
        RefreshLabels();
        PageList.SelectedIndex = i - 1;
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var i = PageList.SelectedIndex;
        if (i < 0 || i >= _pages.Count - 1) return;
        _pages.Move(i, i + 1);
        RefreshLabels();
        PageList.SelectedIndex = i + 1;
    }

    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        if (PageList.SelectedItem is not PageRow row || row.IsImageInsert)
            return;
        row.ExtraRotateDegrees = (row.ExtraRotateDegrees + 90) % 360;
        row.RaiseAll();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (PageList.SelectedItem is not PageRow row)
            return;
        if (_pages.Count <= 1)
        {
            StatusText.Text = "Keep at least one page.";
            return;
        }

        var idx = PageList.SelectedIndex;
        _pages.Remove(row);
        RefreshLabels();
        if (_pages.Count > 0)
            PageList.SelectedIndex = Math.Min(idx, _pages.Count - 1);
        StatusText.Text = $"{_pages.Count} page{(_pages.Count == 1 ? "" : "s")}";
    }

    private async void InsertImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Add files as pages",
            Filter =
                "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff;" +
                "*.svg;*.ico;*.heic;*.heif|" +
                "All files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) != true)
            return;

        var at = PageList.SelectedIndex < 0 ? _pages.Count : PageList.SelectedIndex + 1;
        foreach (var f in dlg.FileNames)
            await InsertConvertibleAt(f, at++);
    }

    private void OpenMerge_Click(object sender, RoutedEventArgs e)
    {
        var ordered = PdfMergeWindow.Show(this, _pdfPath);
        if (ordered is null || ordered.Count < 2)
            return;
        try
        {
            ResultPath = _pdfEdit.MergeOrdered(ordered);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    /// <summary>
    /// Engine-first images. PdfSharp XImage cannot decode these; Organize
    /// rasterizes to PNG then uses <see cref="IsImagePath"/>.
    /// </summary>
    public static bool IsEngineImagePath(string path)
    {
        var ext = IoPath.GetExtension(path).ToLowerInvariant();
        return ext is ".webp" or ".tif" or ".tiff" or ".svg" or ".ico" or ".heic" or ".heif";
    }

    private async Task InsertConvertibleAt(string path, int insertIndex)
    {
        if (IsImagePath(path) && IoFile.Exists(path))
        {
            InsertImageAt(path, insertIndex);
            return;
        }

        if (!IsEngineImagePath(path) || !IoFile.Exists(path))
        {
            StatusText.Text = "Not a supported image file.";
            return;
        }

        if (!_conversion.IsAvailable)
        {
            StatusText.Text = "Convert engine missing — cannot add this image type.";
            return;
        }

        StatusText.Text = $"Converting {IoPath.GetFileName(path)}…";
        var dest = IoPath.Combine(IoPath.GetTempPath(), $"netie_org_{Guid.NewGuid():N}.png");
        var result = await _conversion.ExportAsAsync(path, dest);
        if (!result.Success || !IoFile.Exists(dest))
        {
            StatusText.Text = result.Error ?? "Could not convert image.";
            return;
        }

        InsertImageAt(dest, insertIndex);
    }

    private void InsertImageAt(string imagePath, int insertIndex)
    {
        if (!IsImagePath(imagePath) || !IoFile.Exists(imagePath))
        {
            StatusText.Text = "Not a supported image file.";
            return;
        }

        insertIndex = Math.Clamp(insertIndex, 0, _pages.Count);
        _pages.Insert(insertIndex, new PageRow
        {
            SourcePageIndex = -1,
            ExtraRotateDegrees = 0,
            ExistingRotate = 0,
            IsImageInsert = true,
            ImagePath = imagePath
        });
        RefreshLabels();
        PageList.SelectedIndex = insertIndex;
        StatusText.Text = $"Image queued · {IoPath.GetFileName(imagePath)}";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_pages.Count == 0)
        {
            StatusText.Text = "Keep at least one page.";
            return;
        }

        try
        {
            var slots = _pages.Select(p => p.IsImageInsert
                ? new PdfComposeSlot { IsImage = true, ImagePath = p.ImagePath }
                : new PdfComposeSlot
                {
                    IsImage = false,
                    SourcePageIndex = p.SourcePageIndex,
                    ExtraRotateDegrees = p.ExtraRotateDegrees
                }).ToList();

            ResultPath = _pdfEdit.ComposePages(_pdfPath, slots);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasImageFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetImagePaths(e, out var paths))
            return;
        var at = PageList.SelectedIndex < 0 ? _pages.Count : PageList.SelectedIndex + 1;
        foreach (var path in paths)
            await InsertConvertibleAt(path, at++);
        e.Handled = true;
    }

    private void PageList_DragOver(object sender, DragEventArgs e)
    {
        if (HasImageFiles(e))
        {
            e.Effects = DragDropEffects.Copy;
            ShowDropPreview(e.GetPosition(PageList));
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(typeof(PageRow)))
        {
            e.Effects = DragDropEffects.Move;
            ShowDropPreview(e.GetPosition(PageList));
            e.Handled = true;
        }
    }

    private void PageList_DragLeave(object sender, DragEventArgs e) => HideDropPreview();

    private void PageList_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        // Keep a visible drag affordance — default cursors + our slot ghost
        e.UseDefaultCursors = true;
        e.Handled = false;
    }

    private void ShowDropPreview(Point pos)
    {
        var idx = GetDropIndex(pos);
        StatusText.Text = idx >= _pages.Count
            ? "Release → add at end"
            : $"Release → insert before #{idx + 1}";

        if (DropInsertLine is null || DropGhost is null) return;

        // Place indicator at approximate tile slot
        const double tileW = 168, tileH = 210, pad = 10;
        var cols = Math.Max(1, (int)((PageList.ActualWidth - pad * 2) / tileW));
        var row = idx / cols;
        var col = idx % cols;
        var x = pad + col * tileW + 4;
        var y = pad + row * tileH + 4;

        DropInsertLine.Visibility = Visibility.Visible;
        DropInsertLine.Margin = new Thickness(Math.Max(0, x - 2), y, 0, 0);
        DropGhost.Visibility = Visibility.Visible;
        DropGhost.Margin = new Thickness(x, y, 0, 0);
    }

    private void HideDropPreview()
    {
        if (DropInsertLine is not null) DropInsertLine.Visibility = Visibility.Collapsed;
        if (DropGhost is not null) DropGhost.Visibility = Visibility.Collapsed;
    }

    private async void PageList_Drop(object sender, DragEventArgs e)
    {
        HideDropPreview();
        if (TryGetImagePaths(e, out var paths))
        {
            var target = GetDropIndex(e.GetPosition(PageList));
            foreach (var path in paths)
                await InsertConvertibleAt(path, target++);
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(typeof(PageRow)) && e.Data.GetData(typeof(PageRow)) is PageRow dragged)
        {
            var targetIndex = GetDropIndex(e.GetPosition(PageList));
            var oldIndex = _pages.IndexOf(dragged);
            if (oldIndex < 0) return;
            if (targetIndex > oldIndex) targetIndex--;
            targetIndex = Math.Clamp(targetIndex, 0, _pages.Count - 1);
            if (oldIndex != targetIndex)
            {
                _pages.Move(oldIndex, targetIndex);
                RefreshLabels();
                PageList.SelectedIndex = targetIndex;
                StatusText.Text = $"Moved → slot #{targetIndex + 1}";
            }

            e.Handled = true;
        }
    }

    private void PageList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _peekTimer.Stop();
        PeekPopup.IsOpen = false;
        _dragStart = e.GetPosition(null);
        _dragItem = null;
        _peekRow = null;

        var item = FindRowUnder(e.GetPosition(PageList));
        if (item is null) return;
        PageList.SelectedItem = item;
        _dragItem = item;
        _peekRow = item;
        _peekTimer.Start();
    }

    private void PageList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _peekTimer.Stop();
        PeekPopup.IsOpen = false;
        _dragItem = null;
    }

    private void PageList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null)
            return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _peekTimer.Stop();
        PeekPopup.IsOpen = false;
        var item = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop(PageList, item, DragDropEffects.Move);
    }

    private void PeekTimer_Tick(object? sender, EventArgs e)
    {
        _peekTimer.Stop();
        if (_peekRow?.Thumb is null) return;
        PeekImage.Source = _peekRow.Thumb;
        PeekPopup.IsOpen = true;
    }

    private PageRow? FindRowUnder(Point pos)
    {
        var hit = VisualTreeHelper.HitTest(PageList, pos);
        DependencyObject? cur = hit?.VisualHit;
        while (cur is not null && cur is not ListBoxItem)
            cur = VisualTreeHelper.GetParent(cur);
        return cur is ListBoxItem { DataContext: PageRow row } ? row : null;
    }

    private int GetDropIndex(Point pos)
    {
        // Grid drop: find nearest cell center
        var best = _pages.Count;
        var bestDist = double.MaxValue;
        for (var i = 0; i < _pages.Count; i++)
        {
            if (PageList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item)
                continue;
            var topLeft = item.TranslatePoint(new Point(0, 0), PageList);
            var cx = topLeft.X + item.ActualWidth / 2;
            var cy = topLeft.Y + item.ActualHeight / 2;
            var d = Math.Sqrt((pos.X - cx) * (pos.X - cx) + (pos.Y - cy) * (pos.Y - cy));
            if (d < bestDist)
            {
                bestDist = d;
                // Insert before if left/above of center
                best = (pos.X < cx || pos.Y < cy - item.ActualHeight * 0.15) ? i : i + 1;
            }
        }

        return Math.Clamp(best, 0, _pages.Count);
    }

    private static bool HasImageFiles(DragEventArgs e) =>
        TryGetImagePaths(e, out var paths) && paths.Count > 0;

    private static bool TryGetImagePaths(DragEventArgs e, out List<string> paths)
    {
        paths = new List<string>();
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return false;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return false;
        paths = files.Where(p => IsImagePath(p) || IsEngineImagePath(p)).Where(IoFile.Exists).ToList();
        return paths.Count > 0;
    }

    public static bool IsImagePath(string path)
    {
        var ext = IoPath.GetExtension(path).ToLowerInvariant();
        // PdfSharpCore XImage decoders: BMP / JPEG / GIF / PNG only.
        // .webp / .tif go through the engine to-pdf path (drop / merge).
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";
    }

    private sealed class PageRow : INotifyPropertyChanged
    {
        public int SourcePageIndex { get; init; }
        public int ExtraRotateDegrees { get; set; }
        public int ExistingRotate { get; init; }
        public bool IsImageInsert { get; init; }
        public string? ImagePath { get; init; }
        public string? SourcePdfPath { get; init; }

        public string DisplayTitle { get; private set; } = "";
        public string RotateLabel { get; private set; } = "";
        public string KindLabel { get; private set; } = "";
        public string SlotBadge { get; private set; } = "";
        public ImageSource? Thumb { get; private set; }
        public string ThumbLabel { get; private set; } = "";
        public Visibility ThumbLabelVisibility { get; private set; } = Visibility.Visible;

        public void SetThumb(ImageSource src)
        {
            Thumb = src;
            ThumbLabel = "";
            ThumbLabelVisibility = Visibility.Collapsed;
            OnPropertyChanged(nameof(Thumb));
            OnPropertyChanged(nameof(ThumbLabel));
            OnPropertyChanged(nameof(ThumbLabelVisibility));
        }

        public void NotifyPosition(int oneBased, int total)
        {
            SlotBadge = $"#{oneBased}";
            if (IsImageInsert)
            {
                DisplayTitle = $"New image · slot {oneBased}/{total}";
                KindLabel = IoPath.GetFileName(ImagePath) ?? "image";
                RotateLabel = IoPath.GetFileName(ImagePath) ?? "image";
                ThumbLabel = "";
                ThumbLabelVisibility = Visibility.Collapsed;
                try
                {
                    if (!string.IsNullOrWhiteSpace(ImagePath) && IoFile.Exists(ImagePath))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(ImagePath);
                        bmp.DecodePixelWidth = 120;
                        bmp.EndInit();
                        bmp.Freeze();
                        Thumb = bmp;
                    }
                }
                catch { Thumb = null; }
            }
            else
            {
                DisplayTitle = $"Page {SourcePageIndex + 1} → #{oneBased}";
                KindLabel = $"of {total}";
                var totalRot = (ExistingRotate + ExtraRotateDegrees) % 360;
                RotateLabel = ExtraRotateDegrees == 0
                    ? $"p.{SourcePageIndex + 1}"
                    : $"p.{SourcePageIndex + 1} ↻{totalRot}°";
                if (Thumb is null)
                {
                    Thumb = BuildFallbackThumb(SourcePageIndex + 1, totalRot, SourcePdfPath);
                    ThumbLabel = Thumb is null ? $"{SourcePageIndex + 1}" : "";
                    ThumbLabelVisibility = Thumb is null ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            RaiseAll();
        }

        public static ImageSource? RenderLinesPreview(IReadOnlyList<string> lines, int pageNumber, int w, int h)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x5E, 0xC4, 0xB8)), 1),
                    new Rect(0.5, 0.5, w - 1, h - 1));

                var y = 8.0;
                foreach (var line in lines)
                {
                    var clip = line.Length > 28 ? line[..28] + "…" : line;
                    var tf = new FormattedText(
                        clip,
                        System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),
                        7.5,
                        Brushes.Black,
                        1.25);
                    dc.DrawText(tf, new Point(6, y));
                    y += 8.2;
                    if (y > h - 22) break;
                }

                if (lines.Count == 0)
                {
                    var num = new FormattedText(
                        $"{pageNumber}",
                        System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI Semibold"),
                        28,
                        new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x50)),
                        1.25);
                    dc.DrawText(num, new Point((w - num.Width) / 2, (h - num.Height) / 2 - 6));
                }
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private static ImageSource? BuildFallbackThumb(int pageNumber, int rotateDegrees, string? pdfPath)
        {
            ImageSource? shell = null;
            if (!string.IsNullOrWhiteSpace(pdfPath) && IoFile.Exists(pdfPath) && pageNumber == 1)
                shell = ShellThumbnail.TryGetBitmapSource(pdfPath, 128);

            const int w = 120, h = 150;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1A, 0x1E, 0x28)),
                    new Pen(new SolidColorBrush(Color.FromArgb(0x88, 0x5E, 0xC4, 0xB8)), 1.5),
                    new Rect(0.5, 0.5, w - 1, h - 1));
                if (shell is not null)
                    dc.DrawImage(shell, new Rect(2, 2, w - 4, h - 4));
                var tf = new FormattedText(
                    $"{pageNumber}",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    shell is null ? 28 : 14,
                    new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8)),
                    1.25);
                dc.DrawText(tf, new Point((w - tf.Width) / 2,
                    shell is null ? (h - tf.Height) / 2 : h - 28));
                if (rotateDegrees != 0)
                {
                    var rot = new FormattedText(
                        $"↻{rotateDegrees}°",
                        System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),
                        11,
                        new SolidColorBrush(Color.FromRgb(0x5E, 0xC4, 0xB8)),
                        1.25);
                    dc.DrawText(rot, new Point((w - rot.Width) / 2, 6));
                }
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        public void RaiseAll()
        {
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(RotateLabel));
            OnPropertyChanged(nameof(KindLabel));
            OnPropertyChanged(nameof(SlotBadge));
            OnPropertyChanged(nameof(ExtraRotateDegrees));
            OnPropertyChanged(nameof(Thumb));
            OnPropertyChanged(nameof(ThumbLabel));
            OnPropertyChanged(nameof(ThumbLabelVisibility));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
