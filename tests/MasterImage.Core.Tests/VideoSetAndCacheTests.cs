using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class VideoSetAndCacheTests : IDisposable
{
    private readonly string _tempDir;

    public VideoSetAndCacheTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageVideoTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // The bytes don't matter for set-membership and cache-routing: both dispatch on extension, and
    // nothing here decodes. A real clip is only needed for the poster-frame test.
    private string WriteFakeVideo(string name)
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3 });
        return path;
    }

    [Fact]
    public void VideosAppearInThePhotoSet()
    {
        WriteFakeVideo("CLIP1.mp4");
        TestImageFactory.WriteTestJpeg(Path.Combine(_tempDir, "DSC1.jpg"), 40, 30);

        var items = PhotoSet.Load(_tempDir);

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => Path.GetFileName(i.PrimaryFilePath) == "CLIP1.mp4");
    }

    [Fact]
    public void AVideoIsNotPairedWithASameStemStill()
    {
        // A camera writing DSC1.MP4 and DSC1.JPG made a clip and a still, not two renderings of one
        // shutter press. Pairing them would hide one behind the other and move both on a cull.
        WriteFakeVideo("DSC1.mp4");
        TestImageFactory.WriteTestJpeg(Path.Combine(_tempDir, "DSC1.jpg"), 40, 30);

        var items = PhotoSet.Load(_tempDir);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Single(i.FilePaths));
    }

    [Fact]
    public async Task TheImageCacheReturnsNullForAVideoWithoutQueuingADecode()
    {
        var cache = new PhotoImageCache(capacity: 8);
        var video = new PhotoItem("CLIP1", new[] { WriteFakeVideo("CLIP1.mp4") });

        var result = await cache.GetAsync(video);

        Assert.Null(result);

        // The point isn't the null — it's that no decode slot was spent. Prefetch queues 20 items
        // ahead, so a video that entered the cache would burn slots on work guaranteed to fail and
        // stall real photo read-ahead behind it.
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void PrefetchingAVideoIsANoOp()
    {
        var cache = new PhotoImageCache(capacity: 8);
        var video = new PhotoItem("CLIP1", new[] { WriteFakeVideo("CLIP1.mov") });

        cache.Prefetch(video);

        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task StillsStillEnterTheCacheNormally()
    {
        var cache = new PhotoImageCache(capacity: 8);
        string jpeg = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(jpeg, 40, 30);
        var photo = new PhotoItem("DSC1", new[] { jpeg });

        var result = await cache.GetAsync(photo);

        // Guards the early-return above from over-reaching.
        Assert.NotNull(result);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void AnUnreadableVideoYieldsNoThumbnailRatherThanAFiletypeIcon()
    {
        // These four bytes aren't a video, so the Shell has no thumbnail for them. THUMBNAILONLY
        // means we get nothing back — an icon here would sit in the grid looking like a real frame.
        var frame = VideoThumbnail.TryExtract(WriteFakeVideo("BROKEN.mp4"), 512);

        Assert.Null(frame);
    }
}
