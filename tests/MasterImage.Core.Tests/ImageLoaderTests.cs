using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    [Fact]
    public void Rotates90DegreeExifOrientationAndSwapsDimensions()
    {
        string path = Path.Combine(_tempDir, "rotated90.jpg");
        TestImageFactory.WriteTestJpegWithOrientation(path, width: 100, height: 60, exifOrientation: 6);

        var result = ImageLoader.TryLoadFullResolution(path);

        Assert.NotNull(result);
        Assert.Equal(60, result!.PixelWidth);
        Assert.Equal(100, result.PixelHeight);
    }

    [Fact]
    public void Rotates180DegreeExifOrientationWithoutSwappingDimensions()
    {
        string path = Path.Combine(_tempDir, "rotated180.jpg");
        TestImageFactory.WriteTestJpegWithOrientation(path, width: 100, height: 60, exifOrientation: 3);

        var result = ImageLoader.TryLoadFullResolution(path);

        Assert.NotNull(result);
        Assert.Equal(100, result!.PixelWidth);
        Assert.Equal(60, result.PixelHeight);
    }

    [Fact]
    public void NormalOrientationIsNotRotated()
    {
        string path = Path.Combine(_tempDir, "normal.jpg");
        TestImageFactory.WriteTestJpegWithOrientation(path, width: 100, height: 60, exifOrientation: 1);

        var result = ImageLoader.TryLoadFullResolution(path);

        Assert.NotNull(result);
        Assert.Equal(100, result!.PixelWidth);
        Assert.Equal(60, result.PixelHeight);
    }

    // Orientation 6 = "rotate 90deg clockwise to display upright." Source has a black LEFT half;
    // under WPF's 90deg-CW RotateTransform the left edge swings to the TOP, so the top of the
    // result must be black and the bottom white. Orientation 8 (270deg) is the mirror of this
    // (black to the BOTTOM). Dimensions alone (both give 60x100) cannot tell 6 from 8 apart —
    // this pixel check is what actually pins the rotation direction.
    [Fact]
    public void Orientation6PutsBlackLeftHalfOnTop()
    {
        string path = Path.Combine(_tempDir, "o6.jpg");
        TestImageFactory.WriteTwoToneJpegWithOrientation(path, width: 100, height: 60, exifOrientation: 6);

        var result = ImageLoader.TryLoadFullResolution(path);

        Assert.NotNull(result);
        Assert.Equal(60, result!.PixelWidth);
        Assert.Equal(100, result.PixelHeight);
        Assert.True(IsDark(result, x: 30, y: 15));    // black (was left) now on top
        Assert.False(IsDark(result, x: 30, y: 85));   // white now on bottom
    }

    [Fact]
    public void Orientation8PutsBlackLeftHalfOnBottom()
    {
        string path = Path.Combine(_tempDir, "o8.jpg");
        TestImageFactory.WriteTwoToneJpegWithOrientation(path, width: 100, height: 60, exifOrientation: 8);

        var result = ImageLoader.TryLoadFullResolution(path);

        Assert.NotNull(result);
        Assert.Equal(60, result!.PixelWidth);
        Assert.Equal(100, result.PixelHeight);
        Assert.False(IsDark(result, x: 30, y: 15));   // white now on top
        Assert.True(IsDark(result, x: 30, y: 85));    // black (was left) now on bottom
    }

    [Fact]
    public void PngWithoutOrientationMetadataLoadsUnrotated()
    {
        string path = Path.Combine(_tempDir, "no-orientation.png");
        TestImageFactory.WriteTestPng(path, width: 100, height: 60);

        var result = ImageLoader.TryLoadFullResolution(path);

        Assert.NotNull(result);
        Assert.Equal(100, result!.PixelWidth);
        Assert.Equal(60, result.PixelHeight);
    }

    private static bool IsDark(BitmapSource image, int x, int y)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, stride: 4, offset: 0);
        int brightness = (pixel[0] + pixel[1] + pixel[2]) / 3; // B, G, R
        return brightness < 128;
    }
}
