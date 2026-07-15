using System.Windows.Media.Imaging;

namespace MasterImage.Core;

// An in-memory LRU of decoded, display-ready images, sized to hold as much of a shoot as the
// machine can comfortably spare.
//
// Decoding a 40MB camera JPEG costs ~1 second even when targeting a reduced size, because WIC must
// read and parse essentially the whole file. Culling seeks constantly, so decoding on demand means
// a visible stall on every keypress. Instead the caller reads ahead around the current photo and
// this holds the results, making both forward seeks and revisits instant.
//
// Entries store the decode *Task* rather than its result, so a read-ahead still in flight is
// awaited rather than started a second time when the user seeks onto it. Decodes are bounded by a
// semaphore: read-ahead can queue dozens of photos, and without a limit each would burn a
// thread-pool thread blocking on WIC.
public sealed class PhotoImageCache
{
    private readonly int _capacity;
    private readonly int _decodePixelWidth;
    private readonly SemaphoreSlim _decodeSlots;
    private readonly object _lock = new();
    private readonly Dictionary<string, Task<BitmapSource?>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recent = new();

    public PhotoImageCache(int? capacity = null, int decodePixelWidth = 1920, int? maxConcurrentDecodes = null)
    {
        _capacity = Math.Max(1, capacity ?? RecommendedCapacity());
        _decodePixelWidth = decodePixelWidth;
        _decodeSlots = new SemaphoreSlim(Math.Max(1, maxConcurrentDecodes ?? Environment.ProcessorCount));
    }

    // How many frames we can hold without being a bad citizen. A decoded frame at ~1920px on the
    // long edge is roughly 10MB (1280 x 1920 x 4 bytes for a typical 3:2 camera photo). Spend at
    // most a quarter of the machine's memory on this, capped so a very large machine doesn't hand
    // over an absurd amount — 800 frames already covers a whole shoot.
    public static int RecommendedCapacity()
    {
        const long BytesPerFrame = 10L * 1024 * 1024;
        const long HardCap = 8L * 1024 * 1024 * 1024;

        long available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        long budget = Math.Min(available / 4, HardCap);

        return (int)Math.Clamp(budget / BytesPerFrame, 8, 800);
    }

    public int Capacity => _capacity;

    public int Count
    {
        get { lock (_lock) { return _entries.Count; } }
    }

    // Returns the decoded image for this photo, starting a decode only if one isn't already cached
    // or running. Safe to call from any thread.
    public Task<BitmapSource?> GetAsync(PhotoItem item)
    {
        string key = item.PrimaryFilePath;

        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _recent.Remove(key);
                _recent.AddFirst(key);
                return existing;
            }

            var decode = DecodeAsync(key);
            _entries[key] = decode;
            _recent.AddFirst(key);

            while (_recent.Count > _capacity)
            {
                string evicted = _recent.Last!.Value;
                _recent.RemoveLast();
                _entries.Remove(evicted);
            }

            return decode;
        }
    }

    // Warm an entry without waiting on it. Same work as GetAsync, but says so at the call site and
    // makes the discarded task deliberate rather than an oversight.
    public void Prefetch(PhotoItem item) => _ = GetAsync(item);

    private async Task<BitmapSource?> DecodeAsync(string path)
    {
        await _decodeSlots.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() => ImageLoader.TryLoadAtSize(path, _decodePixelWidth)).ConfigureAwait(false);
        }
        finally
        {
            _decodeSlots.Release();
        }
    }
}
