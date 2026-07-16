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

    // Raised when a left-drag starts on a photo that isn't zoomed in. There's no title bar to grab
    // (the window is borderless by design), and at fit-to-window a left-drag has nothing to pan,
    // so the gesture is free to mean "move the window" — which is what you'd instinctively try.
    public event EventHandler? WindowDragRequested;

    // Raised on double-click, for maximise/restore — the other thing a title bar would have given us.
    public event EventHandler? MaximiseToggleRequested;

    // Raised whenever zoom or pan changes, so compare mode can mirror one pane onto the other.
    public event EventHandler? TransformChanged;

    // False for compare-mode panes. Dragging one half of a split to move the whole window (or
    // double-clicking it to maximise) makes the two panes feel like separate windows rather than
    // one view — and unlike single-image view, a pane has no title bar to miss.
    public bool AllowWindowGestures { get; set; } = true;

    // The pane's zoom/pan, as a matrix in the pane's own coordinate space. Assigning it is how
    // compare mode mirrors one pane onto the other while zoom is locked; because both panes are
    // the same size and both use Stretch="Uniform", copying the matrix mirrors *relative* framing
    // even when the two photos have different dimensions.
    public Matrix Transform
    {
        get => ImageTransform.Matrix;
        set => SetTransform(value);
    }

    public SingleImageView()
    {
        InitializeComponent();
        MouseWheel += OnMouseWheel;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

    // resetZoom: false keeps the current framing across a photo swap. Compare mode wants that —
    // zooming both panes to 100% and then flipping through candidates against a fixed reference is
    // the whole point of the split, and re-zooming after every seek would defeat it.
    public void SetImage(BitmapSource? image, bool resetZoom = true)
    {
        PhotoImage.Source = image;
        if (resetZoom)
        {
            ResetZoom();
        }
    }

    public void ResetZoom() => SetTransform(Matrix.Identity);

    // The single funnel for every zoom and pan. Everything goes through here so TransformChanged
    // can't miss a change — and the equality guard means a mirrored assignment that changes
    // nothing raises nothing, which is what stops two locked panes echoing each other forever.
    private void SetTransform(Matrix matrix)
    {
        if (ImageTransform.Matrix == matrix) return;

        ImageTransform.Matrix = matrix;
        TransformChanged?.Invoke(this, EventArgs.Empty);
    }

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
            SetTransform(matrix);
        }

        e.Handled = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if (AllowWindowGestures)
            {
                MaximiseToggleRequested?.Invoke(this, EventArgs.Empty);
            }
            e.Handled = true;
            return;
        }

        if (ImageTransform.Matrix.M11 <= MinScale)
        {
            // Not zoomed, so there's nothing to pan — drag the window instead. Must be raised
            // synchronously from the mouse-down: Window.DragMove() requires the button to still
            // be physically down when it's called.
            if (AllowWindowGestures)
            {
                WindowDragRequested?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

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
        SetTransform(matrix);
        _dragStart = current;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ReleaseMouseCapture();
    }
}
