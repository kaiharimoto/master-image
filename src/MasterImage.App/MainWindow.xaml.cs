using System.IO;
using System.Windows;
using System.Windows.Input;
using MasterImage.App.ViewModels;

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

        App.OpenRequested += OnOpenRequestedFromAnotherProcess;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
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
        Activate();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (ViewModel.IsShortcutsOverlayVisible)
            {
                ViewModel.IsShortcutsOverlayVisible = false;
            }
            else if (ViewModel.IsFullscreen)
            {
                ToggleFullscreen();
            }
            e.Handled = true;
        }
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
