using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using MasterImage.Core;

namespace MasterImage.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly MarksStore _marksStore;
    private readonly ThumbnailCache _thumbnailCache;
    private readonly ThumbnailPipeline _thumbnailPipeline;

    private IReadOnlyList<PhotoItem> _photos;
    private int _currentIndex;
    private bool _isGridVisible;
    private bool _isFullscreen;
    private bool _isShortcutsOverlayVisible;
    private double _tileSize = 200;

    public MainViewModel(string folderPath, string? initialFilePath)
    {
        FolderPath = folderPath;
        _photos = PhotoSet.Load(folderPath);
        _thumbnailCache = new ThumbnailCache(folderPath);
        _thumbnailPipeline = new ThumbnailPipeline(_thumbnailCache);
        _marksStore = MarksStore.LoadOrCreate(_thumbnailCache.ThumbnailsFolder);

        _currentIndex = 0;
        if (initialFilePath is not null)
        {
            int index = _photos.ToList().FindIndex(p =>
                p.PrimaryFilePath.Equals(initialFilePath, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _currentIndex = index;
            }
        }
    }

    public string FolderPath { get; }
    public IReadOnlyList<PhotoItem> Photos => _photos;
    public int CurrentIndex => _currentIndex;
    public PhotoItem? CurrentPhoto => _photos.Count > 0 ? _photos[_currentIndex] : null;
    public bool IsCurrentMarked => CurrentPhoto is not null && _marksStore.IsMarked(MarkKey(CurrentPhoto));

    public bool IsMarked(PhotoItem item) => _marksStore.IsMarked(MarkKey(item));

    // Keyed by full filename (with extension), not Stem: PhotoSet.Load currently emits
    // one PhotoItem per file, so two files that happen to share a stem across different
    // extensions (e.g. sunset.jpg and sunset.png) would collide on a bare-Stem key and
    // incorrectly share mark state. Filename-with-extension is unique per PhotoItem today,
    // and stays unique once RAW+JPEG pairing groups files under one PhotoItem (Plan 2) since
    // there's still exactly one PrimaryFilePath per PhotoItem.
    private static string MarkKey(PhotoItem item) => Path.GetFileName(item.PrimaryFilePath);

    public bool IsGridVisible
    {
        get => _isGridVisible;
        set { _isGridVisible = value; OnPropertyChanged(); }
    }

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set { _isFullscreen = value; OnPropertyChanged(); }
    }

    public bool IsShortcutsOverlayVisible
    {
        get => _isShortcutsOverlayVisible;
        set { _isShortcutsOverlayVisible = value; OnPropertyChanged(); }
    }

    public double TileSize
    {
        get => _tileSize;
        set { _tileSize = Math.Clamp(value, 80, 480); OnPropertyChanged(); }
    }

    public void SeekNext()
    {
        if (_photos.Count == 0) return;
        SetCurrentIndex((_currentIndex + 1) % _photos.Count);
    }

    public void SeekPrevious()
    {
        if (_photos.Count == 0) return;
        SetCurrentIndex((_currentIndex - 1 + _photos.Count) % _photos.Count);
    }

    public void JumpTo(int index)
    {
        if (index < 0 || index >= _photos.Count) return;
        SetCurrentIndex(index);
    }

    public void ToggleMark()
    {
        if (CurrentPhoto is null) return;
        _marksStore.Toggle(MarkKey(CurrentPhoto));
        _marksStore.Save();
        OnPropertyChanged(nameof(IsCurrentMarked));
    }

    public CullOperations.MoveResult MoveMarkedToSelected()
    {
        var marked = _photos.Where(p => _marksStore.IsMarked(MarkKey(p))).ToList();
        var result = CullOperations.MoveMarkedToSelectedFolder(FolderPath, marked);

        foreach (var item in marked)
        {
            // Only clear the mark for items that actually moved. A failed move (name collision in
            // selected/, or a locked file — both surfaced in result.Failures) leaves the photo
            // right where it was; keeping its mark lets the photographer resolve the conflict and
            // press N again, instead of silently losing that pick from their selection.
            if (item.FilePaths.All(p => !File.Exists(p)))
            {
                _marksStore.Toggle(MarkKey(item));
            }
        }
        _marksStore.Save();

        _photos = PhotoSet.Load(FolderPath);
        _thumbnailCache.PruneOrphans(_photos);
        SetCurrentIndex(Math.Clamp(_currentIndex, 0, Math.Max(0, _photos.Count - 1)));
        OnPropertyChanged(nameof(Photos));

        return result;
    }

    public Task<BitmapSource?> GetThumbnailAsync(PhotoItem item) => _thumbnailPipeline.RequestThumbnailAsync(item);

    public Task PreloadAllAsync(IProgress<int>? progress = null) => _thumbnailPipeline.PreloadAllAsync(_photos, progress);

    private void SetCurrentIndex(int index)
    {
        _currentIndex = index;
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(CurrentPhoto));
        OnPropertyChanged(nameof(IsCurrentMarked));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
