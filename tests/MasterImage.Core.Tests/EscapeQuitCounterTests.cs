using System;
using MasterImage.App.ViewModels;
using Xunit;

namespace MasterImage.Core.Tests;

public class EscapeQuitCounterTests
{
    private static readonly DateTime T0 = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ThreePressesInsideTheWindowQuit()
    {
        var counter = new EscapeQuitCounter();

        Assert.False(counter.RegisterPress(T0));
        Assert.False(counter.RegisterPress(T0.AddMilliseconds(300)));
        Assert.True(counter.RegisterPress(T0.AddMilliseconds(600)));
    }

    [Fact]
    public void PausingLongerThanTheWindowRestartsTheCount()
    {
        var counter = new EscapeQuitCounter();
        counter.RegisterPress(T0);
        counter.RegisterPress(T0.AddMilliseconds(300));

        // A stray Esc now and a stray Esc a minute later must not add up to a quit.
        Assert.False(counter.RegisterPress(T0.AddSeconds(30)));
        Assert.Equal(2, counter.PressesRemaining);
    }

    [Fact]
    public void TheWindowIsMeasuredFromThePreviousPressNotTheFirst()
    {
        var counter = new EscapeQuitCounter();
        counter.RegisterPress(T0);
        counter.RegisterPress(T0.AddSeconds(1.2));

        // 2.4s since the first press, but only 1.2s since the last — a steady tap still quits.
        Assert.True(counter.RegisterPress(T0.AddSeconds(2.4)));
    }

    [Fact]
    public void AnyOtherKeyResetsTheCount()
    {
        var counter = new EscapeQuitCounter();
        counter.RegisterPress(T0);
        counter.RegisterPress(T0.AddMilliseconds(100));

        counter.Reset();

        Assert.False(counter.RegisterPress(T0.AddMilliseconds(200)));
    }

    [Fact]
    public void PressesRemainingDrivesTheOnScreenCountdown()
    {
        var counter = new EscapeQuitCounter();
        Assert.Equal(3, counter.PressesRemaining);

        counter.RegisterPress(T0);
        Assert.Equal(2, counter.PressesRemaining);

        counter.RegisterPress(T0.AddMilliseconds(100));
        Assert.Equal(1, counter.PressesRemaining);
    }

    [Fact]
    public void QuittingIsAnnouncedOnceNotOnEveryFurtherPress()
    {
        var counter = new EscapeQuitCounter();
        counter.RegisterPress(T0);
        counter.RegisterPress(T0.AddMilliseconds(100));
        Assert.True(counter.RegisterPress(T0.AddMilliseconds(200)));

        // The window is closing; a fourth press starts a fresh count rather than re-reporting a
        // quit, so a held-down Esc can't fire the shutdown path repeatedly.
        Assert.False(counter.RegisterPress(T0.AddMilliseconds(300)));
    }

    [Fact]
    public void APressThatExitedFullscreenStillCounts()
    {
        var counter = new EscapeQuitCounter();

        // Escape exits fullscreen *and* counts — the caller doesn't get to exempt it. Three rapid
        // presses from fullscreen therefore quit, which is the accepted consequence of the design.
        counter.RegisterPress(T0);
        counter.RegisterPress(T0.AddMilliseconds(100));

        Assert.True(counter.RegisterPress(T0.AddMilliseconds(200)));
    }
}
