using System;
using System.IO;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class MarksStoreTests : IDisposable
{
    private readonly string _tempDir;

    public MarksStoreTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void NewStoreHasNothingMarked()
    {
        var store = MarksStore.LoadOrCreate(_tempDir);
        Assert.False(store.IsMarked("DSC1"));
    }

    [Fact]
    public void ToggleMarksThenUnmarks()
    {
        var store = MarksStore.LoadOrCreate(_tempDir);

        store.Toggle("DSC1");
        Assert.True(store.IsMarked("DSC1"));

        store.Toggle("DSC1");
        Assert.False(store.IsMarked("DSC1"));
    }

    [Fact]
    public void SavedMarksPersistAcrossReload()
    {
        var store = MarksStore.LoadOrCreate(_tempDir);
        store.Toggle("DSC1");
        store.Toggle("DSC5");
        store.Save();

        var reloaded = MarksStore.LoadOrCreate(_tempDir);
        Assert.True(reloaded.IsMarked("DSC1"));
        Assert.True(reloaded.IsMarked("DSC5"));
        Assert.False(reloaded.IsMarked("DSC2"));
    }

    [Fact]
    public void LoadOrCreateRecoversFromCorruptMarksFile()
    {
        File.WriteAllText(Path.Combine(_tempDir, "marks.json"), "{ not valid json !!! ");

        var store = MarksStore.LoadOrCreate(_tempDir);

        Assert.False(store.IsMarked("DSC1"));
    }
}
