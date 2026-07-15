using System.Diagnostics;
using System.IO;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class ImageLoaderRawTests
{
    // A 61MP Sony ARW shot in PORTRAIT orientation: the sensor pixels are landscape and an EXIF
    // tag says to rotate, which makes it the fixture that catches an unrotated preview.
    private const string PortraitRaw = @"C:\Users\kaihu\Pictures\DSC09423.ARW";

    // A 17MB landscape ARW.
    private const string LandscapeRaw = @"C:\Users\kaihu\Downloads\DSC01565.ARW";

    [Fact]
    public void LoadsARawFileAtTheRequestedWidth()
    {
        if (!File.Exists(LandscapeRaw)) return;

        var image = ImageLoader.TryLoadAtSize(LandscapeRaw, decodePixelWidth: 1920);

        Assert.NotNull(image);
        Assert.Equal(1920, image!.PixelWidth);
        Assert.True(image.IsFrozen, "must be frozen to cross threads");
    }

    [Fact]
    public void AppliesExifRotationToTheEmbeddedPreview()
    {
        if (!File.Exists(PortraitRaw)) return;

        var image = ImageLoader.TryLoadAtSize(PortraitRaw, decodePixelWidth: 1920);

        Assert.NotNull(image);
        // Shot portrait. The embedded preview holds the sensor's landscape pixels plus an
        // orientation tag, so without applying it this comes back landscape and the photo
        // displays on its side.
        Assert.True(image!.PixelHeight > image.PixelWidth,
            $"expected portrait, got {image.PixelWidth}x{image.PixelHeight}");
    }

    [Fact]
    public void UsesTheEmbeddedPreviewRatherThanDemosaicingTheWholeFrame()
    {
        if (!File.Exists(PortraitRaw)) return;

        ImageLoader.TryLoadAtSize(PortraitRaw, decodePixelWidth: 1920); // warm the codec

        var sw = Stopwatch.StartNew();
        var image = ImageLoader.TryLoadAtSize(PortraitRaw, decodePixelWidth: 1920);
        sw.Stop();

        Assert.NotNull(image);
        // A full demosaic of this file measured ~3700ms; the embedded preview measured ~285ms.
        // The threshold sits far enough below the full decode that only the preview path can pass,
        // while leaving generous headroom for a slower machine.
        Assert.True(sw.ElapsedMilliseconds < 1500,
            $"took {sw.ElapsedMilliseconds}ms — looks like the full decode, not the preview");
    }

    [Fact]
    public void LoadsARawFileAtThumbnailSize()
    {
        if (!File.Exists(LandscapeRaw)) return;

        var image = ImageLoader.TryLoadAtSize(LandscapeRaw, decodePixelWidth: 512);

        Assert.NotNull(image);
        Assert.Equal(512, image!.PixelWidth);
    }

    [Fact]
    public void FullResolutionRequestReturnsTheWholePreview()
    {
        if (!File.Exists(LandscapeRaw)) return;

        var image = ImageLoader.TryLoadFullResolution(LandscapeRaw);

        Assert.NotNull(image);
        // Not downscaled, so it's the preview's own size — comfortably bigger than a screen.
        Assert.True(image!.PixelWidth > 1920, $"got {image.PixelWidth}px wide");
    }

    [Fact]
    public void CorruptRawReturnsNullWithoutThrowing()
    {
        string dir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
        try
        {
            string fake = Path.Combine(dir, "broken.arw");
            File.WriteAllText(fake, "this is not a raw file");

            Assert.Null(ImageLoader.TryLoadAtSize(fake, decodePixelWidth: 1920));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
