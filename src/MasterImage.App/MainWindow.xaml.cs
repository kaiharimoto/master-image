using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MasterImage.App.ViewModels;
using MasterImage.Core;

namespace MasterImage.App;

public partial class MainWindow : Window
{
    private WindowState _preFullscreenState;
    private ResizeMode _preFullscreenResizeMode;

    // Null when fullscreen was entered from a maximised window — there's no explicit size to put
    // back in that case, restoring WindowState is enough.
    private Rect? _preFullscreenBounds;

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

    private readonly AppUpdater _updater = new();

    // Non-null exactly while compare mode is up; it owns the two pane indices and which one the
    // arrows drive. ViewModel.IsCompareVisible mirrors it for the rest of the window to read.
    private CompareState? _compareState;

    private readonly EscapeQuitCounter _escapeCounter = new();

    // Set once an update has been found and announced, so the second U installs rather than
    // re-checking. Deliberate: a stray U mid-cull must not be able to restart the app.
    private bool _updateOffered;

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

        // The panes were indexing into the old folder's photo list, which no longer exists. Being
        // asked to open a specific file also means showing that file, not a stale side-by-side.
        _compareState = null;
        CompareHost.Visibility = Visibility.Collapsed;
        SingleImageHost.Visibility = Visibility.Visible;

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

        if (image is null)
        {
            NavigationOverlayControl.ShowMessage(DescribeLoadFailure(photo));
            return;
        }

        NavigationOverlayControl.Show(photo, ViewModel.CurrentIndex, ViewModel.Photos.Count, ViewModel.IsCurrentMarked);

