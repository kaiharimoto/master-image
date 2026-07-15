using System;
using System.IO;
using System.Linq;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class PhotoSetPairingTests : IDisposable
{
    private readonly string _tempDir;

    public PhotoSetPairingTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private void Touch(string name) => File.WriteAllBytes(Path.Combine(_tempDir, name), new byte[] { 0 });

    [Fact]
    public void IncludesRawFiles()
    {
        Touch("DSC1.ARW");

        var items = PhotoSet.Load(_tempDir);

        Assert.Single(items);
        Assert.Equal("DSC1", items[0].Stem);
    }

    [Fact]
    public void PairsARawWithItsSidecarJpegAsOnePhoto()
    {
        Touch("DSC1.ARW");
        Touch("DSC1.jpg");

        var items = PhotoSet.Load(_tempDir);

        // One shot, not two: one tile, one stop when seeking, one mark.
        Assert.Single(items);
        Assert.Equal(2, items[0].FilePaths.Count);
    }

    [Fact]
    public void APairedPhotoDisplaysFromTheRaw()
    {
        Touch("DSC1.ARW");
        Touch("DSC1.jpg");

        var items = PhotoSet.Load(_tempDir);

        Assert.EndsWith(".ARW", items[0].PrimaryFilePath);
    }

    [Fact]
    public void APairCarriesBothFilesSoCullingMovesTheLot()
    {
        Touch("DSC1.ARW");
        Touch("DSC1.jpg");

        var paths = PhotoSet.Load(_tempDir)[0].FilePaths.Select(Path.GetFileName).ToList();

        Assert.Contains("DSC1.ARW", paths);
        Assert.Contains("DSC1.jpg", paths);
    }

    [Fact]
    public void PairingIsCaseInsensitiveOnTheStem()
    {
        // Cameras are not consistent about case, and Windows filenames aren't case-sensitive.
        Touch("dsc1.ARW");
        Touch("DSC1.JPG");

        Assert.Single(PhotoSet.Load(_tempDir));
    }

    [Fact]
    public void UnpairedFilesStayIndependent()
    {
        Touch("DSC1.ARW");
        Touch("DSC2.jpg");

        var items = PhotoSet.Load(_tempDir);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Single(i.FilePaths));
    }

    [Fact]
    public void PairingKeepsNaturalSortOrder()
    {
        Touch("DSC2.ARW");
        Touch("DSC2.jpg");
        Touch("DSC10.ARW");
        Touch("DSC1.jpg");

        var stems = PhotoSet.Load(_tempDir).Select(i => i.Stem).ToArray();

        Assert.Equal(new[] { "DSC1", "DSC2", "DSC10" }, stems);
    }

    [Fact]
    public void TwoNonRawFilesSharingAStemAreNotPaired()
    {
        // sunset.jpg and sunset.png are different pictures that happen to share a name — pairing
        // is specifically a RAW-and-its-sidecar concept, not "same stem".
        Touch("sunset.jpg");
        Touch("sunset.png");

        Assert.Equal(2, PhotoSet.Load(_tempDir).Count);
    }
}
