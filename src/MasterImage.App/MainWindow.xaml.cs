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

    // Decoded-image cache shared across view models (it's keyed by file path, and reopening a
    // folder shouldn't throw away work). Sized to comfortably hold the current photo plus the
    // neighbours we read ahead in both directions.
    private readonly PhotoImageCache _imageCache = new(capacity: 7, decodePixelWidth: 1920);

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
        PreviewKeyUp += MainWindow_PreviewKeyUp;
        TileGridViewControl.SetItems(ViewModel.Photos);
        TileGridViewControl.TileClicked += OnTileClicked;
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

    // Decode the photos on either side of the current one ahead of time. Seeking is the most-used
    // action in a cull and a 40MB camera JPEG takes hundreds of milliseconds to decode, so without
    // this every keypress stalls on the disk. Ordered nearest-first: the very next photo is the one
    // most likely to be needed, and the pipeline's worker limit means order decides what lands first.
    private void PrefetchNeighbours()
    {
        var photos = ViewModel.Photos;
        if (photos.Count <= 1) return;

        foreach (int offset in new[] { 1, -1, 2, -2 })
        {
            int index = ((ViewModel.CurrentIndex + offset) % photos.Count + photos.Count) % photos.Count;
            _imageCache.Prefetch(photos[index]);
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
        NavigationOverlayControl.ShowMessage($"Preloaded {total} thumbnails.");
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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
                ViewModel.IsGridVisible = true;
                GridHost.Visibility = Visibility.Visible;
                SingleImageHost.Visibility = Visibility.Collapsed;
                e.Handled = true;
                break;

            case Key.M:
                ViewModel.ToggleMark();
                if (ViewModel.CurrentPhoto is not null)
                {
                    NavigationOverlayControl.Show(ViewModel.CurrentPhoto, ViewModel.CurrentIndex, ViewModel.Photos.Count, ViewModel.IsCurrentMarked);
                }
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

    // Releasing Shift closes the overview and lands on whichever tile the cursor was over
    // (spec §7 press-and-hold); if the cursor wasn't over a tile, stay on the current photo.
    private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.LeftShift or Key.RightShift))
        {
            return;
        }

        var hovered = TileGridViewControl.GetHoveredItem();
        bool jumped = hovered is not null && TryJumpTo(hovered);

        HideGrid();
        if (jumped)
        {
            _ = LoadCurrentPhotoAsync();
        }
    }

    private void OnTileClicked(PhotoItem item)
    {
        if (!TryJumpTo(item))
        {
            return;
        }

        HideGrid();
        _ = LoadCurrentPhotoAsync();
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

    private void HideGrid()
    {
        ViewModel.IsGridVisible = false;
        GridHost.Visibility = Visibility.Collapsed;
        SingleImageHost.Visibility = Visibility.Visible;
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
