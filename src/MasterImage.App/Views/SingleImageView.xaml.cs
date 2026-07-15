using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MasterImage.App.Views;

public partial class SingleImageView : UserControl
{
    private const double MinScale = 1.0;
    private const double MaxScale = 8.0;

    private Point _dragStart;
    private bool _isDragging;

    public SingleImageView()
    {
        InitializeComponent();
        MouseWheel += OnMouseWheel;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

    public void SetImage(BitmapSource? image)
    {
        PhotoImage.Source = image;
        ResetZoom();
    }

    public void ResetZoom() => ImageTransform.Matrix = Matrix.Identity;

    // Zoom anchored on the cursor (spec §5): scale about the pointer's position in this control's
    // coordinate space, so whatever detail is under the cursor stays under the cursor as you zoom,
    // instead of the image drifting toward its centre. MinScale 1.0 = fit-to-window (Stretch
    // Uniform), so you can zoom in to inspect and back out to the fitted view, but no further.
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var matrix = ImageTransform.Matrix;
        double currentScale = matrix.M11;
        double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;

        // Land exactly on the limit rather than overshooting it.
        double targetScale = Math.Clamp(currentScale * factor, MinScale, MaxScale);
        if (targetScale == currentScale)
        {
            e.Handled = true;
            return;
        }
        factor = targetScale / currentScale;

        if (targetScale == MinScale)
        {
            // Back at fit-to-window: drop any accumulated pan so the photo re-centres cleanly.
            ResetZoom();
        }
        else
        {
            var origin = e.GetPosition(this);
            matrix.ScaleAt(factor, factor, origin.X, origin.Y);
            ImageTransform.Matrix = matrix;
        }

        e.Handled = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ImageTransform.Matrix.M11 <= MinScale) return;
        _isDragging = true;
        _dragStart = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var current = e.GetPosition(this);
        var matrix = ImageTransform.Matrix;
        matrix.Translate(current.X - _dragStart.X, current.Y - _dragStart.Y);
        ImageTransform.Matrix = matrix;
        _dragStart = current;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ReleaseMouseCapture();
    }
}
