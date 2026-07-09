using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MasterImage.Core;

public static class PhotoSet
{
    private static readonly string[] SupportedExtensions =
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff"
    };

    public static IReadOnlyList<PhotoItem> Load(string folderPath)
    {
        var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .ToList();

        files.Sort(new NaturalSortComparer());

        return files
            .Select(path => new PhotoItem(Path.GetFileNameWithoutExtension(path), new[] { path }))
            .ToList();
    }
}
