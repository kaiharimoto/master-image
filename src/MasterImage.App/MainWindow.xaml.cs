using System.IO;
using System.Windows;
using System.Windows.Input;
using MasterImage.App.ViewModels;
using MasterImage.Core;

namespace MasterImage.App;

public partial class MainWindow : Window
{
    private WindowState _preFullscreenState;
    private ResizeMode _preFullscreenResizeMode;

    // How far ahead of / behind the current photo to decode in advance. Forward-biased because a
    // cull runs forwards; deep enough that you'd have to sustain roughly a photo every 50ms to
    // outrun it. Cheap to overshoot — the cache dedupes, and anything already decoded costs nothing.
    private const int ReadAheadForward = 20;
    private const int ReadAheadBackward = 8;

    // How much 1 / 2 move the tile size per press.
    private const double TileSizeStep = 40;

    // Decoded-image cache shared across view models (keyed by file path, so reopening a folder
    // keeps its work). Capacity defaults to a share of this machine's RAM — on a well-specced box
    // that's enough to hold an entire shoot, making every revisit instant.
    private readonly PhotoImageCache _imageCache = new(decodePixelWidth: 1920);

    // Incremented on every load so a slow decode from an abandoned seek can't overwrite the photo
    // the user has since moved to.
    private int _loadGeneration;

    public MainViewModel ViewModel { get; private set; }

    public MainWindow(string? requestedPath)
    {
        InitializeComponent();

        var (folder, file) = ResolveFolderAndFile(requestedPath);
        ViewModel = new MainViewModel(folder, file);
        DataContext = ViewModel;
        _ = LoadCurrentPhotoAsync();

        App.OpenRequested += OnOpenRequestedFromAnotherProcess;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        TileGridViewControl.SetItems(ViewModel.Photos);
        TileGridViewControl.TileClicked += OnTileClicked;
        SingleImageViewControl.WindowDragRequested += OnWindowDragRequested;
        SingleImageViewControl.MaximiseToggleRequested += OnMaximiseToggleRequested;
    }

    private static (string Folder, string? File) ResolveFolderAndFile(string? requestedPath)
    {
        if (requestedPath is null)
        {
            return (Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), null);
        }

        if (Directory.Exists(requestedPath))
        {
            return (requestedPath, null);
        }

