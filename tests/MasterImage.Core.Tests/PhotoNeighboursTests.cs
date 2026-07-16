using System;
using System.IO;
using MasterImage.App.ViewModels;
using Xunit;

namespace MasterImage.Core.Tests;

public class PhotoNeighboursTests : IDisposable
{
    private readonly string _tempDir;

    public PhotoNeighboursTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private MainViewModel VmWith(int photoCount, string? startAt = null)
    {
        for (int i = 0; i < photoCount; i++)
        {
            TestImageFactory.WriteTestJpeg(Path.Combine(_tempDir, $"DSC{i}.jpg"), 40, 30);
        }

        return new MainViewModel(_tempDir, startAt is null ? null : Path.Combine(_tempDir, startAt));
    }

    [Fact]
    public void NeighboursAreThePhotosEitherSide()
    {
        var vm = VmWith(5, "DSC2.jpg");

        var neighbours = vm.GetNeighbours();

        Assert.Equal("DSC1", neighbours.Previous!.Stem);
        Assert.Equal("DSC3", neighbours.Next!.Stem);
    }

    [Fact]
    public void PreviousWrapsToTheEndOfTheFolder()
    {
        var vm = VmWith(3, "DSC0.jpg");

        var neighbours = vm.GetNeighbours();

        // Seeking wraps, so the peek must too — otherwise the corner sits empty while Left
        // cheerfully takes you round to the last photo.
        Assert.Equal("DSC2", neighbours.Previous!.Stem);
    }

    [Fact]
    public void NextWrapsToTheStartOfTheFolder()
    {
        var vm = VmWith(3, "DSC2.jpg");

        var neighbours = vm.GetNeighbours();

        Assert.Equal("DSC0", neighbours.Next!.Stem);
    }

    [Fact]
    public void NeighboursAgreeWithWhereTheArrowKeysActuallyLand()
    {
        var vm = VmWith(4, "DSC0.jpg");
        var neighbours = vm.GetNeighbours();

        // The contract that matters: the peek must never show something different from what the
        // arrow will do. Assert it against the real seek rather than against arithmetic.
        vm.SeekPrevious();
        Assert.Equal(vm.CurrentPhoto!.Stem, neighbours.Previous!.Stem);

        vm.SeekNext();
        vm.SeekNext();
        Assert.Equal(vm.CurrentPhoto!.Stem, neighbours.Next!.Stem);
    }

    [Fact]
    public void TwoPhotosStillYieldDistinctNeighbours()
    {
        var vm = VmWith(2, "DSC0.jpg");

        var neighbours = vm.GetNeighbours();

        // Both sides wrap onto the other photo. That's honest: Left and Right both go there.
        Assert.Equal("DSC1", neighbours.Previous!.Stem);
        Assert.Equal("DSC1", neighbours.Next!.Stem);
    }

    [Fact]
    public void ASinglePhotoHasNoNeighbours()
    {
        var vm = VmWith(1);

        var neighbours = vm.GetNeighbours();

        // Previous and next would both be the photo you're already looking at; showing it three
        // times says nothing.
        Assert.Null(neighbours.Previous);
        Assert.Null(neighbours.Next);
    }

    [Fact]
    public void AnEmptyFolderHasNoNeighbours()
    {
        var vm = VmWith(0);

        var neighbours = vm.GetNeighbours();

        Assert.Null(neighbours.Previous);
        Assert.Null(neighbours.Next);
    }
}
