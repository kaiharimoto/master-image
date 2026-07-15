using System;
using System.IO;
using MasterImage.App.ViewModels;
using MasterImage.Core.Tests;
using Xunit;

namespace MasterImage.Core.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public MainViewModelTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
        for (int i = 0; i < 3; i++)
        {
            TestImageFactory.WriteTestJpeg(Path.Combine(_tempDir, $"DSC{i}.jpg"), 100, 100);
        }
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void StartsAtFirstPhotoWhenNoInitialFileGiven()
    {
        var vm = new MainViewModel(_tempDir, initialFilePath: null);
        Assert.Equal("DSC0", vm.CurrentPhoto!.Stem);
    }

    [Fact]
    public void StartsAtRequestedInitialFile()
    {
        var vm = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC1.jpg"));
        Assert.Equal("DSC1", vm.CurrentPhoto!.Stem);
    }

    [Fact]
    public void SeekNextWrapsAroundAtTheEnd()
    {
        var vm = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC2.jpg"));
        vm.SeekNext();
        Assert.Equal("DSC0", vm.CurrentPhoto!.Stem);
    }

    [Fact]
    public void SeekPreviousWrapsAroundAtTheStart()
    {
        var vm = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC0.jpg"));
        vm.SeekPrevious();
        Assert.Equal("DSC2", vm.CurrentPhoto!.Stem);
    }

    [Fact]
    public void ToggleMarkFlipsIsCurrentMarkedAndPersists()
    {
        var vm = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC0.jpg"));
        Assert.False(vm.IsCurrentMarked);

        vm.ToggleMark();
        Assert.True(vm.IsCurrentMarked);

        var reloaded = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC0.jpg"));
        Assert.True(reloaded.IsCurrentMarked);
    }

    [Fact]
    public void MoveMarkedToSelectedRemovesItemsAndClampsIndex()
    {
        var vm = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC2.jpg"));
        vm.ToggleMark();

        var result = vm.MoveMarkedToSelected();

        Assert.Equal(1, result.MovedFileCount);
        Assert.Equal(2, vm.Photos.Count);
        Assert.InRange(vm.CurrentIndex, 0, 1);
    }

    [Fact]
    public void MoveMarkedToSelectedKeepsTheMarkOnAPhotoThatFailedToMove()
    {
        // Pre-create a colliding file so DSC0's move fails.
        string selectedFolder = Path.Combine(_tempDir, "selected");
        Directory.CreateDirectory(selectedFolder);
        File.WriteAllBytes(Path.Combine(selectedFolder, "DSC0.jpg"), new byte[] { 9 });

        var vm = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC0.jpg"));
        vm.ToggleMark();

        var result = vm.MoveMarkedToSelected();

        Assert.Equal(0, result.MovedFileCount);
        Assert.Single(result.Failures);
        // DSC0 stayed put, so it must still be marked — otherwise the pick is silently lost and
        // the photographer can't just resolve the collision and press N again.
        Assert.Equal("DSC0", vm.CurrentPhoto!.Stem);
        Assert.True(vm.IsCurrentMarked);
    }

    [Fact]
    public void TileSizeIsClampedToReasonableBounds()
    {
        var vm = new MainViewModel(_tempDir, null);

        vm.TileSize = 10;
        Assert.Equal(80, vm.TileSize);

        vm.TileSize = 10000;
        Assert.Equal(480, vm.TileSize);
    }

    [Fact]
    public void IsMarkedReflectsAnyPhotoNotJustTheCurrentOne()
    {
        var vm = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC0.jpg"));
        var otherPhoto = vm.Photos.Single(p => p.Stem == "DSC1");

        Assert.False(vm.IsMarked(otherPhoto));

        vm.JumpTo(vm.Photos.ToList().IndexOf(otherPhoto));
        vm.ToggleMark();

        Assert.True(vm.IsMarked(otherPhoto));
    }
}