        return (Path.GetDirectoryName(requestedPath) ?? ".", requestedPath);
    }

    private void OnOpenRequestedFromAnotherProcess(string path)
    {
        var (folder, file) = ResolveFolderAndFile(path);
        ViewModel = new MainViewModel(folder, file);
        DataContext = ViewModel;
        _ = LoadCurrentPhotoAsync();
        Activate();
        RefreshGridItems();
    }

    private async Task LoadCurrentPhotoAsync()
    {
        var photo = ViewModel.CurrentPhoto;
        if (photo is null)
        {
            SingleImageViewControl.SetImage(null);
            return;
        }

        int generation = ++_loadGeneration;

        var decode = _imageCache.GetAsync(photo);

        // Nothing to wait for when it's already decoded (the common case once read-ahead is
        // warm) — skip the await so the photo swaps within this same input event rather than
        // after a dispatcher round-trip.
        var image = decode.IsCompletedSuccessfully ? decode.Result : await decode;

        // A newer seek started while this was decoding; that one owns the screen now.
        if (generation != _loadGeneration) return;

        SingleImageViewControl.SetImage(image);
        NavigationOverlayControl.Show(photo, ViewModel.CurrentIndex, ViewModel.Photos.Count, ViewModel.IsCurrentMarked);

        PrefetchNeighbours();
    }

    // Decode the photos around the current one ahead of time. Seeking is the most-used action in a
    // cull and a 40MB camera JPEG takes ~1s to decode, so without this every keypress stalls on the
    // disk. Issued nearest-first and interleaved forward/back: decodes are slot-limited, so the
    // order requests go in decides what finishes first, and the next photo matters most.
    private void PrefetchNeighbours()
    {
        var photos = ViewModel.Photos;
        if (photos.Count <= 1) return;

        foreach (int offset in ReadAheadOffsets())
        {
            int index = ((ViewModel.CurrentIndex + offset) % photos.Count + photos.Count) % photos.Count;
            _imageCache.Prefetch(photos[index]);
        }
    }

    private static IEnumerable<int> ReadAheadOffsets()
    {
        for (int distance = 1; distance <= Math.Max(ReadAheadForward, ReadAheadBackward); distance++)
        {
            if (distance <= ReadAheadForward) yield return distance;
            if (distance <= ReadAheadBackward) yield return -distance;
        }
    }

    private async Task HandleCullMoveAsync()
    {
        var result = ViewModel.MoveMarkedToSelected();
        RefreshGridItems(); // Photos was just reassigned — the grid's ItemsSource would otherwise
                            // still list the photos that just moved into selected/.
        await LoadCurrentPhotoAsync();

        // Shown last, and only after the await: LoadCurrentPhotoAsync ends by calling
        // NavigationOverlayControl.Show(...) for the next photo, which would otherwise race with
        // and overwrite this summary.
        string cullMessage = $"Moved {result.MovedFileCount} file(s) to selected/." +
            (result.Failures.Count > 0 ? $" {result.Failures.Count} failed (already existed in selected/)." : "");
        NavigationOverlayControl.ShowMessage(cullMessage);
    }

    private async Task HandlePreloadAllAsync()
    {
        int total = ViewModel.Photos.Count;
        if (total == 0)
        {
            NavigationOverlayControl.ShowMessage("Nothing to preload — no photos in this folder.");
            return;
        }

        // Without a readout, L looks like it does nothing: on a big shoot the work takes tens of
        // seconds with no other visible effect (thumbnails only surface later, in the grid).
        NavigationOverlayControl.ShowSticky($"Preloading 0/{total}…");

        // Progress<T> marshals every report onto the UI thread, so on a folder of thousands that's
        // thousands of callbacks — repaint every 10th (and the last) rather than each one.
        var progress = new Progress<int>(done =>
        {
            if (done % 10 == 0 || done == total)
            {
                NavigationOverlayControl.ShowSticky($"Preloading {done}/{total}…");
            }
        });

        await ViewModel.PreloadAllAsync(progress);

        // "Preload all the images and generate thumbnails" — the thumbnails above are what the grid
        // needs; this warms the full-size decodes the single-image view needs, so seeking anywhere
        // in the folder is instant afterwards rather than only near where you've already been.
        // Capped at the cache's capacity: queuing more than it can hold would evict the earliest
        // work before it was ever used, spending a second per photo for nothing.
        int warmed = 0;
        foreach (var photo in ViewModel.Photos.Take(_imageCache.Capacity))
        {
            _imageCache.Prefetch(photo);
            warmed++;
        }

        string cached = warmed < total ? $" {warmed} kept in memory." : " All kept in memory.";
        NavigationOverlayControl.ShowMessage($"Preloaded {total} thumbnails.{cached}");
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // The grid owns the arrow keys while it's open so you can browse it — the ListBox works out
        // up/down across wrapped rows itself. Everything else below behaves the same in both modes.
        if (ViewModel.IsGridVisible && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F:
                ToggleFullscreen();
                e.Handled = true;
                break;

            case Key.Escape:
                if (ViewModel.IsShortcutsOverlayVisible)
                {
                    ViewModel.IsShortcutsOverlayVisible = false;
                }
                else if (ViewModel.IsGridVisible)
                {
                    HideGrid();
                }
                else if (ViewModel.IsFullscreen)
                {
                    ToggleFullscreen();
                }
                e.Handled = true;
                break;

            case Key.Right:
                ViewModel.SeekNext();
                _ = LoadCurrentPhotoAsync();
                e.Handled = true;
                break;

            case Key.Left:
                ViewModel.SeekPrevious();
                _ = LoadCurrentPhotoAsync();
                e.Handled = true;
                break;

            case Key.LeftShift:
            case Key.RightShift:
                ToggleGrid();
                e.Handled = true;
                break;

            // Open whichever tile you've browsed to.
            case Key.Enter:
                if (ViewModel.IsGridVisible)
                {
                    OpenTile(TileGridViewControl.SelectedItem);
                    e.Handled = true;
                }
                break;

            case Key.D1:
            case Key.NumPad1:
                ViewModel.TileSize -= TileSizeStep;
                e.Handled = true;
                break;

            case Key.D2:
            case Key.NumPad2:
                ViewModel.TileSize += TileSizeStep;
                e.Handled = true;
                break;

            case Key.D3:
            case Key.NumPad3:
                ViewModel.TileSize = MainViewModel.DefaultTileSize;
                e.Handled = true;
                break;

            case Key.M:
                MarkActivePhoto();
                e.Handled = true;
                break;

            case Key.N:
                _ = HandleCullMoveAsync();
                e.Handled = true;
                break;

            case Key.L:
                _ = HandlePreloadAllAsync();
                e.Handled = true;
                break;

            case Key.I:
                ViewModel.IsShortcutsOverlayVisible = !ViewModel.IsShortcutsOverlayVisible;
                e.Handled = true;
                break;
        }
    }

    private void RefreshGridItems() => TileGridViewControl.SetItems(ViewModel.Photos);

    private void ToggleGrid()
    {
        if (ViewModel.IsGridVisible)
        {
            HideGrid();
        }
        else
        {
            ShowGrid();
        }
    }

    private void ShowGrid()
    {
        ViewModel.IsGridVisible = true;
        GridHost.Visibility = Visibility.Visible;
        SingleImageHost.Visibility = Visibility.Collapsed;

        // Start browsing from where you already are, and hand the grid keyboard focus so the
        // arrow keys drive it rather than seeking the single-image view behind it.
        TileGridViewControl.SelectAndFocus(ViewModel.CurrentPhoto);
    }

    private void HideGrid()
    {
        ViewModel.IsGridVisible = false;
        GridHost.Visibility = Visibility.Collapsed;
        SingleImageHost.Visibility = Visibility.Visible;
        Focus();
    }

    private void OnTileClicked(PhotoItem item) => OpenTile(item);

    // Leave the grid on a specific photo — from a click or from Enter on the browsed selection.
    private void OpenTile(PhotoItem? item)
    {
        if (item is null || !TryJumpTo(item))
        {
            return;
        }

        HideGrid();
        _ = LoadCurrentPhotoAsync();
    }

    // M means "mark what I'm looking at": the browsed tile when the grid is open, otherwise the
    // photo on screen. Without this distinction, marking from the grid would silently mark
    // whatever the single-image view happened to be showing behind it.
    private void MarkActivePhoto()
    {
        if (ViewModel.IsGridVisible)
        {
            var selected = TileGridViewControl.SelectedItem;
            if (selected is null) return;

            ViewModel.ToggleMark(selected);
            TileGridViewControl.RefreshMarkBadgeFor(selected);
            return;
        }

        ViewModel.ToggleMark();
        if (ViewModel.CurrentPhoto is not null)
        {
            NavigationOverlayControl.Show(ViewModel.CurrentPhoto, ViewModel.CurrentIndex, ViewModel.Photos.Count, ViewModel.IsCurrentMarked);
        }
    }

    private bool TryJumpTo(PhotoItem item)
    {
        int index = ViewModel.Photos.ToList().IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        ViewModel.JumpTo(index);
        return true;
    }

    private void OnWindowDragRequested(object? sender, EventArgs e)
    {
        // Only valid while the mouse button is genuinely down; a stray call throws.
        if (Mouse.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMaximiseToggleRequested(object? sender, EventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void ToggleFullscreen()
    {
        if (!ViewModel.IsFullscreen)
        {
            _preFullscreenState = WindowState;
            _preFullscreenResizeMode = ResizeMode;

            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
            Topmost = true;
            ViewModel.IsFullscreen = true;
        }
        else
        {
            Topmost = false;
            WindowState = _preFullscreenState;
            ResizeMode = _preFullscreenResizeMode;
            ViewModel.IsFullscreen = false;
        }
    }
}
