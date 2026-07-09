using System;
using System.IO;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class ImageLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public ImageLoaderTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void LoadsAtRequestedDecodePixelWidth()
    {
        string path = Path.Combine(_tempDir, "test.jpg");
        TestImageFactory.WriteTestJpeg(path, width: 640, height: 480);

        var result = ImageLoader.TryLoadAtSize(path, decodePixelWidth: 100);

        Assert.NotNull(result);
        Assert.Equal(100, result!.PixelWidth);
    }

    [Fact]
    public void ReturnsNullForUnsupportedOrMissingFile()
    {
        string path = Path.Combine(_tempDir, "not-an-image.txt");
        File.WriteAllText(path, "hello");

        var result = ImageLoader.TryLoadAtSize(path, decodePixelWidth: 100);

        Assert.Null(result);
    }

    [Fact]
    public void SaveAsJpegWritesAReloadableFile()
    {
        string sourcePath = Path.Combine(_tempDir, "source.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 200, height: 150);
        var decoded = ImageLoader.TryLoadAtSize(sourcePath, decodePixelWidth: 100)!;

        string destPath = Path.Combine(_tempDir, "out", "thumb.jpg");
        ImageLoader.SaveAsJpeg(decoded, destPath);

        var reloaded = ImageLoader.TryLoadAtSize(destPath, decodePixelWidth: 100);
        Assert.NotNull(reloaded);
    }
}
