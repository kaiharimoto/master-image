namespace MasterImage.App.ViewModels;

// Counts Escape presses towards closing the app.
//
// Takes the current time as a parameter rather than reading the clock, so the window can be tested
// instantly instead of by sleeping through it.
public sealed class EscapeQuitCounter
{
    public const int PressesToQuit = 3;

    // Long enough to be a comfortable triple-tap, short enough that presses spread across a cull
    // never accumulate into a quit.
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(1.5);

    private int _count;
    private DateTime _lastPress;

    // True when this press was the last one needed — the caller should shut down.
    public bool RegisterPress(DateTime now)
    {
        // Measured from the previous press, not the first, so a steady deliberate tap always
        // reaches three however slow it is; only an actual pause resets.
        if (_count > 0 && now - _lastPress > Window)
        {
            _count = 0;
        }

        _lastPress = now;
        _count++;

        if (_count < PressesToQuit) return false;

        // Start over rather than staying latched: a held-down Escape auto-repeats, and re-reporting
        // a quit on every repeat would run the shutdown path several times over.
        _count = 0;
        return true;
    }

    public int PressesRemaining => PressesToQuit - _count;

    public void Reset() => _count = 0;
}
