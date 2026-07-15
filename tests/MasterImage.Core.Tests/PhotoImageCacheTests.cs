using System;
using System.IO;
using System.Threading.Tasks;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class PhotoImageCacheTests : IDisposable
{
    private readonly string _tempDir;

    public PhotoImageCacheTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private PhotoItem WritePhoto(string name)
    {
        string path = Path.Combine(_tempDir, name);
        TestImageFactory.WriteTestJpeg(path, width: 400, height: 300);
        return new PhotoItem(Path.GetFileNameWithoutExtension(name), new[] { path });
    }

    [Fact]
    public async Task ReturnsADecodedImage()
    {
        var cache = new PhotoImageCache();

        var image = await cache.GetAsync(WritePhoto("DSC1.jpg"));

        Assert.NotNull(image);
    }

    [Fact]
    public async Task RepeatedGetReusesTheSameDecodeInsteadOfStartingAnother()
    {
        var cache = new PhotoImageCache();
        var item = WritePhoto("DSC1.jpg");

        var first = cache.GetAsync(item);
        var second = cache.GetAsync(item);

        // Same Task instance means the second seek onto this photo rode the cached decode rather
        // than paying for it again — the whole point of the cache.
        Assert.Same(first, second);
        Assert.Equal(1, cache.Count);
        await first;
    }

    [Fact]
    public async Task PrefetchIsReusedByASubsequentGet()
    {
        var cache = new PhotoImageCache();
        var item = WritePhoto("DSC1.jpg");

        cache.Prefetch(item);
        var viaGet = cache.GetAsync(item);

        Assert.Equal(1, cache.Count);
        Assert.NotNull(await viaGet);
    }

    [Fact]
    public async Task EvictsLeastRecentlyUsedBeyondCapacity()
    {
        var cache = new PhotoImageCache(capacity: 2);
        var a = WritePhoto("A.jpg");
        var b = WritePhoto("B.jpg");
        var c = WritePhoto("C.jpg");

        var aTask = cache.GetAsync(a);
        await aTask;
        await cache.GetAsync(b);
        await cache.GetAsync(c); // pushes A out

        Assert.Equal(2, cache.Count);

        var aAgain = cache.GetAsync(a);
        Assert.NotSame(aTask, aAgain); // A was evicted, so it decodes afresh
        await aAgain;
    }

    [Fact]
    public async Task RecentlyUsedEntrySurvivesEviction()
    {
        var cache = new PhotoImageCache(capacity: 2);
        var a = WritePhoto("A.jpg");
        var b = WritePhoto("B.jpg");
        var c = WritePhoto("C.jpg");

        var aTask = cache.GetAsync(a);
        await aTask;
        await cache.GetAsync(b);
        await cache.GetAsync(a);  // touch A so B becomes the least-recently-used
        await cache.GetAsync(c);  // evicts B, not A

        Assert.Same(aTask, cache.GetAsync(a));
    }

    [Fact]
    public async Task UndecodableFileYieldsNullWithoutThrowing()
    {
        string path = Path.Combine(_tempDir, "not-an-image.txt");
        File.WriteAllText(path, "hello");
        var item = new PhotoItem("not-an-image", new[] { path });

        var image = await new PhotoImageCache().GetAsync(item);

        Assert.Null(image);
    }
}
