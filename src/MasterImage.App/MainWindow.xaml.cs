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
        if (photo is null) return;

        var image = await Task.Run(() => Core.ImageLoader.TryLoadAtSize(photo.PrimaryFilePath, decodePixelWidth: 1920));
        SingleImageViewControl.SetImage(image);
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
                e.Handled = true;
                break;

            case Key.N:
                var result = ViewModel.MoveMarkedToSelected();
                _ = LoadCurrentPhotoAsync();
                // The cull rebuilds ViewModel.Photos, and SetItems handed the grid the *old*
                // list object, so the grid would otherwise still show the just-moved photos.
                RefreshGridItems();
                MessageBox.Show(
                    $"Moved {result.MovedFileCount} file(s) to selected/." +
                    (result.Failures.Count > 0 ? $"\n{result.Failures.Count} failure(s) — see below:\n{string.Join("\n", result.Failures)}" : ""),
                    "Master Image");
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
