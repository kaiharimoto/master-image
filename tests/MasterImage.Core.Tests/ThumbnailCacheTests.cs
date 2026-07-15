using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class ThumbnailCacheTests : IDisposable
{
    private readonly string _tempDir;

    public ThumbnailCacheTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void GeneratesAndCachesAThumbnailOnDisk()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 640, height: 480);
        var item = new PhotoItem("DSC1", new[] { sourcePath });

        var cache = new ThumbnailCache(_tempDir);
        var thumbnail = cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);

        Assert.NotNull(thumbnail);
        Assert.Equal(100, thumbnail!.PixelWidth);
        Assert.Single(Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg"));
    }

    [Fact]
    public void ThumbnailsFolderIsHidden()
    {
        var cache = new ThumbnailCache(_tempDir);
        var attributes = File.GetAttributes(cache.ThumbnailsFolder);
        Assert.True((attributes & FileAttributes.Hidden) == FileAttributes.Hidden);
    }

    [Fact]
    public void ReusesCachedThumbnailWhenSourceUnchanged()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 640, height: 480);
        var item = new PhotoItem("DSC1", new[] { sourcePath });

        var cache = new ThumbnailCache(_tempDir);
        cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);
        string thumbPath = Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg")[0];
        DateTime firstWriteTime = File.GetLastWriteTimeUtc(thumbPath);

        System.Threading.Thread.Sleep(50);
        cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);

        Assert.Equal(firstWriteTime, File.GetLastWriteTimeUtc(thumbPath));
    }

    [Fact]
    public void RegeneratesThumbnailWhenSourceFileChanges()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 640, height: 480);
        var item = new PhotoItem("DSC1", new[] { sourcePath });

        var cache = new ThumbnailCache(_tempDir);
        cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);
        string thumbPath = Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg")[0];
        DateTime firstWriteTime = File.GetLastWriteTimeUtc(thumbPath);

        System.Threading.Thread.Sleep(1100); // ensure a distinguishable mtime
        TestImageFactory.WriteTestJpeg(sourcePath, width: 800, height: 600);
        cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);

        Assert.NotEqual(firstWriteTime, File.GetLastWriteTimeUtc(thumbPath));
    }

    [Fact]
    public void PruneOrphansDeletesCachedThumbnailForARemovedSource()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 640, height: 480);
        var item = new PhotoItem("DSC1", new[] { sourcePath });

        var cache = new ThumbnailCache(_tempDir);
        cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);
        Assert.Single(Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg"));

        cache.PruneOrphans(new List<PhotoItem>());

        Assert.Empty(Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg"));
    }

    [Fact]
    public void PruneOrphansKeepsThumbnailForAStillPresentSource()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 640, height: 480);
        var item = new PhotoItem("DSC1", new[] { sourcePath });

        var cache = new ThumbnailCache(_tempDir);
        cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);

        cache.PruneOrphans(new List<PhotoItem> { item });

        Assert.Single(Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg"));
    }

    // Mirrors how the Task 9 pipeline drives the cache: many background threads calling
    // GetOrCreateThumbnail on one shared cache instance at once. Without the manifest lock this
    // faults reproducibly (concurrent manifest.json writes throw a sharing-violation IOException,
    // and concurrent Dictionary mutation corrupts state) — this test pins the fix.
    [Fact]
    public void GeneratesThumbnailsConcurrentlyWithoutError()
    {
        var items = new List<PhotoItem>();
        for (int i = 0; i < 24; i++)
        {
            string p = Path.Combine(_tempDir, $"DSC{i}.jpg");
            TestImageFactory.WriteTestJpeg(p, width: 320, height: 240);
            items.Add(new PhotoItem($"DSC{i}", new[] { p }));
        }

        var cache = new ThumbnailCache(_tempDir);

        var exception = Record.Exception(() =>
            Parallel.ForEach(items, item => cache.GetOrCreateThumbnail(item, targetPixelWidth: 100)));

        Assert.Null(exception);
        Assert.Equal(24, Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg").Length);
    }

    [Fact]
    public void RegeneratesWhenCachedThumbnailFileIsCorrupt()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 640, height: 480);
        var item = new PhotoItem("DSC1", new[] { sourcePath });

        var cache = new ThumbnailCache(_tempDir);
        cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);
        string thumbPath = Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg")[0];

        File.WriteAllText(thumbPath, "not a jpeg");

        var result = cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);

        Assert.NotNull(result);
    }
}
