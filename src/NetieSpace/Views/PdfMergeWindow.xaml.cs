using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NetieSpace.Helpers;
using NetieSpace.Services;

namespace NetieSpace.Views;

/// <summary>Reorder files before merge — current PDF first by default. Accepts PDF / images / Office (converted on Merge).</summary>
public partial class PdfMergeWindow : Window
{
    private static readonly HashSet<string> MergeableExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff",
        ".svg", ".ico", ".heic", ".heif",
        ".doc", ".docx", ".odt", ".rtf",
        ".txt", ".text", ".log", ".csv",
        ".json", ".xml", ".cs", ".js", ".ts", ".tsx", ".jsx",
        ".py", ".java", ".cpp", ".c", ".h", ".hpp",
        ".rs", ".go", ".rb", ".php",
        ".yaml", ".yml", ".toml", ".ini", ".cfg",
        ".sql", ".sh", ".ps1", ".bat",
        ".css", ".scss", ".vue", ".swift", ".kt",
        ".md", ".markdown", ".html", ".htm",
        ".pptx", ".ppsx", ".ppt", ".odp", ".xlsx", ".xlsm", ".xls", ".ods"
    };

    private readonly ObservableCollection<MergeRow> _files = new();
    private readonly ConversionService _conversion = new();
    private Point _dragStart;
    private MergeRow? _dragItem;

    public IReadOnlyList<string> OrderedPaths => _files.Select(f => f.Path).ToList();

    /// <summary>Resolved PDF paths after conversion (set when DialogResult=true).</summary>
    public IReadOnlyList<string> ResolvedPdfPaths { get; private set; } = Array.Empty<string>();

    public PdfMergeWindow(string primaryPdf, IEnumerable<string>? extras = null)
    {
        InitializeComponent();
        GlassHelper.ApplyRoundedDialog(this, 0.92, 16);
        FileList.ItemsSource = _files;
        AddPath(primaryPdf, isPrimary: true);
        if (extras is not null)
        {
            foreach (var p in extras)
                AddPath(p, isPrimary: false);
        }
    }

    public static IReadOnlyList<string>? Show(Window owner, string primaryPdf, IEnumerable<string>? extras = null)
    {
        var win = new PdfMergeWindow(primaryPdf, extras) { Owner = owner };
        return win.ShowDialog() == true ? win.ResolvedPdfPaths.ToList() : null;
    }

    private void AddPath(string path, bool isPrimary)
    {
        if (string.IsNullOrWhiteSpace(path) || !IoFile.Exists(path))
            return;
        var ext = IoPath.GetExtension(path);
        if (!MergeableExt.Contains(ext))
            return;
        if (_files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;
        _files.Add(new MergeRow
        {
            Path = path,
            IsPrimary = isPrimary
        });
        RefreshLabels();
    }

    private void AddFolderRecursive(string folder)
    {
        if (!IoDirectory.Exists(folder))
            return;
        foreach (var file in IoDirectory.EnumerateFiles(folder, "*.*", System.IO.SearchOption.AllDirectories)
                     .Where(f => MergeableExt.Contains(IoPath.GetExtension(f)))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                     .Take(80))
            AddPath(file, false);
    }

    private void RefreshLabels()
    {
        for (var i = 0; i < _files.Count; i++)
            _files[i].NotifyOrder(i + 1);
        StatusText.Text = $"{_files.Count} file{(_files.Count == 1 ? "" : "s")} — top = first in merged PDF";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        AnimationHelper.AnimateOpen(RootBorder, 240);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Add files to merge",
            Filter =
                "Documents & images|*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff;" +
                "*.svg;*.ico;*.heic;*.heif;" +
                "*.doc;*.docx;*.odt;*.rtf;*.txt;*.text;*.log;*.csv;" +
                "*.json;*.xml;*.cs;*.js;*.ts;*.tsx;*.jsx;*.py;*.java;*.cpp;*.c;*.h;*.hpp;" +
                "*.rs;*.go;*.rb;*.php;*.yaml;*.yml;*.toml;*.ini;*.cfg;*.sql;*.sh;*.ps1;*.bat;" +
                "*.css;*.scss;*.vue;*.swift;*.kt;*.md;*.markdown;*.html;*.htm;" +
                "*.pptx;*.ppsx;*.ppt;*.odp;*.xlsx;*.xlsm;*.xls;*.ods|" +
                "PDF|*.pdf|Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) != true)
            return;
        foreach (var f in dlg.FileNames)
            AddPath(f, false);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Add folder — PDF, images, and Office files (max 80)"
        };
        if (dlg.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dlg.FolderName))
            return;
        var before = _files.Count;
        AddFolderRecursive(dlg.FolderName);
        StatusText.Text = $"Folder added · +{_files.Count - before} file(s)";
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var i = FileList.SelectedIndex;
        if (i <= 0) return;
        _files.Move(i, i - 1);
        RefreshLabels();
        FileList.SelectedIndex = i - 1;
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var i = FileList.SelectedIndex;
        if (i < 0 || i >= _files.Count - 1) return;
        _files.Move(i, i + 1);
        RefreshLabels();
        FileList.SelectedIndex = i + 1;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not MergeRow row)
            return;
        if (_files.Count <= 1)
        {
            StatusText.Text = "Keep at least one file.";
            return;
        }

        var idx = FileList.SelectedIndex;
        _files.Remove(row);
        RefreshLabels();
        if (_files.Count > 0)
            FileList.SelectedIndex = Math.Min(idx, _files.Count - 1);
    }

    private async void Merge_Click(object sender, RoutedEventArgs e)
    {
        if (_files.Count < 2)
        {
            StatusText.Text = "Add at least one more file to merge.";
            return;
        }

        try
        {
            IsEnabled = false;
            StatusText.Text = "Converting / preparing PDFs…";
            var pdfs = new List<string>();
            foreach (var row in _files)
            {
                var path = row.Path;
                var ext = IoPath.GetExtension(path).ToLowerInvariant();
                if (ext == ".pdf")
                {
                    pdfs.Add(path);
                    continue;
                }

                StatusText.Text = $"Converting {IoPath.GetFileName(path)}…";
                var result = await _conversion.ConvertToPdfAsync(path);
                if (!result.Success || string.IsNullOrWhiteSpace(result.OutputPath))
                {
                    StatusText.Text = result.Error ?? $"Could not convert {IoPath.GetFileName(path)}";
                    IsEnabled = true;
                    return;
                }
                pdfs.Add(result.OutputPath);
            }

            if (pdfs.Count < 2)
            {
                StatusText.Text = "Need at least two PDFs after conversion.";
                IsEnabled = true;
                return;
            }

            ResolvedPdfPaths = pdfs;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            IsEnabled = true;
        }
    }

    private void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null)
            return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumHorizontalDragDistance)
            return;
        DragDrop.DoDragDrop(FileList, _dragItem, DragDropEffects.Move);
    }

    private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = (e.OriginalSource as FrameworkElement)?.DataContext as MergeRow
                    ?? FileList.SelectedItem as MergeRow;
    }

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(MergeRow)) || e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void FileList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            foreach (var p in dropped)
            {
                if (IoDirectory.Exists(p))
                    AddFolderRecursive(p);
                else
                    AddPath(p, false);
            }
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(typeof(MergeRow)) is not MergeRow moved)
            return;
        var target = (e.OriginalSource as FrameworkElement)?.DataContext as MergeRow;
        var from = _files.IndexOf(moved);
        var to = target is null ? _files.Count - 1 : _files.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
            return;
        _files.Move(from, to);
        RefreshLabels();
        FileList.SelectedItem = moved;
    }

    private sealed class MergeRow : INotifyPropertyChanged
    {
        public string Path { get; set; } = "";
        public bool IsPrimary { get; set; }
        public string DisplayTitle { get; private set; } = "";
        public string Badge { get; private set; } = "";
        public ImageSource? Thumb { get; private set; }

        public void NotifyOrder(int oneBased)
        {
            DisplayTitle = $"{oneBased}. {IoPath.GetFileName(Path)}";
            var ext = IoPath.GetExtension(Path).TrimStart('.').ToUpperInvariant();
            Badge = IsPrimary ? "current" : ext;
            try
            {
                Thumb = ShellThumbnail.TryGetBitmapSource(Path, 96);
            }
            catch { Thumb = null; }

            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(Badge));
            OnPropertyChanged(nameof(Thumb));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
