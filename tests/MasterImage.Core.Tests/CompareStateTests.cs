using MasterImage.App.ViewModels;
using Xunit;

namespace MasterImage.Core.Tests;

public class CompareStateTests
{
    [Fact]
    public void OpensWithBothPanesOnTheCurrentPhotoAndTheRightOneActive()
    {
        var state = new CompareState(photoCount: 5, startIndex: 2);

        // Both panes seed from the photo that was on screen, so J never loses your place — you
        // pick the second photo by browsing, not by hunting for the first one again.
        Assert.Equal(2, state.LeftIndex);
        Assert.Equal(2, state.RightIndex);
        Assert.Equal(ComparePane.Right, state.ActivePane);
    }

    [Fact]
    public void ArrowsMoveOnlyTheActivePane()
    {
        var state = new CompareState(photoCount: 5, startIndex: 2);

        state.SeekNext();

        Assert.Equal(3, state.RightIndex);
        Assert.Equal(2, state.LeftIndex);
    }

    [Fact]
    public void SwitchPaneRedirectsTheArrowsToTheOtherPane()
    {
        var state = new CompareState(photoCount: 5, startIndex: 2);

        state.SwitchPane();
        state.SeekNext();

        Assert.Equal(ComparePane.Left, state.ActivePane);
        Assert.Equal(3, state.LeftIndex);
        Assert.Equal(2, state.RightIndex);
    }

    [Fact]
    public void SeekNextWrapsAroundAtTheEnd()
    {
        var state = new CompareState(photoCount: 3, startIndex: 2);

        state.SeekNext();

        Assert.Equal(0, state.RightIndex);
    }

    [Fact]
    public void SeekPreviousWrapsAroundAtTheStart()
    {
        var state = new CompareState(photoCount: 3, startIndex: 0);

        state.SeekPrevious();

        Assert.Equal(2, state.RightIndex);
    }

    [Fact]
    public void ActiveIndexFollowsTheActivePane()
    {
        var state = new CompareState(photoCount: 5, startIndex: 2);
        state.SeekNext();

        // Exiting compare mode lands on ActiveIndex, so it must track the pane you were browsing.
        Assert.Equal(3, state.ActiveIndex);

        state.SwitchPane();
        Assert.Equal(2, state.ActiveIndex);
    }

    [Fact]
    public void SeekingAnEmptyFolderDoesNothingRatherThanThrowing()
    {
        var state = new CompareState(photoCount: 0, startIndex: 0);

        state.SeekNext();
        state.SeekPrevious();

        Assert.Equal(0, state.LeftIndex);
        Assert.Equal(0, state.RightIndex);
    }

    [Fact]
    public void SeekingASinglePhotoFolderStaysPut()
    {
        var state = new CompareState(photoCount: 1, startIndex: 0);

        state.SeekNext();

        Assert.Equal(0, state.RightIndex);
    }

    [Fact]
    public void StartIndexIsClampedIntoRange()
    {
        var state = new CompareState(photoCount: 3, startIndex: 99);

        Assert.Equal(2, state.LeftIndex);
        Assert.Equal(2, state.RightIndex);
    }

    [Fact]
    public void JumpActiveToMovesOnlyTheActivePane()
    {
        var state = new CompareState(photoCount: 5, startIndex: 0);

        // Shift opens the grid over compare mode, so opening a tile has to land somewhere — it
        // lands in the pane you were browsing, same as the arrows.
        state.JumpActiveTo(3);

        Assert.Equal(3, state.RightIndex);
        Assert.Equal(0, state.LeftIndex);
    }

    [Fact]
    public void JumpActiveToIgnoresAnOutOfRangeIndex()
    {
        var state = new CompareState(photoCount: 5, startIndex: 2);

        state.JumpActiveTo(99);
        state.JumpActiveTo(-1);

        Assert.Equal(2, state.RightIndex);
    }

    [Fact]
    public void ClampToPullsBothPanesBackInsideAShrunkPhotoList()
    {
        var state = new CompareState(photoCount: 5, startIndex: 4);
        state.SwitchPane();
        state.SeekPrevious(); // left = 3, right = 4

        // N moves marked photos out of the folder and reloads it — both panes can now be pointing
        // past the end, and indexing there would throw.
        state.ClampTo(photoCount: 2);

        Assert.Equal(1, state.LeftIndex);
        Assert.Equal(1, state.RightIndex);
    }

    [Fact]
    public void ClampToTeachesSeekTheNewLength()
    {
        var state = new CompareState(photoCount: 5, startIndex: 0);

        state.ClampTo(photoCount: 2);
        state.SeekPrevious();

        // Wrapping must use the new count, not the one from construction.
        Assert.Equal(1, state.RightIndex);
    }

    [Fact]
    public void ClampToAnEmptyFolderLeavesUsableIndices()
    {
        var state = new CompareState(photoCount: 5, startIndex: 4);

        state.ClampTo(photoCount: 0);

        Assert.Equal(0, state.LeftIndex);
        Assert.Equal(0, state.RightIndex);
    }
}
