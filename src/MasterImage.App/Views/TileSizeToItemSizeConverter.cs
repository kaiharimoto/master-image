using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MasterImage.App.Views;

// Turns MainViewModel.TileSize into the grid's cell size.
//
// VirtualizingWrapPanel determines its cell size once, by measuring the first realized item, and
// then reuses it — so binding only the tile's Border to TileSize resizes the picture inside each
// cell while the cells themselves stay put. Binding the panel's ItemSize to the same value keeps
// the layout in step with the content.
//
// The parameter is the tile's total horizontal/vertical margin (its Margin, doubled), so the cell
// leaves room for the gap between tiles.
public sealed class TileSizeToItemSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double tile = value is double size ? size : 0;
        double margin = parameter is string text && double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 0;

        return new Size(tile + margin, tile + margin);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
