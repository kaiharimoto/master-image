using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MasterImage.App.ViewModels;

namespace MasterImage.App.Views;

// Two photos side by side. Each pane is a full SingleImageView, so wheel-zoom, drag-pan,
// cursor-anchored scaling and the scale limits all come for free; this control only adds which
// pane is active and whether the two zoom together.
public partial class CompareView : UserControl
{
    // The same blue the shortcuts overlay uses for its section headings — the app's one accent.
    private static readonly Brush ActivePaneBrush =
        new SolidColorBrush(Color.FromRgb(0x8A, 0xB4, 0xF8));

    // Guards against the mirrored assignment bouncing straight back. SingleImageView also no-ops
    // an assignment that changes nothing, so this is the second of two independent stops.
    private bool _isMirroring;

    public CompareView()
    {
        InitializeComponent();

        foreach (var pane in new[] { LeftPane, RightPane })
        {
            pane.AllowWindowGestures = false;
        }

        LeftPane.TransformChanged += (_, _) => Mirror(LeftPane, RightPane);
        RightPane.TransformChanged += (_, _) => Mirror(RightPane, LeftPane);

        SetActivePane(ComparePane.Right);
    }

    // On by default: the dominant use for a side-by-side is judging two frames of the same shot for
    // focus, and mirrored movement is the point of that comparison. Independent zoom is the
    // exception, so it's the toggle rather than the default.
    public bool IsZoomLocked { get; private set; } = true;

    public void SetPaneImage(ComparePane pane, BitmapSource? image) =>
        // resetZoom: false — seeking a pane must not throw away the framing you set up, or
        // comparing a run of candidates against a fixed reference would mean re-zooming every time.
        PaneFor(pane).SetImage(image, resetZoom: false);

    public void SetActivePane(ComparePane pane)
    {
        LeftPaneBorder.BorderBrush = pane == ComparePane.Left ? ActivePaneBrush : Brushes.Transparent;
        RightPaneBorder.BorderBrush = pane == ComparePane.Right ? ActivePaneBrush : Brushes.Transparent;
    }

    public void SetZoomLocked(bool locked, ComparePane activePane)
    {
        IsZoomLocked = locked;

        // Bring the panes together the moment the lock goes on. Without this, H would appear to do
        // nothing until you next touched the wheel, and it'd be unclear whether it had taken.
        if (locked)
        {
            var source = PaneFor(activePane);
            Mirror(source, source == LeftPane ? RightPane : LeftPane);
        }
    }

    public void ResetZoom()
    {
        LeftPane.ResetZoom();
        RightPane.ResetZoom();
    }

    private void Mirror(SingleImageView source, SingleImageView target)
    {
        if (!IsZoomLocked || _isMirroring) return;

        _isMirroring = true;
        try
        {
            target.Transform = source.Transform;
        }
        finally
        {
            _isMirroring = false;
        }
    }

    private SingleImageView PaneFor(ComparePane pane) => pane == ComparePane.Left ? LeftPane : RightPane;
}
