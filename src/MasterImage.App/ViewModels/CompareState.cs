namespace MasterImage.App.ViewModels;

public enum ComparePane
{
    Left,
    Right,
}

// Which two photos compare mode is showing, and which one the arrows drive.
//
// Kept out of CompareView deliberately: the navigation rules (wrapping, which pane moves, what
// happens when a cull shrinks the folder underneath you) are the part worth testing, and they'd
// otherwise need a WPF control and a message loop to exercise.
public sealed class CompareState
{
    private int _photoCount;

    public CompareState(int photoCount, int startIndex)
    {
        _photoCount = Math.Max(0, photoCount);

        // Both panes start on the photo that was already on screen: J opens the split without
        // losing your place, and you pick the second photo by browsing from there.
        int seed = Clamp(startIndex);
        LeftIndex = seed;
        RightIndex = seed;

        // The left pane is the one you just came from, so the right is the one you want to move.
        ActivePane = ComparePane.Right;
    }

    public int LeftIndex { get; private set; }
    public int RightIndex { get; private set; }
    public ComparePane ActivePane { get; private set; }

    public int ActiveIndex => ActivePane == ComparePane.Left ? LeftIndex : RightIndex;

    public void SwitchPane() =>
        ActivePane = ActivePane == ComparePane.Left ? ComparePane.Right : ComparePane.Left;

    public void SeekNext() => Seek(1);

    public void SeekPrevious() => Seek(-1);

    // Opening a tile from the grid while compare mode is up: it lands in the pane you were
    // browsing, for the same reason the arrows do.
    public void JumpActiveTo(int index)
    {
        if (index < 0 || index >= _photoCount) return;
        SetActiveIndex(index);
    }

    private void Seek(int delta)
    {
        if (_photoCount == 0) return;

        SetActiveIndex(((ActiveIndex + delta) % _photoCount + _photoCount) % _photoCount);
    }

    private void SetActiveIndex(int index)
    {
        if (ActivePane == ComparePane.Left)
        {
            LeftIndex = index;
        }
        else
        {
            RightIndex = index;
        }
    }

    // N moves marked photos out of the folder and reloads it, so the list can shrink under a pane
    // that's pointing near the end. MainViewModel already does this for its own index; two more
    // indices need the same treatment or compare mode would index past the end after a cull.
    public void ClampTo(int photoCount)
    {
        _photoCount = Math.Max(0, photoCount);
        LeftIndex = Clamp(LeftIndex);
        RightIndex = Clamp(RightIndex);
    }

    private int Clamp(int index) => _photoCount == 0 ? 0 : Math.Clamp(index, 0, _photoCount - 1);
}
