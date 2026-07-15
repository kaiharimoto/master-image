using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MasterImage.App.ViewModels;
using MasterImage.Core;

namespace MasterImage.App.Views;

public partial class TileGridView : UserControl
{
    public event Action<PhotoItem>? TileClicked;

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public TileGridView()
    {
        InitializeComponent();
        MouseWheel += OnMouseWheel;
        TileList.MouseUp += OnTileListMouseUp;
    }

    public void SetItems(IReadOnlyList<PhotoItem> items)
    {
        TileList.ItemsSource = items;
    }

    // Which photo is under the cursor right now, or null if the cursor isn't over a tile.
    // Used on Shift-release to land on the tile the user was pointing at (spec §7).
    public PhotoItem? GetHoveredItem()
    {
        var hit = VisualTreeHelper.HitTest(TileList, Mouse.GetPosition(TileList));
        DependencyObject? node = hit?.VisualHit;

        while (node is not null and not ListBoxItem)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        return (node as ListBoxItem)?.DataContext as PhotoItem;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // While the grid is visible, Shift is already held (that's how the grid opened),
        // so any wheel event here is inherently a "Shift + scroll" per spec §7.
        // Tile Width/Height are bound straight to ViewModel.TileSize in XAML, so updating
        // it here is enough to resize every tile live.
        if (ViewModel is null) return;
        ViewModel.TileSize += e.Delta > 0 ? 20 : -20;
        e.Handled = true;
    }

    private void OnTileListMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (TileList.SelectedItem is PhotoItem item)
        {
            TileClicked?.Invoke(item);
        }
    }

    private void Tile_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement border || border.Tag is not PhotoItem item || ViewModel is null) return;

        var image = (Image)border.FindName("TileImage")!;
        var badge = (TextBlock)border.FindName("MarkBadge")!;

        badge.Visibility = ViewModel.IsMarked(item) ? Visibility.Visible : Visibility.Collapsed;

        _ = LoadTileThumbnailAsync(border, image, item);
    }

    private async Task LoadTileThumbnailAsync(FrameworkElement border, Image image, PhotoItem item)
    {
        var thumbnail = await ViewModel!.GetThumbnailAsync(item);
        if (thumbnail is null) return;

        // Tile containers are recycled (VirtualizationMode=Recycling), so by the time this
        // thumbnail arrives the very same Border may already have been re-bound to a different
        // photo further down the folder. Tag is re-bound before Loaded re-fires for the new item,
        // so it is the authoritative record of what this tile currently shows: if it no longer
        // matches, drop the result rather than painting a stale photo into a live tile.
        if (!ReferenceEquals(border.Tag, item)) return;

        image.Source = thumbnail;
    }
}
