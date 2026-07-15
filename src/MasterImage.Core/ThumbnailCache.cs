using System.IO;
using System.Windows.Media.Imaging;

namespace MasterImage.Core;

public sealed class ThumbnailCache
{
    private readonly string _manifestPath;
    private readonly ThumbnailManifest _manifest;

    // The thumbnail pipeline (Task 9) calls GetOrCreateThumbnail from many background threads at
    // once, and on-demand tile loads can interleave with an L full-folder preload — two
    // independent concurrent entry points into this one shared cache. The manifest (a plain
    // Dictionary + non-atomic JSON Save) is not thread-safe, so ALL manifest reads/mutations and
    // its Save must happen under this lock. The expensive decode is deliberately left OUTSIDE the
    // lock so thumbnail generation still runs in parallel — only the fast manifest bookkeeping is
    // serialized. Without this, concurrent Save() calls race on manifest.json (sharing-violation
    // IOException) and concurrent Dictionary writes corrupt state.
    private readonly object _manifestLock = new();

    public ThumbnailCache(string folderPath)
    {
        ThumbnailsFolder = Path.Combine(folderPath, ".thumbnails");
        Directory.CreateDirectory(ThumbnailsFolder);
        File.SetAttributes(ThumbnailsFolder, File.GetAttributes(ThumbnailsFolder) | FileAttributes.Hidden);

        _manifestPath = Path.Combine(ThumbnailsFolder, "manifest.json");
        _manifest = ThumbnailManifest.LoadOrCreate(_manifestPath);
    }

    public string ThumbnailsFolder { get; }

    public BitmapSource? GetOrCreateThumbnail(PhotoItem item, int targetPixelWidth)
    {
        string sourcePath = item.PrimaryFilePath;
        string sourceFileName = Path.GetFileName(sourcePath);

        string thumbFileName;
        bool upToDate;
        lock (_manifestLock)
        {
            thumbFileName = _manifest.GetOrAssignThumbnailFileName(sourceFileName);
            upToDate = _manifest.IsUpToDate(sourcePath, sourceFileName);
        }
        string thumbPath = Path.Combine(ThumbnailsFolder, thumbFileName);

        if (upToDate && File.Exists(thumbPath))
        {
            var cached = ImageLoader.TryLoadAtSize(thumbPath, targetPixelWidth);
            if (cached is not null)
            {
                return cached;
            }
            // Cached file exists but is unreadable (e.g. truncated by an interrupted write) —
            // fall through and regenerate from source rather than returning null forever, since
            // IsUpToDate stays true and would never otherwise self-heal.
        }

        var decoded = ImageLoader.TryLoadAtSize(sourcePath, targetPixelWidth);
        if (decoded is null)
        {
            return null;
        }

        ImageLoader.SaveAsJpeg(decoded, thumbPath);
        lock (_manifestLock)
        {
            _manifest.Update(sourcePath, sourceFileName, thumbFileName);
            _manifest.Save(_manifestPath);
        }

        return decoded;
    }

    public void PruneOrphans(IReadOnlyList<PhotoItem> currentItems)
    {
        var existingNames = currentItems.Select(i => Path.GetFileName(i.PrimaryFilePath)).ToHashSet();

        IReadOnlyList<string> orphanedThumbnailFileNames;
        lock (_manifestLock)
        {
            orphanedThumbnailFileNames = _manifest.PruneMissing(existingNames);
            _manifest.Save(_manifestPath);
        }

        foreach (var thumbnailFileName in orphanedThumbnailFileNames)
        {
            string orphanPath = Path.Combine(ThumbnailsFolder, thumbnailFileName);
            try
            {
                if (File.Exists(orphanPath))
                {
                    File.Delete(orphanPath);
                }
            }
            catch (IOException)
            {
                // Thumbnail file is locked (e.g. still held open by the grid, or by Explorer's
                // thumbnail handler) — skip it rather than aborting the whole prune. Consistent
                // with the file-op error handling in ImageLoader and CullOperations.
            }
            catch (UnauthorizedAccessException)
            {
                // Read-only / permission denied — skip, same rationale.
            }
        }
    }
}
