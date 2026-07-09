using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MasterImage.Core;

public sealed class ThumbnailManifestEntry
{
    public DateTime SourceModifiedUtc { get; set; }
    public long SourceSizeBytes { get; set; }
    public string ThumbnailFileName { get; set; } = "";
}

public sealed class ThumbnailManifest
{
    private readonly Dictionary<string, ThumbnailManifestEntry> _entries;

    private ThumbnailManifest(Dictionary<string, ThumbnailManifestEntry> entries)
    {
        _entries = entries;
    }

    public static ThumbnailManifest LoadOrCreate(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return new ThumbnailManifest(new Dictionary<string, ThumbnailManifestEntry>());
        }

        string json = File.ReadAllText(manifestPath);
        var entries = JsonSerializer.Deserialize<Dictionary<string, ThumbnailManifestEntry>>(json)
            ?? new Dictionary<string, ThumbnailManifestEntry>();
        return new ThumbnailManifest(entries);
    }

    public void Save(string manifestPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        string json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, json);
    }

    public bool IsUpToDate(string sourceFilePath, string sourceFileName)
    {
        if (!_entries.TryGetValue(sourceFileName, out var entry))
        {
            return false;
        }

        var info = new FileInfo(sourceFilePath);
        return entry.SourceModifiedUtc == info.LastWriteTimeUtc && entry.SourceSizeBytes == info.Length;
    }

    public string GetOrAssignThumbnailFileName(string sourceFileName)
    {
        if (_entries.TryGetValue(sourceFileName, out var entry))
        {
            return entry.ThumbnailFileName;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceFileName)))[..16] + ".jpg";
    }

    public void Update(string sourceFilePath, string sourceFileName, string thumbnailFileName)
    {
        var info = new FileInfo(sourceFilePath);
        _entries[sourceFileName] = new ThumbnailManifestEntry
        {
            SourceModifiedUtc = info.LastWriteTimeUtc,
            SourceSizeBytes = info.Length,
            ThumbnailFileName = thumbnailFileName
        };
    }

    public void PruneMissing(IReadOnlySet<string> existingSourceFileNames)
    {
        foreach (var key in _entries.Keys.Where(k => !existingSourceFileNames.Contains(k)).ToList())
        {
            _entries.Remove(key);
        }
    }
}
