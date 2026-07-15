using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MasterImage.Core;

public static class PhotoSet
{
    private static readonly string[] StandardExtensions =
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff"
    };

    public static IReadOnlyList<PhotoItem> Load(string folderPath)
    {
        var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSupported)
            .ToList();

        files.Sort(new NaturalSortComparer());

        // Grouping preserves the order files first appear, so the natural sort above still decides
        // the order photos come out in.
        return files
            .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .SelectMany(BuildItems)
            .ToList();
    }

    private static bool IsSupported(string path) =>
        StandardExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()) || RawFormats.IsRaw(path);

    // A camera shooting RAW+JPEG writes two files for one press of the shutter (DSC1.ARW and
    // DSC1.jpg). That's one photo, so it becomes a single PhotoItem holding both paths: one tile,
    // one stop when seeking, one mark, and culling moves both halves together. RAW goes first
    // because PrimaryFilePath is what gets displayed, and the RAW's embedded preview is the
    // camera's own rendering and fast to read (~93ms).
    //
    // Pairing is specifically a RAW-and-its-sidecars relationship, not "shares a stem". Files
    // grouped without any RAW among them (sunset.jpg and sunset.png) are unrelated pictures that
    // happen to share a name, and each stays its own photo.
    private static IEnumerable<PhotoItem> BuildItems(IGrouping<string, string> group)
    {
        var paths = group.ToList();
        var raw = paths.Where(RawFormats.IsRaw).ToList();

        if (raw.Count == 0)
        {
            return paths.Select(path => new PhotoItem(group.Key, new[] { path }));
        }

        return new[]
        {
            new PhotoItem(group.Key, raw.Concat(paths.Where(p => !RawFormats.IsRaw(p))).ToList())
        };
    }
}
