using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace MasterImage.App.Views;

// A glance at the photos either side of the one on screen: previous bottom-left, next bottom-right.
// Purely a display — which photos these are, and whether to show them at all, is MainWindow's call.
public partial class PeekOverlay : UserControl
{
    public PeekOverlay()
    {
        InitializeComponent();
    }

    public void Show(BitmapSource? previous, BitmapSource? next)
    {
        SetPanel(PreviousPanel, PreviousImage, previous);
        SetPanel(NextPanel, NextImage, next);
    }

    public void Clear() => Show(null, null);

    // A thumbnail that failed to generate hides its panel rather than leaving an empty frame — an
    // empty bordered box in the corner reads as a bug, not as "no picture for this one".
    private static void SetPanel(UIElement panel, Image image, BitmapSource? source)
    {
        image.Source = source;
        panel.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
