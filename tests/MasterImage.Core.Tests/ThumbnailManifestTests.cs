using System;
using System.IO;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class ThumbnailManifestTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _manifestPath;

    public ThumbnailManifestTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
        _manifestPath = Path.Combine(_tempDir, "manifest.json");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void NewSourceFileIsNotUpToDate()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        File.WriteAllBytes(sourcePath, new byte[] { 1, 2, 3 });

        var manifest = ThumbnailManifest.LoadOrCreate(_manifestPath);

        Assert.False(manifest.IsUpToDate(sourcePath, "DSC1.jpg"));
    }

    [Fact]
    public void UpdatedEntryIsUpToDateUntilSourceChanges()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        File.WriteAllBytes(sourcePath, new byte[] { 1, 2, 3 });

        var manifest = ThumbnailManifest.LoadOrCreate(_manifestPath);
        manifest.Update(sourcePath, "DSC1.jpg", "abc123.jpg");

        Assert.True(manifest.IsUpToDate(sourcePath, "DSC1.jpg"));

        File.WriteAllBytes(sourcePath, new byte[] { 1, 2, 3, 4, 5 });
        Assert.False(manifest.IsUpToDate(sourcePath, "DSC1.jpg"));
    }

    [Fact]
    public void SavedManifestReloadsWithSameEntries()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        File.WriteAllBytes(sourcePath, new byte[] { 1, 2, 3 });

        var manifest = ThumbnailManifest.LoadOrCreate(_manifestPath);
        manifest.Update(sourcePath, "DSC1.jpg", "abc123.jpg");
        manifest.Save(_manifestPath);

        var reloaded = ThumbnailManifest.LoadOrCreate(_manifestPath);
        Assert.True(reloaded.IsUpToDate(sourcePath, "DSC1.jpg"));
        Assert.Equal("abc123.jpg", reloaded.GetOrAssignThumbnailFileName("DSC1.jpg"));
    }

    [Fact]
    public void GetOrAssignThumbnailFileNameIsStableAcrossCalls()
    {
        var manifest = ThumbnailManifest.LoadOrCreate(_manifestPath);

        string first = manifest.GetOrAssignThumbnailFileName("DSC1.jpg");
        string second = manifest.GetOrAssignThumbnailFileName("DSC1.jpg");

        Assert.Equal(first, second);
    }

    [Fact]
    public void PruneMissingRemovesEntriesNotInExistingSetAndReturnsTheirThumbnailFileNames()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        File.WriteAllBytes(sourcePath, new byte[] { 1 });

        var manifest = ThumbnailManifest.LoadOrCreate(_manifestPath);
        manifest.Update(sourcePath, "DSC1.jpg", "abc123.jpg");

        var removed = manifest.PruneMissing(new HashSet<string>());

        Assert.False(manifest.IsUpToDate(sourcePath, "DSC1.jpg"));
        Assert.Equal(new[] { "abc123.jpg" }, removed);
    }

    [Fact]
    public void PruneMissingKeepsEntriesStillInExistingSet()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        File.WriteAllBytes(sourcePath, new byte[] { 1 });

        var manifest = ThumbnailManifest.LoadOrCreate(_manifestPath);
        manifest.Update(sourcePath, "DSC1.jpg", "abc123.jpg");

        var removed = manifest.PruneMissing(new HashSet<string> { "DSC1.jpg" });

        Assert.Empty(removed);
        Assert.True(manifest.IsUpToDate(sourcePath, "DSC1.jpg"));
    }

    [Fact]
    public void LoadOrCreateRecoversFromCorruptManifestFile()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        File.WriteAllBytes(sourcePath, new byte[] { 1 });
        File.WriteAllText(_manifestPath, "{ not valid json !!! ");

        var manifest = ThumbnailManifest.LoadOrCreate(_manifestPath);

        Assert.False(manifest.IsUpToDate(sourcePath, "DSC1.jpg"));
    }
}