        PrefetchNeighbours();
        _ = RefreshPeekAsync(generation);
    }

    // The P peek. Takes the load generation of the seek that asked for it: thumbnails resolve
    // asynchronously, so without this a slow thumbnail from a photo you've already moved past could
    // land in the corner after the fact — the same race the main image uses the generation to avoid.
    private async Task RefreshPeekAsync(int generation)
    {
        if (!ShouldDrawPeek())
        {
            PeekOverlayControl.Clear();
            return;
        }

        var neighbours = ViewModel.GetNeighbours();
        if (neighbours.Previous is null || neighbours.Next is null)
        {
            PeekOverlayControl.Clear();
            return;
        }

        var previous = await ViewModel.GetThumbnailAsync(neighbours.Previous);
        var next = await ViewModel.GetThumbnailAsync(neighbours.Next);

        if (generation != _loadGeneration || !ShouldDrawPeek()) return;

        PeekOverlayControl.Show(previous, next);
    }

    // P is on, and nothing that supersedes it is up. The grid is nothing but neighbours, and
    // compare is already showing two photos — a peek in either would be noise.
    private bool ShouldDrawPeek() =>
        ViewModel.IsPeekEnabled && !ViewModel.IsGridVisible && _compareState is null;

    private void TogglePeek()
    {
        ViewModel.IsPeekEnabled = !ViewModel.IsPeekEnabled;
        _ = RefreshPeekAsync(_loadGeneration);

        // Says so out loud when it can't be shown, rather than looking like a dead key.
        if (ViewModel.IsPeekEnabled && !ShouldDrawPeek())
        {
            NavigationOverlayControl.ShowMessage("Peek is on — it shows in the single-photo view.");
        }
    }

    // A RAW that won't open on a machine with no RAW codec is the one failure with a fix the user
    // can act on, so name it. Anything else is genuinely just a bad file.
    private static string DescribeLoadFailure(PhotoItem photo)
    {
        string name = Path.GetFileName(photo.PrimaryFilePath);

        if (RawFormats.IsRaw(photo.PrimaryFilePath) && !ImageLoader.IsRawDecodingAvailable())
        {
            return $"Can't open {name} — RAW support needs the free \"Raw Image Extension\" from the Microsoft Store.";
        }

        return $"Can't open {name}.";
    }

    // Decode the photos around the current one ahead of time. Seeking is the most-used action in a
    // cull and a 40MB camera JPEG takes ~1s to decode, so without this every keypress stalls on the
    // disk. Issued nearest-first and interleaved forward/back: decodes are slot-limited, so the
    // order requests go in decides what finishes first, and the next photo matters most.
    private void PrefetchNeighbours() => PrefetchAround(ViewModel.CurrentIndex);

    // Takes the centre explicitly: compare mode seeks its own pane indices, not ViewModel.CurrentIndex.
    private void PrefetchAround(int centreIndex)
    {
        var photos = ViewModel.Photos;
        if (photos.Count <= 1) return;

        foreach (int offset in ReadAheadOffsets())
        {
            int index = ((centreIndex + offset) % photos.Count + photos.Count) % photos.Count;
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

    // Up/Down are Left/Right — the folder is a single sequence however you ask for the next photo.
    private void SeekActiveView(bool forward)
    {
        if (_compareState is not null)
        {
            if (forward) _compareState.SeekNext(); else _compareState.SeekPrevious();
            _ = LoadComparePanesAsync(_compareState.ActivePane);
            return;
        }

        if (forward) ViewModel.SeekNext(); else ViewModel.SeekPrevious();
        _ = LoadCurrentPhotoAsync();
    }

    private async Task ToggleCompareAsync()
    {
        if (_compareState is not null)
        {
            await ExitCompareAsync();
            return;
        }

        if (ViewModel.Photos.Count == 0)
        {
            NavigationOverlayControl.ShowMessage("Nothing to compare — no photos in this folder.");
            return;
        }

        _compareState = new CompareState(ViewModel.Photos.Count, ViewModel.CurrentIndex);
        ViewModel.IsCompareVisible = true;
        CompareHost.Visibility = Visibility.Visible;
        SingleImageHost.Visibility = Visibility.Collapsed;
        CompareViewControl.SetActivePane(_compareState.ActivePane);
        PeekOverlayControl.Clear();

        // Both panes open at fit-to-window regardless of how the single view was zoomed — the
        // split halves the width, so the previous framing would be wrong for both panes anyway.
        CompareViewControl.ResetZoom();

        await LoadComparePanesAsync(ComparePane.Left, ComparePane.Right);
    }

    private async Task ExitCompareAsync()
    {
        // Land on whichever photo the active pane was showing: browsing in compare and then
        // leaving should deposit you where you were looking, not where you started.
        if (_compareState is not null)
        {
            ViewModel.JumpTo(_compareState.ActiveIndex);
        }

        _compareState = null;
        ViewModel.IsCompareVisible = false;
        CompareHost.Visibility = Visibility.Collapsed;

        // The grid may be up over compare mode; leave it owning the screen if so.
        if (!ViewModel.IsGridVisible)
        {
            SingleImageHost.Visibility = Visibility.Visible;
        }

        await LoadCurrentPhotoAsync();
    }

    private void SwitchComparePane()
    {
        if (_compareState is null) return;

        _compareState.SwitchPane();
        CompareViewControl.SetActivePane(_compareState.ActivePane);
        ShowCompareOverlay();
    }

    private void ToggleZoomLock()
    {
        if (_compareState is null) return;

        bool locked = !CompareViewControl.IsZoomLocked;
        CompareViewControl.SetZoomLocked(locked, _compareState.ActivePane);
        NavigationOverlayControl.ShowMessage(locked
            ? "Zoom locked — both panes move together"
            : "Zoom unlocked — each pane moves on its own");
    }

    // Loads the named panes under a single load generation, so a seek part-way through abandons
    // the whole batch rather than leaving one pane showing the photo you've already moved off.
    private async Task LoadComparePanesAsync(params ComparePane[] panes)
    {
        if (_compareState is null) return;

        var photos = ViewModel.Photos;
        if (photos.Count == 0)
        {
            CompareViewControl.SetPaneImage(ComparePane.Left, null);
            CompareViewControl.SetPaneImage(ComparePane.Right, null);
            return;
        }

        int generation = ++_loadGeneration;

        foreach (var pane in panes)
        {
            var photo = photos[pane == ComparePane.Left ? _compareState.LeftIndex : _compareState.RightIndex];

            var decode = _imageCache.GetAsync(photo);
            var image = decode.IsCompletedSuccessfully ? decode.Result : await decode;

            if (generation != _loadGeneration || _compareState is null) return;

            CompareViewControl.SetPaneImage(pane, image);

            if (image is null)
            {
                NavigationOverlayControl.ShowMessage(DescribeLoadFailure(photo));
                return;
            }
        }

        ShowCompareOverlay();
        PrefetchAround(_compareState.ActiveIndex);
    }

    // The overlay reports the active pane's photo — the pane border says which side that is.
    private void ShowCompareOverlay()
    {
        if (_compareState is null) return;

        var photo = ViewModel.Photos.ElementAtOrDefault(_compareState.ActiveIndex);
        if (photo is null) return;

        NavigationOverlayControl.Show(
            photo, _compareState.ActiveIndex, ViewModel.Photos.Count, ViewModel.IsMarked(photo));
    }

    // Escape is no longer a back button — Shift, I, F and J each own their own mode. It keeps only
    // the two exits where Esc is a strong enough OS convention to be worth the redundancy, and
    // every press counts towards the quit, including those two.
    private async Task HandleEscapeAsync()
    {
        if (_compareState is not null)
        {
            await ExitCompareAsync();
        }
        else if (ViewModel.IsFullscreen)
        {
            ToggleFullscreen();
        }

        if (_escapeCounter.RegisterPress(DateTime.UtcNow))
        {
            Close();
            return;
        }

        // Shown after the await, so the photo reload that exiting compare kicks off can't overwrite
        // it — the same ordering LoadCurrentPhotoAsync forces on HandleCullMoveAsync.
        NavigationOverlayControl.ShowMessage(DescribeQuitProgress(_escapeCounter.PressesRemaining));
    }

    // Nothing else in the app hints that Escape accumulates, so the gesture has to announce itself.
    private static string DescribeQuitProgress(int remaining) => remaining switch
    {
        1 => "Press Esc once more to quit",
        2 => "Press Esc twice more to quit",
        _ => $"Press Esc {remaining} more times to quit",
    };

    private async Task HandleCullMoveAsync()
    {
        var result = ViewModel.MoveMarkedToSelected();
        RefreshGridItems(); // Photos was just reassigned — the grid's ItemsSource would otherwise
                            // still list the photos that just moved into selected/.

        if (_compareState is not null)
        {
            // The folder just shrank; both panes could be pointing past the end of it.
            _compareState.ClampTo(ViewModel.Photos.Count);
            await LoadComparePanesAsync(ComparePane.Left, ComparePane.Right);
        }
        else
        {
            await LoadCurrentPhotoAsync();
        }

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

        await PreloadThenWarmCacheAsync(progress, total);
    }

    // First U checks; a second U (once something's pending) installs and restarts. Two presses on
    // purpose — a stray U mid-cull must not be able to restart the app out from under you.
    private async Task HandleUpdateCheckAsync()
    {
        if (!_updateOffered)
        {
            var result = await _updater.CheckAsync();
            NavigationOverlayControl.ShowMessage(result.Message);
            _updateOffered = result.UpdateAvailable;
            return;
        }

        NavigationOverlayControl.ShowSticky("Downloading update…");
        string? error = await _updater.DownloadAndApplyAsync();

        // Only reached when the update failed — a success restarts the process.
        _updateOffered = false;
        NavigationOverlayControl.ShowMessage(error ?? "Update failed.");
    }

    private async Task PreloadThenWarmCacheAsync(IProgress<int> progress, int total)
    {
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
        // Any other key means you're still working, so the quit gesture starts over. Done before
        // the grid's early-return below, so browsing the grid resets it too.
        if (e.Key != Key.Escape)
        {
            _escapeCounter.Reset();
        }

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
                _ = HandleEscapeAsync();
                e.Handled = true;
                break;

            case Key.Right:
            case Key.Down:
                SeekActiveView(forward: true);
                e.Handled = true;
                break;

            case Key.Left:
            case Key.Up:
                SeekActiveView(forward: false);
                e.Handled = true;
                break;

            case Key.J:
                _ = ToggleCompareAsync();
                e.Handled = true;
                break;

            case Key.K:
                SwitchComparePane();
                e.Handled = true;
                break;

            case Key.H:
                ToggleZoomLock();
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

            case Key.U:
                _ = HandleUpdateCheckAsync();
                e.Handled = true;
                break;

            case Key.P:
                TogglePeek();
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
        CompareHost.Visibility = Visibility.Collapsed;
        PeekOverlayControl.Clear();

        // Start browsing from where you already are, and hand the grid keyboard focus so the
        // arrow keys drive it rather than seeking the single-image view behind it.
        TileGridViewControl.SelectAndFocus(BrowsedPhoto);
    }

    private void HideGrid()
    {
        ViewModel.IsGridVisible = false;
        GridHost.Visibility = Visibility.Collapsed;

        // Back to whichever view the grid was opened over.
        CompareHost.Visibility = _compareState is not null ? Visibility.Visible : Visibility.Collapsed;
        SingleImageHost.Visibility = _compareState is not null ? Visibility.Collapsed : Visibility.Visible;
        Focus();

        _ = RefreshPeekAsync(_loadGeneration);
    }

    // "The photo you're looking at" — the active pane's in compare mode, otherwise the current one.
    // ViewModel.CurrentIndex is stale while compare is up: compare tracks its own two indices and
    // only writes one back on exit.
    private PhotoItem? BrowsedPhoto => _compareState is not null
        ? ViewModel.Photos.ElementAtOrDefault(_compareState.ActiveIndex)
        : ViewModel.CurrentPhoto;

    private void OnTileClicked(PhotoItem item) => OpenTile(item);

    // Leave the grid on a specific photo — from a click or from Enter on the browsed selection.
    private void OpenTile(PhotoItem? item)
    {
        if (item is null) return;

        int index = ViewModel.Photos.ToList().IndexOf(item);
        if (index < 0) return;

        // Opened over compare mode, the tile fills the pane you were browsing.
        if (_compareState is not null)
        {
            _compareState.JumpActiveTo(index);
            HideGrid();
            _ = LoadComparePanesAsync(_compareState.ActivePane);
            return;
        }

        ViewModel.JumpTo(index);
        HideGrid();
        _ = LoadCurrentPhotoAsync();
    }

    // M means "mark what I'm looking at": the browsed tile when the grid is open, the active pane's
    // photo in compare mode, otherwise the photo on screen. Without this distinction, marking from
    // the grid would silently mark whatever the single-image view happened to be showing behind it.
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

        if (_compareState is not null)
        {
            var browsed = BrowsedPhoto;
            if (browsed is null) return;

            ViewModel.ToggleMark(browsed);
            ShowCompareOverlay();
            return;
        }

        ViewModel.ToggleMark();
        if (ViewModel.CurrentPhoto is not null)
        {
            NavigationOverlayControl.Show(ViewModel.CurrentPhoto, ViewModel.CurrentIndex, ViewModel.Photos.Count, ViewModel.IsCurrentMarked);
        }
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
        if (ViewModel.IsFullscreen)
        {
            ExitFullscreen();
        }
        else
        {
            EnterFullscreen();
        }
    }

    // Sizes the window to the monitor's *full* bounds rather than maximising it.
    //
    // WindowState.Maximized would leave the taskbar showing: because the window opts into
    // WindowChrome (for real resize edges), it takes part in normal window management, and
    // maximising then respects the monitor's work area — which excludes the taskbar by definition.
    // Taking the monitor rect directly and positioning the window over it sidesteps that, and picks
    // up whichever monitor the window is currently on rather than assuming the primary.
    private void EnterFullscreen()
    {
        if (!TryGetCurrentMonitorBounds(out Rect bounds))
        {
            return;
        }

        _preFullscreenState = WindowState;
        _preFullscreenResizeMode = ResizeMode;
        _preFullscreenBounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : null;

        // Bounds only take effect on a non-maximised window.
        WindowState = WindowState.Normal;
        ResizeMode = ResizeMode.NoResize;

        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        Topmost = true;

        ViewModel.IsFullscreen = true;
    }

    private void ExitFullscreen()
    {
        Topmost = false;
        ResizeMode = _preFullscreenResizeMode;

        if (_preFullscreenBounds is Rect previous)
        {
            Left = previous.Left;
            Top = previous.Top;
            Width = previous.Width;
            Height = previous.Height;
        }

        WindowState = _preFullscreenState;
        ViewModel.IsFullscreen = false;
    }

    // The monitor this window is on, in WPF's device-independent units. GetMonitorInfo answers in
    // physical pixels, so on a scaled display (very likely here) the raw numbers would overshoot —
    // hence dividing through by the visual's device transform.
    private bool TryGetCurrentMonitorBounds(out Rect bounds)
    {
        bounds = default;

        IntPtr monitor = MonitorFromWindow(new WindowInteropHelper(this).Handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
        double scaleX = transform?.M11 is > 0 ? transform.Value.M11 : 1;
        double scaleY = transform?.M22 is > 0 ? transform.Value.M22 : 1;

        var screen = info.rcMonitor;
        bounds = new Rect(
            screen.Left / scaleX,
            screen.Top / scaleY,
            (screen.Right - screen.Left) / scaleX,
            (screen.Bottom - screen.Top) / scaleY);

        return bounds is { Width: > 0, Height: > 0 };
    }

    // A borderless window maximises over the *whole* monitor, not the work area — so the taskbar,
    // which is always-on-top, ends up drawn across the bottom of our content (it was covering the
    // filename/position overlay in the corner). Constrain maximise to the work area so it behaves
    // like a normal maximised window. Fullscreen is unaffected: it sets explicit bounds on a
    // Normal-state window rather than maximising, and is the one mode that *should* cover the taskbar.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WindowProc);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmGetMinMaxInfo = 0x0024;

        if (msg == WmGetMinMaxInfo && TryConstrainMaximiseToWorkArea(hwnd, lParam))
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static bool TryConstrainMaximiseToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        NativeRect work = info.rcWork;
        NativeRect screen = info.rcMonitor;

        // Positions here are relative to the monitor's top-left, not the desktop's.
        minMax.ptMaxPosition.X = work.Left - screen.Left;
        minMax.ptMaxPosition.Y = work.Top - screen.Top;
        minMax.ptMaxSize.X = work.Right - work.Left;
        minMax.ptMaxSize.Y = work.Bottom - work.Top;

        // ...but still allow a *non*-maximised window to reach the monitor's full size, which is
        // exactly what fullscreen asks for. Without this the tracking limit would clamp it back to
        // the work area and the taskbar would reappear.
        minMax.ptMaxTrackSize.X = screen.Right - screen.Left;
        minMax.ptMaxTrackSize.Y = screen.Bottom - screen.Top;

        Marshal.StructureToPtr(minMax, lParam, fDeleteOld: true);
        return true;
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor; // whole monitor, taskbar included
        public NativeRect rcWork;    // work area, taskbar excluded
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint ptReserved;
        public NativePoint ptMaxSize;
        public NativePoint ptMaxPosition;
        public NativePoint ptMinTrackSize;
        public NativePoint ptMaxTrackSize;
    }
}
