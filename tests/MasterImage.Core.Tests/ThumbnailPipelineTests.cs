using System;
using System.IO;
using System.Threading.Tasks;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class ThumbnailPipelineTests : IDisposable
{
    private readonly string _tempDir;

    public ThumbnailPipelineTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task RequestThumbnailAsyncReturnsADecodedThumbnail()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 400, height: 300);
        var item = new PhotoItem("DSC1", new[] { sourcePath });

        var pipeline = new ThumbnailPipeline(new ThumbnailCache(_tempDir));
        var thumbnail = await pipeline.RequestThumbnailAsync(item);

        Assert.NotNull(thumbnail);
    }

    [Fact]
    public async Task PreloadAllAsyncGeneratesThumbnailsForEveryItem()
    {
        var items = new PhotoItem[3];
        for (int i = 0; i < 3; i++)
        {
            string path = Path.Combine(_tempDir, $"DSC{i}.jpg");
            TestImageFactory.WriteTestJpeg(path, width: 200, height: 150);
            items[i] = new PhotoItem($"DSC{i}", new[] { path });
        }

        var cache = new ThumbnailCache(_tempDir);
        var pipeline = new ThumbnailPipeline(cache);

        await pipeline.PreloadAllAsync(items);

        Assert.Equal(3, Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg").Length);
    }

    [Fact]
    public async Task PreloadAllAsyncReportsProgressForEveryItem()
    {
        var items = new PhotoItem[3];
        for (int i = 0; i < 3; i++)
        {
            string path = Path.Combine(_tempDir, $"DSC{i}.jpg");
            TestImageFactory.WriteTestJpeg(path, width: 200, height: 150);
            items[i] = new PhotoItem($"DSC{i}", new[] { path });
        }

        var pipeline = new ThumbnailPipeline(new ThumbnailCache(_tempDir));
        int reportCount = 0;
        var progress = new Progress<int>(_ => reportCount++);

        await pipeline.PreloadAllAsync(items, progress);
        await Task.Delay(50); // let the last Progress<T> callback marshal through

        Assert.Equal(3, reportCount);
    }
}
