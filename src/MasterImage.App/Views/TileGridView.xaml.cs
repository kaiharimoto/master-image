using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

    public PhotoItem? SelectedItem => TileList.SelectedItem as PhotoItem;

    // Select a photo and hand the ListBox keyboard focus, so arrow keys browse the grid natively —
    // including up/down across rows, which the wrap panel works out for us.
    public void SelectAndFocus(PhotoItem? item)
    {
        if (item is not null)
        {
            TileList.SelectedItem = item;
            TileList.ScrollIntoView(item);
        }

        TileList.Focus();

        // The container for the selected item may not exist yet (virtualization realises it during
        // the layout pass ScrollIntoView just queued), so focus it once layout has settled.
        Dispatcher.BeginInvoke(new Action(FocusSelectedContainer), DispatcherPriority.Loaded);
    }

    private void FocusSelectedContainer()
    {
        if (TileList.SelectedItem is null) return;
        if (TileList.ItemContainerGenerator.ContainerFromItem(TileList.SelectedItem) is ListBoxItem container)
        {
            container.Focus();
        }
    }

    // Marks are held outside PhotoItem (in MarksStore), so a tile's badge can't simply bind to the
    // item. Tile_Loaded sets it when a container is realised; this repaints it for a photo whose
    // mark changed while its tile was already on screen.
    public void RefreshMarkBadgeFor(PhotoItem item)
    {
        if (ViewModel is null) return;
        if (TileList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container) return;

        var badge = FindDescendantByName<TextBlock>(container, "MarkBadge");
        if (badge is not null)
        {
            badge.Visibility = ViewModel.IsMarked(item) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static T? FindDescendantByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed && typed.Name == name)
            {
                return typed;
            }

            var found = FindDescendantByName<T>(child, name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
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

    // Shift+scroll resizes tiles; a plain scroll is left alone so it scrolls the list, which is
    // what you want now the grid is a toggle you browse rather than something held open.
    // Tile Width/Height bind straight to ViewModel.TileSize, so setting it resizes every tile live.
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ViewModel is null) return;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return;

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
