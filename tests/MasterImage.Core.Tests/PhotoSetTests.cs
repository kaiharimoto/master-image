using System;
using System.IO;
using System.Linq;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class PhotoSetTests : IDisposable
{
    private readonly string _tempDir;

    public PhotoSetTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void OnlyIncludesSupportedExtensionsInNaturalOrder()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, "DSC10.jpg"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(_tempDir, "DSC2.jpg"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(_tempDir, "notes.txt"), new byte[] { 0 });

        var items = PhotoSet.Load(_tempDir);

        Assert.Equal(new[] { "DSC2", "DSC10" }, items.Select(i => i.Stem).ToArray());
    }

    [Fact]
    public void ExcludesSubfolderContents()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, ".thumbnails"));
        File.WriteAllBytes(Path.Combine(_tempDir, ".thumbnails", "cached.jpg"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(_tempDir, "DSC1.jpg"), new byte[] { 0 });

        var items = PhotoSet.Load(_tempDir);

        Assert.Single(items);
        Assert.Equal("DSC1", items[0].Stem);
    }

    [Fact]
    public void EachItemHasOneFilePathInPhaseOne()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, "DSC1.png"), new byte[] { 0 });

        var items = PhotoSet.Load(_tempDir);

        Assert.Single(items[0].FilePaths);
        Assert.Equal(items[0].FilePaths[0], items[0].PrimaryFilePath);
    }
}
