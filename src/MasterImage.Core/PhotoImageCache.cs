using System.Windows.Media.Imaging;

namespace MasterImage.Core;

// A small in-memory LRU of decoded, display-ready images.
//
// Decoding a 40MB camera JPEG costs hundreds of milliseconds even when targeting a reduced size,
// because WIC still has to read and parse the whole file. Culling seeks constantly (right, right,
// back, right...), so without this every keypress pays that cost again — including for photos
// viewed seconds earlier. Holding a handful of decoded frames makes revisits instant, and lets the
// caller warm the neighbours of the current photo so a forward seek is already done before it's
// asked for.
//
// Entries store the decode *Task*, not its result, so a prefetch that's still in flight is awaited
// rather than started a second time when the user seeks onto it.
public sealed class PhotoImageCache
{
    private readonly int _capacity;
    private readonly int _decodePixelWidth;
    private readonly object _lock = new();
    private readonly Dictionary<string, Task<BitmapSource?>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recent = new();

    public PhotoImageCache(int capacity = 7, int decodePixelWidth = 1920)
    {
        _capacity = Math.Max(1, capacity);
        _decodePixelWidth = decodePixelWidth;
    }

    public int Count
    {
        get { lock (_lock) { return _entries.Count; } }
    }

    // Returns the decoded image for this photo, starting a decode only if one isn't cached or
    // already running. Safe to call from any thread.
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

            var decode = Task.Run(() => ImageLoader.TryLoadAtSize(key, _decodePixelWidth));
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

    // Warm an entry without waiting on it. Identical to GetAsync but expresses intent at the call
    // site and makes the discarded task explicit rather than an accident.
    public void Prefetch(PhotoItem item) => _ = GetAsync(item);
}
