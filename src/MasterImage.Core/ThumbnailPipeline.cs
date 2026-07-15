namespace MasterImage.Core;

public sealed class ThumbnailPipeline
{
    private readonly ThumbnailCache _cache;
    private readonly int _targetPixelWidth;

    // 512 comfortably exceeds the max grid tile size (MainViewModel clamps TileSize to 480), so a
    // cached thumbnail is always at least as large as any tile it's displayed in — the grid
    // downscales, never upscales. This sidesteps a known ThumbnailCache limitation: the cache
    // reuses a thumbnail whenever the source is unchanged, without checking the cached pixel
    // width, so if we generated smaller than the largest tile, Shift+scroll-enlarged tiles (Task
    // 14) would show a blurry upscaled thumbnail. Single-image view does NOT use this pipeline
    // (it calls ImageLoader.TryLoadAtSize directly at ~1920), so 512 only needs to cover the grid.
    public ThumbnailPipeline(ThumbnailCache cache, int targetPixelWidth = 512)
    {
        _cache = cache;
        _targetPixelWidth = targetPixelWidth;
    }

    public Task<System.Windows.Media.Imaging.BitmapSource?> RequestThumbnailAsync(PhotoItem item)
    {
        return Task.Run(() => _cache.GetOrCreateThumbnail(item, _targetPixelWidth));
    }

    public async Task PreloadAllAsync(IReadOnlyList<PhotoItem> items, IProgress<int>? progress = null)
    {
        int workerCount = Math.Max(1, Environment.ProcessorCount);
        using var semaphore = new SemaphoreSlim(workerCount);
        int completed = 0;

        // ConfigureAwait(false) throughout: this is a Core library method with no UI work, but the
        // L handler (Task 15) awaits it from the WPF UI thread. Without it, every continuation
        // (including the finally's Release) marshals back onto the dispatcher — needless UI-thread
        // churn during a large preload, and a hard deadlock if any caller ever .Wait()s it from the
        // UI thread. (Progress<int>.Report is unaffected — it captures its own context separately.)
        var tasks = items.Select(async item =>
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() => _cache.GetOrCreateThumbnail(item, _targetPixelWidth)).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
                progress?.Report(Interlocked.Increment(ref completed));
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
