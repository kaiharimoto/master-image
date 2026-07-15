using System.IO;
using System.Windows.Media.Imaging;

namespace MasterImage.Core;

public sealed class ThumbnailCache
{
    private readonly string _manifestPath;
    private readonly ThumbnailManifest _manifest;

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
        string thumbFileName = _manifest.GetOrAssignThumbnailFileName(sourceFileName);
        string thumbPath = Path.Combine(ThumbnailsFolder, thumbFileName);

        if (_manifest.IsUpToDate(sourcePath, sourceFileName) && File.Exists(thumbPath))
        {
            return ImageLoader.TryLoadAtSize(thumbPath, targetPixelWidth);
        }

        var decoded = ImageLoader.TryLoadAtSize(sourcePath, targetPixelWidth);
        if (decoded is null)
        {
            return null;
        }

        ImageLoader.SaveAsJpeg(decoded, thumbPath);
        _manifest.Update(sourcePath, sourceFileName, thumbFileName);
        _manifest.Save(_manifestPath);

        return decoded;
    }

    public void PruneOrphans(IReadOnlyList<PhotoItem> currentItems)
    {
        var existingNames = currentItems.Select(i => Path.GetFileName(i.PrimaryFilePath)).ToHashSet();
        var orphanedThumbnailFileNames = _manifest.PruneMissing(existingNames);
        _manifest.Save(_manifestPath);

        foreach (var thumbnailFileName in orphanedThumbnailFileNames)
        {
            string orphanPath = Path.Combine(ThumbnailsFolder, thumbnailFileName);
            if (File.Exists(orphanPath))
            {
                File.Delete(orphanPath);
            }
        }
    }
}
