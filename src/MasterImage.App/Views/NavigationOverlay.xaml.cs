using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using MasterImage.Core;

namespace MasterImage.App.Views;

public partial class NavigationOverlay : UserControl
{
    public NavigationOverlay()
    {
        InitializeComponent();
    }

    public void Show(PhotoItem photo, int index, int total, bool isMarked)
    {
        FileNameText.Text = Path.GetFileName(photo.PrimaryFilePath);
        PositionText.Text = $"{index + 1}/{total}";
        MarkIndicator.Visibility = isMarked ? Visibility.Visible : Visibility.Collapsed;
        FadeOutAfter(TimeSpan.FromSeconds(1.2));
    }

    // Longer on-screen time than Show(): a sentence is read-heavy compared with a filename.
    public void ShowMessage(string message)
    {
        SetMessage(message);
        FadeOutAfter(TimeSpan.FromSeconds(2.5));
    }

    // Stays put until something else replaces it — for long-running progress, where a fade-out
    // would hide the status while the operation is still going.
    public void ShowSticky(string message)
    {
        SetMessage(message);
        OverlayPanel.BeginAnimation(OpacityProperty, null);
        OverlayPanel.Opacity = 1;
    }

    private void SetMessage(string message)
    {
        FileNameText.Text = message;
        PositionText.Text = "";
        MarkIndicator.Visibility = Visibility.Collapsed;
    }

    private void FadeOutAfter(TimeSpan delay)
    {
        // Clear any in-flight animation first, otherwise the previous fade keeps running and
        // the panel we just set to fully opaque immediately starts vanishing again.
        OverlayPanel.BeginAnimation(OpacityProperty, null);
        OverlayPanel.Opacity = 1;

        OverlayPanel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(1.5))) { BeginTime = delay });
    }
}
