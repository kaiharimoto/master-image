# Master Image — Core Viewer (Plan 1 of 4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a fully working, keyboard-driven photo viewer for standard image formats (JPEG/PNG/GIF/BMP/WebP/TIFF) — seek, zoom/pan, tile-grid overview, thumbnail caching, mark/cull workflow, fullscreen — as a complete, usable .NET 8 WPF app. RAW support, GPU acceleration, and the installer are separate follow-on plans.

**Architecture:** A `MasterImage.Core` class library holds all non-UI logic (folder scanning/sorting, thumbnail cache + on-disk manifest, marks persistence, cull/move operation, WIC-based image decode) built test-first with xUnit. A `MasterImage.App` WPF project consumes Core and implements the window, views, and keyboard/mouse interaction model. UI logic that doesn't require actual rendering (navigation, mark toggling, tile-size clamping, mode switches) lives in a plain `MainViewModel` class that's also unit-testable; visual/interaction pieces (XAML views, zoom/pan, virtualized grid) are verified by manually running the app.

**Tech Stack:** .NET 8, WPF (`net8.0-windows`, `UseWPF=true`), WIC (`System.Windows.Media.Imaging`) for decode, `System.Threading.Channels`/`SemaphoreSlim` for the background thumbnail pipeline, xUnit for tests.

**Known caveat carried into this plan:** WIC's built-in codecs cover JPEG/PNG/GIF/BMP/TIFF natively on any Windows install. `.webp` decoding depends on the "WebP Image Extensions" package from the Microsoft Store being installed — if it's missing, `BitmapDecoder` throws and this plan's `ImageLoader` catches that and treats the file as undecodable (shown as a broken-image placeholder) rather than crashing. Nothing to install to build/run this plan; it only affects whether actual `.webp` files preview correctly on a given machine.

**Three behaviors resolved here (not explicit in the spec) — flagging for visibility:**
- Left/Right seeking **wraps around** at the folder's ends (pressing Right on the last photo goes to the first, and vice versa) rather than stopping.
- After `N` moves marked photos to `selected/`, the in-memory photo list is reloaded from disk and the current index is clamped back into range if it moved past the new end.
- Marks are keyed by full filename (with extension), not by `PhotoItem.Stem` — found during Task 3's code review: `PhotoSet.Load` (Phase 1) emits one `PhotoItem` per file, so two files sharing a stem across different extensions (e.g. `sunset.jpg` and `sunset.png`) would otherwise collide on a bare-stem mark key and incorrectly share mark state. See `MarkKey` in Task 10.

---

## Task 1: Solution & project scaffolding

**Files:**
- Create: `MasterImage.sln`
- Create: `src/MasterImage.Core/MasterImage.Core.csproj`
- Create: `src/MasterImage.App/MasterImage.App.csproj` (+ default template files)
- Create: `tests/MasterImage.Core.Tests/MasterImage.Core.Tests.csproj`

- [ ] **Step 1: Scaffold the three projects and solution**

Run from the repo root (`C:\Users\kaihu\Documents\projects\image viewer`):

```bash
dotnet new sln -n MasterImage
dotnet new classlib -n MasterImage.Core -o src/MasterImage.Core
dotnet new wpf -n MasterImage.App -o src/MasterImage.App
dotnet new xunit -n MasterImage.Core.Tests -o tests/MasterImage.Core.Tests
dotnet sln add src/MasterImage.Core/MasterImage.Core.csproj
dotnet sln add src/MasterImage.App/MasterImage.App.csproj
dotnet sln add tests/MasterImage.Core.Tests/MasterImage.Core.Tests.csproj
dotnet add src/MasterImage.App/MasterImage.App.csproj reference src/MasterImage.Core/MasterImage.Core.csproj
dotnet add tests/MasterImage.Core.Tests/MasterImage.Core.Tests.csproj reference src/MasterImage.Core/MasterImage.Core.csproj
```

- [ ] **Step 2: Edit `src/MasterImage.Core/MasterImage.Core.csproj` to enable WPF imaging types and nullable/implicit usings**

Replace its contents with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>MasterImage.Core</RootNamespace>
  </PropertyGroup>

</Project>
```

- [ ] **Step 3: Edit `tests/MasterImage.Core.Tests/MasterImage.Core.Tests.csproj` to also enable WPF imaging types**

Replace its contents with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>MasterImage.Core.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\MasterImage.Core\MasterImage.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Delete the default template files that won't be used**

```bash
rm src/MasterImage.Core/Class1.cs
rm tests/MasterImage.Core.Tests/UnitTest1.cs
```

- [ ] **Step 5: Build to confirm the scaffold is sound**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` across all three projects.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Scaffold MasterImage solution (Core, App, Tests)"
```

---

## Task 2: Natural sort comparer

**Files:**
- Create: `src/MasterImage.Core/NaturalSortComparer.cs`
- Test: `tests/MasterImage.Core.Tests/NaturalSortComparerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MasterImage.Core.Tests/NaturalSortComparerTests.cs
using System.Collections.Generic;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class NaturalSortComparerTests
{
    [Fact]
    public void SortsNumericSuffixesNumerically()
    {
        var input = new List<string> { "DSC10.jpg", "DSC2.jpg", "DSC1.jpg" };
        input.Sort(new NaturalSortComparer());
        Assert.Equal(new[] { "DSC1.jpg", "DSC2.jpg", "DSC10.jpg" }, input);
    }

    [Fact]
    public void FallsBackToOrdinalForNonNumericParts()
    {
        var input = new List<string> { "banana.jpg", "apple.jpg" };
        input.Sort(new NaturalSortComparer());
        Assert.Equal(new[] { "apple.jpg", "banana.jpg" }, input);
    }

    [Fact]
    public void HandlesMixedAlphaNumericPrefixes()
    {
        var input = new List<string> { "IMG_2.jpg", "DSC_1.jpg", "IMG_10.jpg" };
        input.Sort(new NaturalSortComparer());
        Assert.Equal(new[] { "DSC_1.jpg", "IMG_2.jpg", "IMG_10.jpg" }, input);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MasterImage.Core.Tests --filter NaturalSortComparerTests`
Expected: FAIL to compile — `NaturalSortComparer` does not exist.

- [ ] **Step 3: Implement `NaturalSortComparer`**

```csharp
// src/MasterImage.Core/NaturalSortComparer.cs
using System.Text.RegularExpressions;

namespace MasterImage.Core;

public sealed class NaturalSortComparer : IComparer<string>
{
    private static readonly Regex ChunkPattern = new(@"\d+|\D+", RegexOptions.Compiled);

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return string.Compare(x, y, StringComparison.Ordinal);
        }

        var xChunks = ChunkPattern.Matches(x);
        var yChunks = ChunkPattern.Matches(y);
        int count = Math.Min(xChunks.Count, yChunks.Count);

        for (int i = 0; i < count; i++)
        {
            string xChunk = xChunks[i].Value;
            string yChunk = yChunks[i].Value;

            bool xIsDigits = char.IsDigit(xChunk[0]);
            bool yIsDigits = char.IsDigit(yChunk[0]);

            if (xIsDigits && yIsDigits &&
                long.TryParse(xChunk, out long xNum) &&
                long.TryParse(yChunk, out long yNum) &&
                xNum != yNum)
            {
                return xNum.CompareTo(yNum);
            }

            int chunkCompare = string.Compare(xChunk, yChunk, StringComparison.OrdinalIgnoreCase);
            if (chunkCompare != 0)
            {
                return chunkCompare;
            }
        }

        return xChunks.Count.CompareTo(yChunks.Count);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MasterImage.Core.Tests --filter NaturalSortComparerTests`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add src/MasterImage.Core/NaturalSortComparer.cs tests/MasterImage.Core.Tests/NaturalSortComparerTests.cs
git commit -m "Add natural sort comparer for photo ordering"
```

---

## Task 3: PhotoItem + PhotoSet folder scanning

**Files:**
- Create: `src/MasterImage.Core/PhotoItem.cs`
- Create: `src/MasterImage.Core/PhotoSet.cs`
- Test: `tests/MasterImage.Core.Tests/PhotoSetTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MasterImage.Core.Tests/PhotoSetTests.cs
using System;
using System.IO;
using System.Linq;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class PhotoSetTests : IDisposable
{
    private readonly string _tempDir;

    public PhotoSetTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void OnlyIncludesSupportedExtensionsInNaturalOrder()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, "DSC10.jpg"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(_tempDir, "DSC2.jpg"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(_tempDir, "notes.txt"), new byte[] { 0 });

        var items = PhotoSet.Load(_tempDir);

        Assert.Equal(new[] { "DSC2", "DSC10" }, items.Select(i => i.Stem).ToArray());
    }

    [Fact]
    public void ExcludesSubfolderContents()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, ".thumbnails"));
        File.WriteAllBytes(Path.Combine(_tempDir, ".thumbnails", "cached.jpg"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(_tempDir, "DSC1.jpg"), new byte[] { 0 });

        var items = PhotoSet.Load(_tempDir);

        Assert.Single(items);
        Assert.Equal("DSC1", items[0].Stem);
    }

    [Fact]
    public void EachItemHasOneFilePathInPhaseOne()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, "DSC1.png"), new byte[] { 0 });

        var items = PhotoSet.Load(_tempDir);

        Assert.Single(items[0].FilePaths);
        Assert.Equal(items[0].FilePaths[0], items[0].PrimaryFilePath);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MasterImage.Core.Tests --filter PhotoSetTests`
Expected: FAIL to compile — `PhotoItem`/`PhotoSet` do not exist.

- [ ] **Step 3: Implement `PhotoItem`**

```csharp
// src/MasterImage.Core/PhotoItem.cs
namespace MasterImage.Core;

public sealed class PhotoItem
{
    public PhotoItem(string stem, IReadOnlyList<string> filePaths)
    {
        Stem = stem;
        FilePaths = filePaths;
    }

    public string Stem { get; }
    public IReadOnlyList<string> FilePaths { get; }
    public string PrimaryFilePath => FilePaths[0];
}
```

- [ ] **Step 4: Implement `PhotoSet`**

```csharp
// src/MasterImage.Core/PhotoSet.cs
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
```

`SearchOption.TopDirectoryOnly` is what gives us "same folder only, non-recursive" from §4 of the spec — `.thumbnails` and `selected` are subfolders, so their contents are never enumerated here.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MasterImage.Core.Tests --filter PhotoSetTests`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 6: Commit**

```bash
git add src/MasterImage.Core/PhotoItem.cs src/MasterImage.Core/PhotoSet.cs tests/MasterImage.Core.Tests/PhotoSetTests.cs
git commit -m "Add PhotoItem/PhotoSet folder scanning and sorting"
```

---

## Task 4: Thumbnail manifest (staleness tracking)

**Files:**
- Create: `src/MasterImage.Core/ThumbnailManifest.cs`
- Test: `tests/MasterImage.Core.Tests/ThumbnailManifestTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MasterImage.Core.Tests/ThumbnailManifestTests.cs
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MasterImage.Core.Tests --filter ThumbnailManifestTests`
Expected: FAIL to compile — `ThumbnailManifest` does not exist.

- [ ] **Step 3: Implement `ThumbnailManifest`**

```csharp
// src/MasterImage.Core/ThumbnailManifest.cs
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

        try
        {
            string json = File.ReadAllText(manifestPath);
            var entries = JsonSerializer.Deserialize<Dictionary<string, ThumbnailManifestEntry>>(json)
                ?? new Dictionary<string, ThumbnailManifestEntry>();
            return new ThumbnailManifest(entries);
        }
        catch (JsonException)
        {
            // A crash or power loss mid-write can leave manifest.json truncated/corrupt.
            // Treat it the same as "missing" rather than taking down the whole cache permanently.
            return new ThumbnailManifest(new Dictionary<string, ThumbnailManifestEntry>());
        }
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

    public IReadOnlyList<string> PruneMissing(IReadOnlySet<string> existingSourceFileNames)
    {
        var removedThumbnailFileNames = new List<string>();
        foreach (var key in _entries.Keys.Where(k => !existingSourceFileNames.Contains(k)).ToList())
        {
            removedThumbnailFileNames.Add(_entries[key].ThumbnailFileName);
            _entries.Remove(key);
        }
        return removedThumbnailFileNames;
    }
}
```

`PruneMissing` returns the `ThumbnailFileName`s it just orphaned (rather than `void`) so `ThumbnailCache.PruneOrphans` (Task 8) can actually delete those cached `.jpg` files from disk — without this, every cull (`N` key, Task 6) would leave its thumbnail behind in `.thumbnails/` forever, a real disk leak in an app whose core workflow is repeatedly removing photos from watched folders. `LoadOrCreate` catching `JsonException` means a crash mid-write (non-atomic `Save`) degrades to "act like the cache is empty and rebuild it" instead of permanently breaking that folder's cache on every future launch.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MasterImage.Core.Tests --filter ThumbnailManifestTests`
Expected: `Passed! - Failed: 0, Passed: 7`

- [ ] **Step 5: Commit**

```bash
git add src/MasterImage.Core/ThumbnailManifest.cs tests/MasterImage.Core.Tests/ThumbnailManifestTests.cs
git commit -m "Add thumbnail manifest for cache staleness tracking"
```

---

## Task 5: Marks store (persisted mark/unmark)

**Files:**
- Create: `src/MasterImage.Core/MarksStore.cs`
- Test: `tests/MasterImage.Core.Tests/MarksStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MasterImage.Core.Tests/MarksStoreTests.cs
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MasterImage.Core.Tests --filter MarksStoreTests`
Expected: FAIL to compile — `MarksStore` does not exist.

- [ ] **Step 3: Implement `MarksStore`**

```csharp
// src/MasterImage.Core/MarksStore.cs
using System.Text.Json;

namespace MasterImage.Core;

// Generic string-keyed persisted set. The caller supplies the key — it must be a
// collision-free identifier (e.g. a filename with extension), not a bare filename
// stem: PhotoSet.Load can emit two PhotoItems sharing a stem across extensions
// (e.g. sunset.jpg + sunset.png), which would incorrectly share mark state if
// this store were keyed on stem. See MarkKey in Task 10's MainViewModel.
public sealed class MarksStore
{
    private readonly HashSet<string> _markedKeys;
    private readonly string _marksPath;

    private MarksStore(string marksPath, HashSet<string> markedKeys)
    {
        _marksPath = marksPath;
        _markedKeys = markedKeys;
    }

    public static MarksStore LoadOrCreate(string thumbnailsFolder)
    {
        string path = Path.Combine(thumbnailsFolder, "marks.json");
        if (!File.Exists(path))
        {
            return new MarksStore(path, new HashSet<string>());
        }

        try
        {
            string json = File.ReadAllText(path);
            var keys = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            return new MarksStore(path, new HashSet<string>(keys));
        }
        catch (JsonException)
        {
            // A crash or power loss mid-write can leave marks.json truncated/corrupt.
            // Treat it the same as "missing" rather than throwing on every future launch.
            return new MarksStore(path, new HashSet<string>());
        }
    }

    public bool IsMarked(string key) => _markedKeys.Contains(key);

    public void Toggle(string key)
    {
        if (!_markedKeys.Remove(key))
        {
            _markedKeys.Add(key);
        }
    }

    public IReadOnlyCollection<string> MarkedKeys => _markedKeys;

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_marksPath)!);
        string json = JsonSerializer.Serialize(_markedKeys.ToList(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_marksPath, json);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MasterImage.Core.Tests --filter MarksStoreTests`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 5: Commit**

```bash
git add src/MasterImage.Core/MarksStore.cs tests/MasterImage.Core.Tests/MarksStoreTests.cs
git commit -m "Add persisted marks store for the culling workflow"
```

---

## Task 6: Cull operation (move marked photos to selected/)

**Files:**
- Create: `src/MasterImage.Core/CullOperations.cs`
- Test: `tests/MasterImage.Core.Tests/CullOperationsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MasterImage.Core.Tests/CullOperationsTests.cs
using System;
using System.IO;
using System.Linq;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class CullOperationsTests : IDisposable
{
    private readonly string _tempDir;

    public CullOperationsTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void MovesEachFileOfEachMarkedItemIntoSelectedFolder()
    {
        string pathA = Path.Combine(_tempDir, "DSC1.jpg");
        string pathB = Path.Combine(_tempDir, "DSC2.jpg");
        File.WriteAllBytes(pathA, new byte[] { 1 });
        File.WriteAllBytes(pathB, new byte[] { 2 });

        var marked = new[]
        {
            new PhotoItem("DSC1", new[] { pathA }),
            new PhotoItem("DSC2", new[] { pathB })
        };

        var result = CullOperations.MoveMarkedToSelectedFolder(_tempDir, marked);

        Assert.Equal(2, result.MovedFileCount);
        Assert.Empty(result.Failures);
        Assert.True(File.Exists(Path.Combine(_tempDir, "selected", "DSC1.jpg")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "selected", "DSC2.jpg")));
        Assert.False(File.Exists(pathA));
    }

    [Fact]
    public void MovesBothFilesOfAPairTogether()
    {
        string rawPath = Path.Combine(_tempDir, "DSC1.NEF");
        string jpgPath = Path.Combine(_tempDir, "DSC1.JPG");
        File.WriteAllBytes(rawPath, new byte[] { 1 });
        File.WriteAllBytes(jpgPath, new byte[] { 2 });

        var marked = new[] { new PhotoItem("DSC1", new[] { rawPath, jpgPath }) };

        var result = CullOperations.MoveMarkedToSelectedFolder(_tempDir, marked);

        Assert.Equal(2, result.MovedFileCount);
        Assert.True(File.Exists(Path.Combine(_tempDir, "selected", "DSC1.NEF")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "selected", "DSC1.JPG")));
    }

    [Fact]
    public void ReportsFailureWithoutThrowingWhenDestinationAlreadyExists()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        File.WriteAllBytes(sourcePath, new byte[] { 1 });
        Directory.CreateDirectory(Path.Combine(_tempDir, "selected"));
        File.WriteAllBytes(Path.Combine(_tempDir, "selected", "DSC1.jpg"), new byte[] { 9 });

        var marked = new[] { new PhotoItem("DSC1", new[] { sourcePath }) };

        var result = CullOperations.MoveMarkedToSelectedFolder(_tempDir, marked);

        Assert.Equal(0, result.MovedFileCount);
        Assert.Single(result.Failures);
        Assert.True(File.Exists(sourcePath));
    }

    [Fact]
    public void PartialPairFailureMovesOneFileAndReportsTheOtherAsAFailure()
    {
        string rawPath = Path.Combine(_tempDir, "DSC1.NEF");
        string jpgPath = Path.Combine(_tempDir, "DSC1.JPG");
        File.WriteAllBytes(rawPath, new byte[] { 1 });
        File.WriteAllBytes(jpgPath, new byte[] { 2 });
        Directory.CreateDirectory(Path.Combine(_tempDir, "selected"));
        File.WriteAllBytes(Path.Combine(_tempDir, "selected", "DSC1.JPG"), new byte[] { 9 });

        var marked = new[] { new PhotoItem("DSC1", new[] { rawPath, jpgPath }) };

        var result = CullOperations.MoveMarkedToSelectedFolder(_tempDir, marked);

        Assert.Equal(1, result.MovedFileCount);
        Assert.Single(result.Failures);
        Assert.True(File.Exists(Path.Combine(_tempDir, "selected", "DSC1.NEF")));
        Assert.False(File.Exists(rawPath));
        Assert.True(File.Exists(jpgPath));
    }
}
```

The last test documents accepted (not prevented) behavior: if one file of a RAW+JPEG pair can't move, the pair can end up split across two folders. `CullOperations` reports this via `Failures` rather than silently losing track of it or throwing, but nothing re-pairs them — a photographer would need to notice the failure and finish the move manually.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MasterImage.Core.Tests --filter CullOperationsTests`
Expected: FAIL to compile — `CullOperations` does not exist.

- [ ] **Step 3: Implement `CullOperations`**

```csharp
// src/MasterImage.Core/CullOperations.cs
namespace MasterImage.Core;

public static class CullOperations
{
    public sealed record MoveResult(int MovedFileCount, IReadOnlyList<string> Failures);

    public static MoveResult MoveMarkedToSelectedFolder(string folderPath, IEnumerable<PhotoItem> markedItems)
    {
        string selectedFolder = Path.Combine(folderPath, "selected");
        Directory.CreateDirectory(selectedFolder);

        int movedCount = 0;
        var failures = new List<string>();

        foreach (var item in markedItems)
        {
            foreach (var filePath in item.FilePaths)
            {
                string destination = Path.Combine(selectedFolder, Path.GetFileName(filePath));
                try
                {
                    if (File.Exists(destination))
                    {
                        failures.Add($"{filePath} (already exists in selected/)");
                        continue;
                    }

                    File.Move(filePath, destination);
                    movedCount++;
                }
                catch (IOException ex)
                {
                    failures.Add($"{filePath} ({ex.Message})");
                }
                catch (UnauthorizedAccessException ex)
                {
                    failures.Add($"{filePath} ({ex.Message})");
                }
            }
        }

        return new MoveResult(movedCount, failures);
    }
}
```

`UnauthorizedAccessException` (thrown for a read-only or externally-locked file, e.g. permission-denied rather than a sharing violation) doesn't derive from `IOException`, so it needs its own `catch` — without it, one locked file aborts the entire batch move uncaught instead of degrading to a `Failures` entry like every other failure mode here.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MasterImage.Core.Tests --filter CullOperationsTests`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 5: Commit**

```bash
git add src/MasterImage.Core/CullOperations.cs tests/MasterImage.Core.Tests/CullOperationsTests.cs
git commit -m "Add cull operation to move marked photos into selected/"
```

---

## Task 7: WIC-based image loader + test image factory

**Files:**
- Create: `src/MasterImage.Core/ImageLoader.cs`
- Create: `tests/MasterImage.Core.Tests/TestImageFactory.cs`
- Test: `tests/MasterImage.Core.Tests/ImageLoaderTests.cs`

- [ ] **Step 1: Write the test image factory helper (not itself a test, used by tests)**

```csharp
// tests/MasterImage.Core.Tests/TestImageFactory.cs
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MasterImage.Core.Tests;

public static class TestImageFactory
{
    public static void WriteTestJpeg(string path, int width = 64, int height = 48)
    {
        var pixels = new byte[width * height * 3];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i % 256);
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static void WriteTestJpegWithOrientation(string path, int width, int height, ushort exifOrientation)
    {
        var pixels = new byte[width * height * 3];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i % 256);
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);

        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("System.Photo.Orientation", exifOrientation);

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, thumbnail: null, metadata: metadata, colorContexts: null));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // Writes a JPEG whose LEFT half is solid black and RIGHT half is solid white (a vertical
    // split), tagged with the given EXIF orientation. Large solid regions survive JPEG
    // compression cleanly, so post-rotation a sample pixel's brightness reveals which way the
    // image actually rotated — enough to tell 90deg (orientation 6) from 270deg (orientation 8),
    // which a dimensions-only check cannot (both produce the same swapped size).
    public static void WriteTwoToneJpegWithOrientation(string path, int width, int height, ushort exifOrientation)
    {
        var pixels = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte v = (byte)(x < width / 2 ? 0 : 255);
                int idx = (y * width + x) * 3;
                pixels[idx] = v;
                pixels[idx + 1] = v;
                pixels[idx + 2] = v;
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);

        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("System.Photo.Orientation", exifOrientation);

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, thumbnail: null, metadata: metadata, colorContexts: null));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // A PNG carries no System.Photo.Orientation tag, so it exercises ImageLoader's
    // "metadata present but no orientation key -> normal orientation" fallback branch.
    public static void WriteTestPng(string path, int width, int height)
    {
        var pixels = new byte[width * height * 3];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i % 256);
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/MasterImage.Core.Tests/ImageLoaderTests.cs
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class ImageLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public ImageLoaderTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void LoadsAtRequestedDecodePixelWidth()
    {
        string path = Path.Combine(_tempDir, "test.jpg");
        TestImageFactory.WriteTestJpeg(path, width: 640, height: 480);

        var result = ImageLoader.TryLoadAtSize(path, decodePixelWidth: 100);

        Assert.NotNull(result);
        Assert.Equal(100, result!.PixelWidth);
    }

    [Fact]
    public void ReturnsNullForUnsupportedOrMissingFile()
    {
        string path = Path.Combine(_tempDir, "not-an-image.txt");
        File.WriteAllText(path, "hello");

        var result = ImageLoader.TryLoadAtSize(path, decodePixelWidth: 100);

        Assert.Null(result);
    }

    [Fact]
    public void SaveAsJpegWritesAReloadableFile()
    {
        string sourcePath = Path.Combine(_tempDir, "source.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 200, height: 150);
        var decoded = ImageLoader.TryLoadAtSize(sourcePath, decodePixelWidth: 100)!;

        string destPath = Path.Combine(_tempDir, "out", "thumb.jpg");
        ImageLoader.SaveAsJpeg(decoded, destPath);

        var reloaded = ImageLoader.TryLoadAtSize(destPath, decodePixelWidth: 100);
        Assert.NotNull(reloaded);
    }

    [Fact]
    public void Rotates90DegreeExifOrientationAndSwapsDimensions()
    {
        string path = Path.Combine(_tempDir, "rotated90.jpg");
        TestImageFactory.WriteTestJpegWithOrientation(path, width: 100, height: 60, exifOrientation: 6);

        var result = ImageLoader.TryLoadFullResolution(path);

        Assert.NotNull(result);
        Assert.Equal(60, result!.PixelWidth);
        Assert.Equal(100, result.PixelHeight);
    }

    [Fact]
    public void Rotates180DegreeExifOrientationWithoutSwappingDimensions()
    {
        string path = Path.Combine(_tempDir, "rotated180.jpg");
        TestImageFactory.WriteTestJpegWithOrientation(path, width: 100, height: 60, exifOrientation: 3);

        var result = ImageLoader.TryLoadFullResolution(path);

        Assert.NotNull(result);
        Assert.Equal(100, result!.PixelWidth);
        Assert.Equal(60, result.PixelHeight);
    }

    [Fact]
    public void NormalOrientationIsNotRotated()
    {
        string path = Path.Combine(_tempDir, "normal.jpg");
        TestImageFactory.WriteTestJpegWithOrientation(path, width: 100, height: 60, exifOrientation: 1);

        var result = ImageLoader.TryLoadFullResolution(path);

        Assert.NotNull(result);
        Assert.Equal(100, result!.PixelWidth);
        Assert.Equal(60, result.PixelHeight);
    }

    // Orientation 6 = "rotate 90deg clockwise to display upright." Source has a black LEFT half;
    // under WPF's 90deg-CW RotateTransform the left edge swings to the TOP, so the top of the
    // result must be black and the bottom white. Orientation 8 (270deg) is the mirror of this
    // (black to the BOTTOM). Dimensions alone (both give 60x100) cannot tell 6 from 8 apart —
    // this pixel check is what actually pins the rotation direction.
    [Fact]
    public void Orientation6PutsBlackLeftHalfOnTop()
    {
        string path = Path.Combine(_tempDir, "o6.jpg");
        TestImageFactory.WriteTwoToneJpegWithOrientation(path, width: 100, height: 60, exifOrientation: 6);

        var result = ImageLoader.TryLoadFullResolution(path);

        Assert.NotNull(result);
        Assert.Equal(60, result!.PixelWidth);
        Assert.Equal(100, result.PixelHeight);
        Assert.True(IsDark(result, x: 30, y: 15));    // black (was left) now on top
        Assert.False(IsDark(result, x: 30, y: 85));   // white now on bottom
    }

    [Fact]
    public void Orientation8PutsBlackLeftHalfOnBottom()
    {
        string path = Path.Combine(_tempDir, "o8.jpg");
        TestImageFactory.WriteTwoToneJpegWithOrientation(path, width: 100, height: 60, exifOrientation: 8);

        var result = ImageLoader.TryLoadFullResolution(path);

        Assert.NotNull(result);
        Assert.Equal(60, result!.PixelWidth);
        Assert.Equal(100, result.PixelHeight);
        Assert.False(IsDark(result, x: 30, y: 15));   // white now on top
        Assert.True(IsDark(result, x: 30, y: 85));    // black (was left) now on bottom
    }

    [Fact]
    public void PngWithoutOrientationMetadataLoadsUnrotated()
    {
        string path = Path.Combine(_tempDir, "no-orientation.png");
        TestImageFactory.WriteTestPng(path, width: 100, height: 60);

        var result = ImageLoader.TryLoadFullResolution(path);

        Assert.NotNull(result);
        Assert.Equal(100, result!.PixelWidth);
        Assert.Equal(60, result.PixelHeight);
    }

    private static bool IsDark(BitmapSource image, int x, int y)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, stride: 4, offset: 0);
        int brightness = (pixel[0] + pixel[1] + pixel[2]) / 3; // B, G, R
        return brightness < 128;
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/MasterImage.Core.Tests --filter ImageLoaderTests`
Expected: FAIL to compile — `ImageLoader` does not exist.

- [ ] **Step 4: Implement `ImageLoader`**

```csharp
// src/MasterImage.Core/ImageLoader.cs
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MasterImage.Core;

public static class ImageLoader
{
    public static BitmapSource? TryLoadAtSize(string filePath, int decodePixelWidth)
    {
        return TryLoad(filePath, decodePixelWidth);
    }

    public static BitmapSource? TryLoadFullResolution(string filePath)
    {
        return TryLoad(filePath, decodePixelWidth: 0);
    }

    private static BitmapSource? TryLoad(string filePath, int decodePixelWidth)
    {
        try
        {
            ushort orientation = ReadExifOrientation(filePath);

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            if (decodePixelWidth > 0)
            {
                bitmap.DecodePixelWidth = decodePixelWidth;
            }
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            BitmapSource result = ApplyOrientation(bitmap, orientation);
            if (!result.IsFrozen)
            {
                result.Freeze();
            }
            return result;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (FileFormatException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // EXIF orientation lives in *frame* metadata. BitmapImage.Metadata is NOT usable for this —
    // it throws NotSupportedException unconditionally ("BitmapMetadata is not available on
    // BitmapImage"), so orientation must be read from a BitmapFrame via the decoder instead.
    // This is a lightweight header-only read (BitmapCacheOption.None, no full pixel decode) on
    // its own read-only stream; any failure falls back to "normal" so a metadata quirk can
    // never prevent an image from loading.
    private static ushort ReadExifOrientation(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None);
            if (decoder.Frames.Count > 0
                && decoder.Frames[0].Metadata is BitmapMetadata metadata
                && metadata.GetQuery("System.Photo.Orientation") is { } value)
            {
                return Convert.ToUInt16(value);
            }
        }
        catch (Exception)
        {
            // No readable orientation metadata (e.g. PNG/BMP/GIF, an unsupported/corrupt file,
            // or a value WIC can't parse). Reading orientation is strictly best-effort — a
            // failure here must never stop the image itself from decoding — so swallow broadly
            // and treat as normal orientation. If the file is genuinely unloadable, the real
            // decode in TryLoad will surface that and return null.
        }
        return 1;
    }

    // Cameras only ever produce EXIF orientation 1 (normal), 3 (180deg), 6 (90deg CW), or
    // 8 (270deg CW / 90deg CCW) - the mirror-flip variants (2, 4, 5, 7) come only from certain
    // scanning/editing tools and are deliberately left un-rotated (angle 0) here as out of scope.
    private static BitmapSource ApplyOrientation(BitmapSource source, ushort orientation)
    {
        double angle = orientation switch
        {
            3 => 180,
            6 => 90,
            8 => 270,
            _ => 0,
        };

        return angle == 0 ? source : new TransformedBitmap(source, new RotateTransform(angle));
    }

    public static void SaveAsJpeg(BitmapSource source, string destinationPath, int quality = 85)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(source));

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var stream = File.Create(destinationPath);
        encoder.Save(stream);
    }
}
```

Note: for a 90deg/270deg rotation, the final `PixelWidth`/`PixelHeight` will be swapped relative to `decodePixelWidth` (which constrains the pre-rotation decode) — e.g. requesting `decodePixelWidth: 400` on a portrait phone photo stored as landscape pixels + orientation 6 yields a final bitmap that's 400 tall, not 400 wide. This is fine for every consumer in this plan (WPF `Image` controls use `Stretch="Uniform"`, which fits either dimension), but worth knowing if a future caller ever treats `decodePixelWidth` as an exact width contract rather than a size hint.

**Fixed during implementation** (originally this used `bitmap.UriSource = new Uri(filePath)` instead of an explicit `FileStream` + `StreamSource`): the implementer correctly caught that `UriSource`-based loading only releases its file handle deterministically on a *successful* decode. On a failed decode (unsupported format), `BitmapImage`/`BitmapDecoder` don't implement `IDisposable`, so the handle lingers until the next GC — not just a test-cleanup annoyance, but a real risk for the actual app: `PhotoSet.Load` filters by extension only, so a corrupted file or an unsupported format (e.g. `.webp` without the codec installed) that gets scanned and thumbnailed would leave a lingering open handle, and doing this across many files during an `L` full-folder preload could accumulate a pile of leaked handles before GC ever runs. Opening the file explicitly via `FileStream` (with `FileShare.Read`, since other processes/the OS shell may want to read it concurrently) and wrapping it in `using` guarantees the handle closes deterministically the moment `TryLoad` returns — on success *or* failure — which is exactly the documented WPF pattern for this ("with `CacheOption.OnLoad` and a stream source, the stream can be closed immediately after `EndInit()` returns"). Also added a `catch (UnauthorizedAccessException)` alongside the others, consistent with the same exception-class gap found and fixed in Task 6's `CullOperations` — a permission-denied or externally-locked file throws this instead of `IOException`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MasterImage.Core.Tests --filter ImageLoaderTests`
Expected: `Passed! - Failed: 0, Passed: 9`

- [ ] **Step 6: Commit**

```bash
git add src/MasterImage.Core/ImageLoader.cs tests/MasterImage.Core.Tests/TestImageFactory.cs tests/MasterImage.Core.Tests/ImageLoaderTests.cs
git commit -m "Add WIC-based image loader with size-targeted decode"
```

---

## Task 8: Thumbnail cache (disk-backed, staleness-aware)

**Files:**
- Create: `src/MasterImage.Core/ThumbnailCache.cs`
- Test: `tests/MasterImage.Core.Tests/ThumbnailCacheTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MasterImage.Core.Tests/ThumbnailCacheTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class ThumbnailCacheTests : IDisposable
{
    private readonly string _tempDir;

    public ThumbnailCacheTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void GeneratesAndCachesAThumbnailOnDisk()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 640, height: 480);
        var item = new PhotoItem("DSC1", new[] { sourcePath });

        var cache = new ThumbnailCache(_tempDir);
        var thumbnail = cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);

        Assert.NotNull(thumbnail);
        Assert.Equal(100, thumbnail!.PixelWidth);
        Assert.Single(Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg"));
    }

    [Fact]
    public void ThumbnailsFolderIsHidden()
    {
        var cache = new ThumbnailCache(_tempDir);
        var attributes = File.GetAttributes(cache.ThumbnailsFolder);
        Assert.True((attributes & FileAttributes.Hidden) == FileAttributes.Hidden);
    }

    [Fact]
    public void ReusesCachedThumbnailWhenSourceUnchanged()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 640, height: 480);
        var item = new PhotoItem("DSC1", new[] { sourcePath });

        var cache = new ThumbnailCache(_tempDir);
        cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);
        string thumbPath = Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg")[0];
        DateTime firstWriteTime = File.GetLastWriteTimeUtc(thumbPath);

        System.Threading.Thread.Sleep(50);
        cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);

        Assert.Equal(firstWriteTime, File.GetLastWriteTimeUtc(thumbPath));
    }

    [Fact]
    public void RegeneratesThumbnailWhenSourceFileChanges()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 640, height: 480);
        var item = new PhotoItem("DSC1", new[] { sourcePath });

        var cache = new ThumbnailCache(_tempDir);
        cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);
        string thumbPath = Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg")[0];
        DateTime firstWriteTime = File.GetLastWriteTimeUtc(thumbPath);

        System.Threading.Thread.Sleep(1100); // ensure a distinguishable mtime
        TestImageFactory.WriteTestJpeg(sourcePath, width: 800, height: 600);
        cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);

        Assert.NotEqual(firstWriteTime, File.GetLastWriteTimeUtc(thumbPath));
    }

    [Fact]
    public void PruneOrphansDeletesCachedThumbnailForARemovedSource()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 640, height: 480);
        var item = new PhotoItem("DSC1", new[] { sourcePath });

        var cache = new ThumbnailCache(_tempDir);
        cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);
        Assert.Single(Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg"));

        // The source is gone (e.g. culled/moved). Pruning against an empty current-set must
        // delete the now-orphaned cached thumbnail file from disk, not just forget the manifest entry.
        cache.PruneOrphans(new List<PhotoItem>());

        Assert.Empty(Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg"));
    }

    [Fact]
    public void PruneOrphansKeepsThumbnailForAStillPresentSource()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 640, height: 480);
        var item = new PhotoItem("DSC1", new[] { sourcePath });

        var cache = new ThumbnailCache(_tempDir);
        cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);

        cache.PruneOrphans(new List<PhotoItem> { item });

        Assert.Single(Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg"));
    }

    // Mirrors how the Task 9 pipeline drives the cache: many background threads calling
    // GetOrCreateThumbnail on one shared cache instance at once. Without the manifest lock this
    // faults reproducibly (concurrent manifest.json writes throw a sharing-violation IOException,
    // and concurrent Dictionary mutation corrupts state) — this test pins the fix.
    [Fact]
    public void GeneratesThumbnailsConcurrentlyWithoutError()
    {
        var items = new List<PhotoItem>();
        for (int i = 0; i < 24; i++)
        {
            string p = Path.Combine(_tempDir, $"DSC{i}.jpg");
            TestImageFactory.WriteTestJpeg(p, width: 320, height: 240);
            items.Add(new PhotoItem($"DSC{i}", new[] { p }));
        }

        var cache = new ThumbnailCache(_tempDir);

        var exception = Record.Exception(() =>
            Parallel.ForEach(items, item => cache.GetOrCreateThumbnail(item, targetPixelWidth: 100)));

        Assert.Null(exception);
        Assert.Equal(24, Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg").Length);
    }

    // Many threads generating the SAME uncached item collide on ImageLoader.SaveAsJpeg's
    // File.Create (FileShare.None) — the losers must swallow the write collision and still return
    // a decoded bitmap, not throw. This is the cross-entry-point race (on-demand tile load
    // overlapping an L preload of the same photo).
    [Fact]
    public void ConcurrentGenerationOfTheSameItemDoesNotThrow()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 640, height: 480);
        var item = new PhotoItem("DSC1", new[] { sourcePath });
        var cache = new ThumbnailCache(_tempDir);

        var exception = Record.Exception(() =>
            Parallel.For(0, 16, _ => cache.GetOrCreateThumbnail(item, targetPixelWidth: 100)));

        Assert.Null(exception);
        Assert.Single(Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg"));
    }

    [Fact]
    public void RegeneratesWhenCachedThumbnailFileIsCorrupt()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 640, height: 480);
        var item = new PhotoItem("DSC1", new[] { sourcePath });

        var cache = new ThumbnailCache(_tempDir);
        cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);
        string thumbPath = Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg")[0];

        // Corrupt the cached thumbnail as an interrupted write might leave it. The source is
        // unchanged, so the manifest still considers it "up to date" — the cache must notice the
        // cached file won't decode and regenerate rather than returning null.
        File.WriteAllText(thumbPath, "not a jpeg");

        var result = cache.GetOrCreateThumbnail(item, targetPixelWidth: 100);

        Assert.NotNull(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MasterImage.Core.Tests --filter ThumbnailCacheTests`
Expected: FAIL to compile — `ThumbnailCache` does not exist.

- [ ] **Step 3: Implement `ThumbnailCache`**

```csharp
// src/MasterImage.Core/ThumbnailCache.cs
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

        // Caching is best-effort and must never throw out of this method — the pipeline (Task 9)
        // calls it in bulk, so one failed write can't be allowed to abort a whole L preload. Two
        // realistic failure modes: (a) another thread generating the SAME uncached item at the same
        // time (on-demand tile load overlapping an L preload) — ImageLoader.SaveAsJpeg opens with
        // FileShare.None, so the loser's File.Create throws a sharing-violation IOException; (b)
        // disk full / thumbnail folder momentarily unwritable. In both cases we still return the
        // decoded bitmap so the caller gets its image; only the disk cache write is skipped, and it
        // is retried the next time this item is requested.
        try
        {
            ImageLoader.SaveAsJpeg(decoded, thumbPath);
            lock (_manifestLock)
            {
                _manifest.Update(sourcePath, sourceFileName, thumbFileName);
                _manifest.Save(_manifestPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MasterImage.Core.Tests --filter ThumbnailCacheTests`
Expected: `Passed! - Failed: 0, Passed: 9`

- [ ] **Step 5: Commit**

```bash
git add src/MasterImage.Core/ThumbnailCache.cs tests/MasterImage.Core.Tests/ThumbnailCacheTests.cs
git commit -m "Add disk-backed thumbnail cache with staleness detection"
```

---

## Task 9: Thumbnail pipeline (on-demand + L full preload)

**Files:**
- Create: `src/MasterImage.Core/ThumbnailPipeline.cs`
- Test: `tests/MasterImage.Core.Tests/ThumbnailPipelineTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MasterImage.Core.Tests/ThumbnailPipelineTests.cs
using System;
using System.IO;
using System.Threading.Tasks;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class ThumbnailPipelineTests : IDisposable
{
    private readonly string _tempDir;

    public ThumbnailPipelineTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task RequestThumbnailAsyncReturnsADecodedThumbnail()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        TestImageFactory.WriteTestJpeg(sourcePath, width: 400, height: 300);
        var item = new PhotoItem("DSC1", new[] { sourcePath });

        var pipeline = new ThumbnailPipeline(new ThumbnailCache(_tempDir));
        var thumbnail = await pipeline.RequestThumbnailAsync(item);

        Assert.NotNull(thumbnail);
    }

    [Fact]
    public async Task PreloadAllAsyncGeneratesThumbnailsForEveryItem()
    {
        var items = new PhotoItem[3];
        for (int i = 0; i < 3; i++)
        {
            string path = Path.Combine(_tempDir, $"DSC{i}.jpg");
            TestImageFactory.WriteTestJpeg(path, width: 200, height: 150);
            items[i] = new PhotoItem($"DSC{i}", new[] { path });
        }

        var cache = new ThumbnailCache(_tempDir);
        var pipeline = new ThumbnailPipeline(cache);

        await pipeline.PreloadAllAsync(items);

        Assert.Equal(3, Directory.GetFiles(cache.ThumbnailsFolder, "*.jpg").Length);
    }

    [Fact]
    public async Task PreloadAllAsyncReportsProgressForEveryItem()
    {
        var items = new PhotoItem[3];
        for (int i = 0; i < 3; i++)
        {
            string path = Path.Combine(_tempDir, $"DSC{i}.jpg");
            TestImageFactory.WriteTestJpeg(path, width: 200, height: 150);
            items[i] = new PhotoItem($"DSC{i}", new[] { path });
        }

        var pipeline = new ThumbnailPipeline(new ThumbnailCache(_tempDir));
        int reportCount = 0;
        var progress = new Progress<int>(_ => reportCount++);

        await pipeline.PreloadAllAsync(items, progress);
        await Task.Delay(50); // let the last Progress<T> callback marshal through

        Assert.Equal(3, reportCount);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MasterImage.Core.Tests --filter ThumbnailPipelineTests`
Expected: FAIL to compile — `ThumbnailPipeline` does not exist.

- [ ] **Step 3: Implement `ThumbnailPipeline`**

```csharp
// src/MasterImage.Core/ThumbnailPipeline.cs
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MasterImage.Core.Tests --filter ThumbnailPipelineTests`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add src/MasterImage.Core/ThumbnailPipeline.cs tests/MasterImage.Core.Tests/ThumbnailPipelineTests.cs
git commit -m "Add background thumbnail pipeline for on-demand and L preload"
```

---

## Task 10: MainViewModel (navigation, marks, mode state)

**Files:**
- Create: `src/MasterImage.App/ViewModels/MainViewModel.cs`
- Test: `tests/MasterImage.Core.Tests/MainViewModelTests.cs`

Since `MainViewModel` only depends on `MasterImage.Core` types (no WPF window/control types), it's practical to unit-test its navigation and state logic directly, so its test lives alongside the Core tests project (which already has a project reference available) rather than requiring a new WPF-aware test project. Add a reference from the tests project to the App project first.

- [ ] **Step 1: Add project reference from tests to the App project**

```bash
dotnet add tests/MasterImage.Core.Tests/MasterImage.Core.Tests.csproj reference src/MasterImage.App/MasterImage.App.csproj
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/MasterImage.Core.Tests/MainViewModelTests.cs
using System;
using System.IO;
using MasterImage.App.ViewModels;
using MasterImage.Core.Tests;
using Xunit;

namespace MasterImage.Core.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public MainViewModelTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
        for (int i = 0; i < 3; i++)
        {
            TestImageFactory.WriteTestJpeg(Path.Combine(_tempDir, $"DSC{i}.jpg"), 100, 100);
        }
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void StartsAtFirstPhotoWhenNoInitialFileGiven()
    {
        var vm = new MainViewModel(_tempDir, initialFilePath: null);
        Assert.Equal("DSC0", vm.CurrentPhoto!.Stem);
    }

    [Fact]
    public void StartsAtRequestedInitialFile()
    {
        var vm = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC1.jpg"));
        Assert.Equal("DSC1", vm.CurrentPhoto!.Stem);
    }

    [Fact]
    public void SeekNextWrapsAroundAtTheEnd()
    {
        var vm = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC2.jpg"));
        vm.SeekNext();
        Assert.Equal("DSC0", vm.CurrentPhoto!.Stem);
    }

    [Fact]
    public void SeekPreviousWrapsAroundAtTheStart()
    {
        var vm = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC0.jpg"));
        vm.SeekPrevious();
        Assert.Equal("DSC2", vm.CurrentPhoto!.Stem);
    }

    [Fact]
    public void ToggleMarkFlipsIsCurrentMarkedAndPersists()
    {
        var vm = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC0.jpg"));
        Assert.False(vm.IsCurrentMarked);

        vm.ToggleMark();
        Assert.True(vm.IsCurrentMarked);

        var reloaded = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC0.jpg"));
        Assert.True(reloaded.IsCurrentMarked);
    }

    [Fact]
    public void MoveMarkedToSelectedRemovesItemsAndClampsIndex()
    {
        var vm = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC2.jpg"));
        vm.ToggleMark();

        var result = vm.MoveMarkedToSelected();

        Assert.Equal(1, result.MovedFileCount);
        Assert.Equal(2, vm.Photos.Count);
        Assert.InRange(vm.CurrentIndex, 0, 1);
    }

    [Fact]
    public void MoveMarkedToSelectedKeepsTheMarkOnAPhotoThatFailedToMove()
    {
        // Pre-create a colliding file so DSC0's move fails.
        string selectedFolder = Path.Combine(_tempDir, "selected");
        Directory.CreateDirectory(selectedFolder);
        File.WriteAllBytes(Path.Combine(selectedFolder, "DSC0.jpg"), new byte[] { 9 });

        var vm = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC0.jpg"));
        vm.ToggleMark();

        var result = vm.MoveMarkedToSelected();

        Assert.Equal(0, result.MovedFileCount);
        Assert.Single(result.Failures);
        // DSC0 stayed put, so it must still be marked — otherwise the pick is silently lost and
        // the photographer can't just resolve the collision and press N again.
        Assert.Equal("DSC0", vm.CurrentPhoto!.Stem);
        Assert.True(vm.IsCurrentMarked);
    }

    [Fact]
    public void TileSizeIsClampedToReasonableBounds()
    {
        var vm = new MainViewModel(_tempDir, null);

        vm.TileSize = 10;
        Assert.Equal(80, vm.TileSize);

        vm.TileSize = 10000;
        Assert.Equal(480, vm.TileSize);
    }

    [Fact]
    public void IsMarkedReflectsAnyPhotoNotJustTheCurrentOne()
    {
        var vm = new MainViewModel(_tempDir, Path.Combine(_tempDir, "DSC0.jpg"));
        var otherPhoto = vm.Photos.Single(p => p.Stem == "DSC1");

        Assert.False(vm.IsMarked(otherPhoto));

        vm.JumpTo(vm.Photos.ToList().IndexOf(otherPhoto));
        vm.ToggleMark();

        Assert.True(vm.IsMarked(otherPhoto));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/MasterImage.Core.Tests --filter MainViewModelTests`
Expected: FAIL to compile — `MainViewModel` does not exist.

- [ ] **Step 4: Implement `MainViewModel`**

```csharp
// src/MasterImage.App/ViewModels/MainViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using MasterImage.Core;

namespace MasterImage.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly MarksStore _marksStore;
    private readonly ThumbnailCache _thumbnailCache;
    private readonly ThumbnailPipeline _thumbnailPipeline;

    private IReadOnlyList<PhotoItem> _photos;
    private int _currentIndex;
    private bool _isGridVisible;
    private bool _isFullscreen;
    private bool _isShortcutsOverlayVisible;
    private double _tileSize = 200;

    public MainViewModel(string folderPath, string? initialFilePath)
    {
        FolderPath = folderPath;
        _photos = PhotoSet.Load(folderPath);
        _thumbnailCache = new ThumbnailCache(folderPath);
        _thumbnailPipeline = new ThumbnailPipeline(_thumbnailCache);
        _marksStore = MarksStore.LoadOrCreate(_thumbnailCache.ThumbnailsFolder);

        _currentIndex = 0;
        if (initialFilePath is not null)
        {
            int index = _photos.ToList().FindIndex(p =>
                p.PrimaryFilePath.Equals(initialFilePath, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _currentIndex = index;
            }
        }
    }

    public string FolderPath { get; }
    public IReadOnlyList<PhotoItem> Photos => _photos;
    public int CurrentIndex => _currentIndex;
    public PhotoItem? CurrentPhoto => _photos.Count > 0 ? _photos[_currentIndex] : null;
    public bool IsCurrentMarked => CurrentPhoto is not null && _marksStore.IsMarked(MarkKey(CurrentPhoto));

    public bool IsMarked(PhotoItem item) => _marksStore.IsMarked(MarkKey(item));

    // Keyed by full filename (with extension), not Stem: PhotoSet.Load currently emits
    // one PhotoItem per file, so two files that happen to share a stem across different
    // extensions (e.g. sunset.jpg and sunset.png) would collide on a bare-Stem key and
    // incorrectly share mark state. Filename-with-extension is unique per PhotoItem today,
    // and stays unique once RAW+JPEG pairing groups files under one PhotoItem (Plan 2) since
    // there's still exactly one PrimaryFilePath per PhotoItem.
    private static string MarkKey(PhotoItem item) => Path.GetFileName(item.PrimaryFilePath);

    public bool IsGridVisible
    {
        get => _isGridVisible;
        set { _isGridVisible = value; OnPropertyChanged(); }
    }

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set { _isFullscreen = value; OnPropertyChanged(); }
    }

    public bool IsShortcutsOverlayVisible
    {
        get => _isShortcutsOverlayVisible;
        set { _isShortcutsOverlayVisible = value; OnPropertyChanged(); }
    }

    public double TileSize
    {
        get => _tileSize;
        set { _tileSize = Math.Clamp(value, 80, 480); OnPropertyChanged(); }
    }

    public void SeekNext()
    {
        if (_photos.Count == 0) return;
        SetCurrentIndex((_currentIndex + 1) % _photos.Count);
    }

    public void SeekPrevious()
    {
        if (_photos.Count == 0) return;
        SetCurrentIndex((_currentIndex - 1 + _photos.Count) % _photos.Count);
    }

    public void JumpTo(int index)
    {
        if (index < 0 || index >= _photos.Count) return;
        SetCurrentIndex(index);
    }

    public void ToggleMark()
    {
        if (CurrentPhoto is null) return;
        _marksStore.Toggle(MarkKey(CurrentPhoto));
        _marksStore.Save();
        OnPropertyChanged(nameof(IsCurrentMarked));
    }

    public CullOperations.MoveResult MoveMarkedToSelected()
    {
        var marked = _photos.Where(p => _marksStore.IsMarked(MarkKey(p))).ToList();
        var result = CullOperations.MoveMarkedToSelectedFolder(FolderPath, marked);

        foreach (var item in marked)
        {
            // Only clear the mark for items that actually moved. A failed move (name collision in
            // selected/, or a locked file — both surfaced in result.Failures) leaves the photo
            // right where it was; keeping its mark lets the photographer resolve the conflict and
            // press N again, instead of silently losing that pick from their selection.
            if (item.FilePaths.All(p => !File.Exists(p)))
            {
                _marksStore.Toggle(MarkKey(item));
            }
        }
        _marksStore.Save();

        _photos = PhotoSet.Load(FolderPath);
        _thumbnailCache.PruneOrphans(_photos);
        SetCurrentIndex(Math.Clamp(_currentIndex, 0, Math.Max(0, _photos.Count - 1)));
        OnPropertyChanged(nameof(Photos));

        return result;
    }

    public Task<BitmapSource?> GetThumbnailAsync(PhotoItem item) => _thumbnailPipeline.RequestThumbnailAsync(item);

    public Task PreloadAllAsync(IProgress<int>? progress = null) => _thumbnailPipeline.PreloadAllAsync(_photos, progress);

    private void SetCurrentIndex(int index)
    {
        _currentIndex = index;
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(CurrentPhoto));
        OnPropertyChanged(nameof(IsCurrentMarked));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MasterImage.Core.Tests --filter MainViewModelTests`
Expected: `Passed! - Failed: 0, Passed: 9`

- [ ] **Step 6: Commit**

```bash
git add tests/MasterImage.Core.Tests/MasterImage.Core.Tests.csproj src/MasterImage.App/ViewModels/MainViewModel.cs tests/MasterImage.Core.Tests/MainViewModelTests.cs
git commit -m "Add MainViewModel with navigation, marking, and cull state"
```

---

## Task 11: WPF app shell — single instance, borderless chrome, fullscreen toggle, Esc handling

This task wires up `App.xaml.cs` and `MainWindow.xaml.cs` together in one step, since the single-instance startup logic in `App.xaml.cs` directly constructs a `MainWindow(requestedPath)` — splitting them into separate tasks would leave the project non-building in between.

**Files:**
- Modify: `src/MasterImage.App/App.xaml`
- Modify: `src/MasterImage.App/App.xaml.cs`
- Modify: `src/MasterImage.App/MainWindow.xaml`
- Modify: `src/MasterImage.App/MainWindow.xaml.cs`

- [ ] **Step 1: Replace `App.xaml`**

```xml
<!-- src/MasterImage.App/App.xaml -->
<Application x:Class="MasterImage.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources />
</Application>
```

Note the deliberate absence of `StartupUri` (the `dotnet new wpf` template sets it to `MainWindow.xaml` — it must be removed). `OnStartup` below constructs the window itself, passing the command-line path. If `StartupUri` were left set, WPF would *also* try to create a second `MainWindow` by loading that XAML, which instantiates the root type via its **parameterless** constructor — and `MainWindow` only has `MainWindow(string?)`. That's a `MissingMethodException` on launch, every launch.

- [ ] **Step 2: Replace `App.xaml.cs` with single-instance handling**

A named `Mutex` detects whether another instance is already running. If so, this process writes the requested path to a named pipe the first instance is listening on, then exits immediately instead of opening a second window.

```csharp
// src/MasterImage.App/App.xaml.cs
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;

namespace MasterImage.App;

public partial class App : Application
{
    private const string MutexName = "MasterImage.SingleInstance";
    private const string PipeName = "MasterImage.OpenRequest";

    private Mutex? _mutex;
    private NamedPipeServerStream? _pipeServer;

    public static event Action<string>? OpenRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? requestedPath = e.Args.Length > 0 ? e.Args[0] : null;

        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            if (requestedPath is not null)
            {
                ForwardToRunningInstance(requestedPath);
            }
            Shutdown();
            return;
        }

        StartPipeServer();

        var window = new MainWindow(requestedPath);
        window.Show();
    }

    private static void ForwardToRunningInstance(string path)
    {
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
        client.Connect(timeout: 1000);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        writer.Write(path);
    }

    private void StartPipeServer()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                _pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.In);
                await _pipeServer.WaitForConnectionAsync();
                using var reader = new StreamReader(_pipeServer);
                string path = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    Current.Dispatcher.Invoke(() => OpenRequested?.Invoke(path));
                }
                _pipeServer.Dispose();
            }
        });
    }
}
```

- [ ] **Step 3: Replace `MainWindow.xaml`**

```xml
<!-- src/MasterImage.App/MainWindow.xaml -->
<Window x:Class="MasterImage.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Master Image"
        Background="Black"
        WindowStyle="None"
        ResizeMode="CanResizeWithGrip"
        Width="1200" Height="800">
    <Grid x:Name="RootGrid">
        <ContentControl x:Name="SingleImageHost" />
        <ContentControl x:Name="GridHost" Visibility="Collapsed" />
        <ContentControl x:Name="NavigationOverlayHost" />
        <ContentControl x:Name="ShortcutsOverlayHost" Visibility="Collapsed" />
    </Grid>
</Window>
```

- [ ] **Step 4: Replace `MainWindow.xaml.cs` with the constructor, fullscreen toggle, and Esc handling**

```csharp
// src/MasterImage.App/MainWindow.xaml.cs
using System.IO;
using System.Windows;
using System.Windows.Input;
using MasterImage.App.ViewModels;

namespace MasterImage.App;

public partial class MainWindow : Window
{
    private WindowState _preFullscreenState;
    private ResizeMode _preFullscreenResizeMode;

    public MainViewModel ViewModel { get; private set; }

    public MainWindow(string? requestedPath)
    {
        InitializeComponent();

        var (folder, file) = ResolveFolderAndFile(requestedPath);
        ViewModel = new MainViewModel(folder, file);
        DataContext = ViewModel;

        App.OpenRequested += OnOpenRequestedFromAnotherProcess;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private static (string Folder, string? File) ResolveFolderAndFile(string? requestedPath)
    {
        if (requestedPath is null)
        {
            return (Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), null);
        }

        if (Directory.Exists(requestedPath))
        {
            return (requestedPath, null);
        }

        return (Path.GetDirectoryName(requestedPath) ?? ".", requestedPath);
    }

    private void OnOpenRequestedFromAnotherProcess(string path)
    {
        var (folder, file) = ResolveFolderAndFile(path);
        ViewModel = new MainViewModel(folder, file);
        DataContext = ViewModel;
        Activate();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (ViewModel.IsShortcutsOverlayVisible)
            {
                ViewModel.IsShortcutsOverlayVisible = false;
            }
            else if (ViewModel.IsFullscreen)
            {
                ToggleFullscreen();
            }
            e.Handled = true;
        }
    }

    private void ToggleFullscreen()
    {
        if (!ViewModel.IsFullscreen)
        {
            _preFullscreenState = WindowState;
            _preFullscreenResizeMode = ResizeMode;

            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
            Topmost = true;
            ViewModel.IsFullscreen = true;
        }
        else
        {
            Topmost = false;
            WindowState = _preFullscreenState;
            ResizeMode = _preFullscreenResizeMode;
            ViewModel.IsFullscreen = false;
        }
    }
}
```

`WindowState.Maximized` combined with `WindowStyle="None"` (already set in XAML) makes the window cover the full monitor including where the taskbar would be, which is the "fullscreen" behavior from spec §9; the default (non-maximized) borderless window is what's used the rest of the time.

- [ ] **Step 5: Build and manually verify**

Run: `dotnet build` then `dotnet run --project src/MasterImage.App`
Expected: A black borderless window opens. Press `F` — window fills the screen edge-to-edge. Press `F` again — returns to the original bordered-less windowed size. Press `Escape` while fullscreen — also returns to windowed. Close via Alt+F4.

- [ ] **Step 6: Commit**

```bash
git add src/MasterImage.App/App.xaml src/MasterImage.App/App.xaml.cs src/MasterImage.App/MainWindow.xaml src/MasterImage.App/MainWindow.xaml.cs
git commit -m "Add WPF app shell: single-instance startup, borderless fullscreen window"
```

---

## Task 12: Single-image view (display, scroll-to-zoom, drag-to-pan)

**Files:**
- Create: `src/MasterImage.App/Views/SingleImageView.xaml`
- Create: `src/MasterImage.App/Views/SingleImageView.xaml.cs`
- Modify: `src/MasterImage.App/MainWindow.xaml`
- Modify: `src/MasterImage.App/MainWindow.xaml.cs`

- [ ] **Step 1: Create `SingleImageView.xaml`**

```xml
<!-- src/MasterImage.App/Views/SingleImageView.xaml -->
<UserControl x:Class="MasterImage.App.Views.SingleImageView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid ClipToBounds="True" Background="Transparent">
        <Image x:Name="PhotoImage" Stretch="Uniform" RenderOptions.BitmapScalingMode="HighQuality">
            <Image.RenderTransform>
                <MatrixTransform x:Name="ImageTransform" />
            </Image.RenderTransform>
        </Image>
    </Grid>
</UserControl>
```

A single `MatrixTransform` (rather than a `ScaleTransform` + `TranslateTransform` pair with a fixed `RenderTransformOrigin`) is what makes cursor-anchored zoom straightforward: `Matrix.ScaleAt(factor, factor, cursorX, cursorY)` composes the scale-about-a-point and the resulting translation in one step, and `Matrix.Translate` handles panning on the same matrix. The `Background="Transparent"` on the Grid is load-bearing — without a brush the Grid isn't hit-testable and the control never receives mouse events.

- [ ] **Step 2: Create `SingleImageView.xaml.cs` with zoom-at-cursor and drag-to-pan**

```csharp
// src/MasterImage.App/Views/SingleImageView.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MasterImage.App.Views;

public partial class SingleImageView : UserControl
{
    private const double MinScale = 1.0;
    private const double MaxScale = 8.0;

    private Point _dragStart;
    private bool _isDragging;

    public SingleImageView()
    {
        InitializeComponent();
        MouseWheel += OnMouseWheel;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

    public void SetImage(BitmapSource? image)
    {
        PhotoImage.Source = image;
        ResetZoom();
    }

    public void ResetZoom() => ImageTransform.Matrix = Matrix.Identity;

    // Zoom anchored on the cursor (spec §5): scale about the pointer's position in this control's
    // coordinate space, so whatever detail is under the cursor stays under the cursor as you zoom,
    // instead of the image drifting toward its centre. MinScale 1.0 = fit-to-window (Stretch
    // Uniform), so you can zoom in to inspect and back out to the fitted view, but no further.
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var matrix = ImageTransform.Matrix;
        double currentScale = matrix.M11;
        double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;

        // Land exactly on the limit rather than overshooting it.
        double targetScale = Math.Clamp(currentScale * factor, MinScale, MaxScale);
        if (targetScale == currentScale)
        {
            e.Handled = true;
            return;
        }
        factor = targetScale / currentScale;

        if (targetScale == MinScale)
        {
            // Back at fit-to-window: drop any accumulated pan so the photo re-centres cleanly.
            ResetZoom();
        }
        else
        {
            var origin = e.GetPosition(this);
            matrix.ScaleAt(factor, factor, origin.X, origin.Y);
            ImageTransform.Matrix = matrix;
        }

        e.Handled = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ImageTransform.Matrix.M11 <= MinScale) return;
        _isDragging = true;
        _dragStart = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var current = e.GetPosition(this);
        var matrix = ImageTransform.Matrix;
        matrix.Translate(current.X - _dragStart.X, current.Y - _dragStart.Y);
        ImageTransform.Matrix = matrix;
        _dragStart = current;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ReleaseMouseCapture();
    }
}
```

`Matrix` is a struct, so `ImageTransform.Matrix` returns a *copy* — every handler must mutate the local and assign it back (`ImageTransform.Matrix = matrix`). Mutating the property's return value directly compiles but silently does nothing.

- [ ] **Step 3: Wire `SingleImageView` into `MainWindow`**

Update `MainWindow.xaml` — replace the `SingleImageHost` line:

```xml
<ContentControl x:Name="SingleImageHost">
    <local:SingleImageView x:Name="SingleImageViewControl" />
</ContentControl>
```

Add the namespace import to the `Window` root tag in `MainWindow.xaml`:

```xml
xmlns:local="clr-namespace:MasterImage.App.Views"
```

Update `MainWindow.xaml.cs` constructor to load and display the current photo after `DataContext` is set (append after `DataContext = ViewModel;` in both the constructor and `OnOpenRequestedFromAnotherProcess`):

```csharp
_ = LoadCurrentPhotoAsync();
```

And add the method:

```csharp
private async Task LoadCurrentPhotoAsync()
{
    var photo = ViewModel.CurrentPhoto;
    if (photo is null) return;

    var image = await Task.Run(() => Core.ImageLoader.TryLoadAtSize(photo.PrimaryFilePath, decodePixelWidth: 1920));
    SingleImageViewControl.SetImage(image);
}
```

- [ ] **Step 4: Build and manually verify**

Run: `dotnet run --project src/MasterImage.App -- "C:\Users\kaihu\Pictures\<some existing jpg>"`
Expected: The window opens showing that photo. Scroll up zooms in, scroll down zooms back out, click-drag pans while zoomed in.

- [ ] **Step 5: Commit**

```bash
git add src/MasterImage.App/Views/SingleImageView.xaml src/MasterImage.App/Views/SingleImageView.xaml.cs src/MasterImage.App/MainWindow.xaml src/MasterImage.App/MainWindow.xaml.cs
git commit -m "Add single-image view with scroll zoom and drag pan"
```

---

## Task 13: Left/Right seek + M mark + N cull wiring

**Files:**
- Modify: `src/MasterImage.App/MainWindow.xaml.cs`

- [ ] **Step 1: Extend `MainWindow_PreviewKeyDown` with Left/Right/M/N**

Replace the method body with:

```csharp
private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
{
    switch (e.Key)
    {
        case Key.F:
            ToggleFullscreen();
            e.Handled = true;
            break;

        case Key.Escape:
            if (ViewModel.IsShortcutsOverlayVisible)
            {
                ViewModel.IsShortcutsOverlayVisible = false;
            }
            else if (ViewModel.IsFullscreen)
            {
                ToggleFullscreen();
            }
            e.Handled = true;
            break;

        case Key.Right:
            ViewModel.SeekNext();
            _ = LoadCurrentPhotoAsync();
            e.Handled = true;
            break;

        case Key.Left:
            ViewModel.SeekPrevious();
            _ = LoadCurrentPhotoAsync();
            e.Handled = true;
            break;

        case Key.M:
            ViewModel.ToggleMark();
            e.Handled = true;
            break;

        case Key.N:
            var result = ViewModel.MoveMarkedToSelected();
            _ = LoadCurrentPhotoAsync();
            MessageBox.Show(
                $"Moved {result.MovedFileCount} file(s) to selected/." +
                (result.Failures.Count > 0 ? $"\n{result.Failures.Count} failure(s) — see below:\n{string.Join("\n", result.Failures)}" : ""),
                "Master Image");
            e.Handled = true;
            break;
    }
}
```

`MessageBox.Show` for the `N` result is a placeholder-free, working notification — it will be replaced by the fade-in/out navigation overlay banner in Task 15 once that exists, but is fully functional in the meantime (it does not block the app from working, just isn't the polished final overlay).

- [ ] **Step 2: Build and manually verify**

Run: `dotnet run --project src/MasterImage.App -- "<folder with several jpgs>"`
Expected:
- `Right`/`Left` change the displayed photo, wrapping at the ends.
- `M` toggles a mark (verify via Task 15's overlay once built, or temporarily by checking `.thumbnails/marks.json` in the folder after pressing `M`).
- `N` creates a `selected/` subfolder containing the marked file(s), shows a message box with the move count, and the app continues working on the remaining photos afterward.

- [ ] **Step 3: Commit**

```bash
git add src/MasterImage.App/MainWindow.xaml.cs
git commit -m "Wire Left/Right seek, M mark, and N cull-move shortcuts"
```

---

## Task 14: Tile grid view (Shift-hold overview + Shift+scroll tile size)

**Files:**
- Create: `src/MasterImage.App/Views/TileGridView.xaml`
- Create: `src/MasterImage.App/Views/TileGridView.xaml.cs`
- Modify: `src/MasterImage.App/MainWindow.xaml`
- Modify: `src/MasterImage.App/MainWindow.xaml.cs`

- [ ] **Step 1: Create `TileGridView.xaml`**

**Virtualization requires a virtualizing panel — WPF's `WrapPanel` is not one.** `VirtualizingPanel.IsVirtualizing` only takes effect when the `ItemsPanel` derives from `VirtualizingPanel`; a plain `WrapPanel` doesn't, so setting it there silently does nothing and every tile gets realized up front. That breaks spec §7 ("recycles a fixed pool of Image elements... smooth into the thousands of tiles") and, worse, spec §6 — `Tile_Loaded` would fire for *every* photo the moment Shift is pressed, kicking off thumbnail generation for the entire folder instead of just on-screen tiles. WPF ships no virtualizing wrap/grid panel, so we use the `VirtualizingWrapPanel` package (MIT, added via `dotnet add src/MasterImage.App/MasterImage.App.csproj package VirtualizingWrapPanel`), which provides one with proper container recycling.

```xml
<!-- src/MasterImage.App/Views/TileGridView.xaml -->
<UserControl x:Class="MasterImage.App.Views.TileGridView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:core="clr-namespace:MasterImage.Core;assembly=MasterImage.Core"
             xmlns:vwp="clr-namespace:WpfToolkit.Controls;assembly=VirtualizingWrapPanel">
    <Border Background="#111111">
        <ListBox x:Name="TileList"
                 Background="Transparent"
                 BorderThickness="0"
                 ScrollViewer.CanContentScroll="True"
                 VirtualizingPanel.IsVirtualizing="True"
                 VirtualizingPanel.VirtualizationMode="Recycling"
                 SelectionMode="Single">
            <ListBox.ItemsPanel>
                <ItemsPanelTemplate>
                    <vwp:VirtualizingWrapPanel Orientation="Horizontal" />
                </ItemsPanelTemplate>
            </ListBox.ItemsPanel>
            <ListBox.ItemTemplate>
                <DataTemplate DataType="{x:Type core:PhotoItem}">
                    <Border Width="{Binding DataContext.TileSize, RelativeSource={RelativeSource AncestorType=UserControl}}"
                            Height="{Binding DataContext.TileSize, RelativeSource={RelativeSource AncestorType=UserControl}}"
                            Margin="4" Background="#222222" Tag="{Binding}" Loaded="Tile_Loaded">
                        <Grid>
                            <Image x:Name="TileImage" Stretch="Uniform" />
                            <TextBlock x:Name="MarkBadge" Text="&#9733;" Foreground="Gold" FontWeight="Bold"
                                       FontSize="18" HorizontalAlignment="Right" VerticalAlignment="Top"
                                       Margin="4" Visibility="Collapsed" />
                        </Grid>
                    </Border>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
    </Border>
</UserControl>
```

- [ ] **Step 2: Create `TileGridView.xaml.cs` with on-demand thumbnail loading, Shift+scroll tile-size, and click-to-jump**

```csharp
// src/MasterImage.App/Views/TileGridView.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MasterImage.App.ViewModels;
using MasterImage.Core;

namespace MasterImage.App.Views;

public partial class TileGridView : UserControl
{
    public event Action<PhotoItem>? TileClicked;

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public TileGridView()
    {
        InitializeComponent();
        MouseWheel += OnMouseWheel;
        TileList.MouseUp += OnTileListMouseUp;
    }

    public void SetItems(IReadOnlyList<PhotoItem> items)
    {
        TileList.ItemsSource = items;
    }

    // Which photo is under the cursor right now, or null if the cursor isn't over a tile.
    // Used on Shift-release to land on the tile the user was pointing at (spec §7).
    public PhotoItem? GetHoveredItem()
    {
        var hit = VisualTreeHelper.HitTest(TileList, Mouse.GetPosition(TileList));
        DependencyObject? node = hit?.VisualHit;

        while (node is not null and not ListBoxItem)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        return (node as ListBoxItem)?.DataContext as PhotoItem;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // While the grid is visible, Shift is already held (that's how the grid opened),
        // so any wheel event here is inherently a "Shift + scroll" per spec §7.
        // Tile Width/Height are bound straight to ViewModel.TileSize in XAML, so updating
        // it here is enough to resize every tile live.
        if (ViewModel is null) return;
        ViewModel.TileSize += e.Delta > 0 ? 20 : -20;
        e.Handled = true;
    }

    private void OnTileListMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (TileList.SelectedItem is PhotoItem item)
        {
            TileClicked?.Invoke(item);
        }
    }

    private void Tile_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement border || border.Tag is not PhotoItem item || ViewModel is null) return;

        var image = (Image)border.FindName("TileImage")!;
        var badge = (TextBlock)border.FindName("MarkBadge")!;

        badge.Visibility = ViewModel.IsMarked(item) ? Visibility.Visible : Visibility.Collapsed;

        _ = LoadTileThumbnailAsync(image, item);
    }

    private async Task LoadTileThumbnailAsync(Image image, PhotoItem item)
    {
        var thumbnail = await ViewModel!.GetThumbnailAsync(item);
        if (thumbnail is not null)
        {
            image.Source = thumbnail;
        }
    }
}
```

The `Loaded` event fires each time virtualization brings a container on-screen — including when a recycled container is reused for a different item as you scroll — which is exactly the "generate on-demand for visible tiles only" behavior from spec §6. This only holds because the `ItemsPanel` is an actual virtualizing panel (see above); with a plain `WrapPanel` it would fire for every photo at once.

Note `Tile_Loaded` reads `border.Tag` (the `PhotoItem`) rather than closing over a captured item, because with `VirtualizationMode="Recycling"` the same `Border` instance is reused for different photos as you scroll — the `Tag` binding is what keeps it pointing at the item currently being shown.

- [ ] **Step 3: Wire `TileGridView` into `MainWindow` with Shift-hold show/hide**

Update `MainWindow.xaml` — replace the `GridHost` line:

```xml
<ContentControl x:Name="GridHost" Visibility="Collapsed">
    <local:TileGridView x:Name="TileGridViewControl" />
</ContentControl>
```

Update `MainWindow.xaml.cs`: add `PreviewKeyUp` handling for releasing Shift, and bind the grid's visibility/hovered-tile-jump behavior. Add to the constructor (after the existing `PreviewKeyDown += ...` line):

```csharp
PreviewKeyUp += MainWindow_PreviewKeyUp;
TileGridViewControl.SetItems(ViewModel.Photos);
TileGridViewControl.TileClicked += OnTileClicked;
```

`SetItems` hands the grid the *current* `Photos` list imperatively, so it must be re-called every time `ViewModel.Photos` is replaced — otherwise the grid keeps rendering a stale list. `MainViewModel.MoveMarkedToSelected` reassigns `_photos` after a cull, and `OnOpenRequestedFromAnotherProcess` swaps in a whole new view model, so both need a refresh. Add this helper:

```csharp
private void RefreshGridItems() => TileGridViewControl.SetItems(ViewModel.Photos);
```

and call it at the end of `OnOpenRequestedFromAnotherProcess` (right after `DataContext = ViewModel;`):

```csharp
RefreshGridItems();
```

(Task 15's cull handler calls it too, once `N` is wired to reload the photo list.)

Add these methods:

```csharp
// Releasing Shift closes the overview and lands on whichever tile the cursor was over
// (spec §7 press-and-hold); if the cursor wasn't over a tile, stay on the current photo.
private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
{
    if (e.Key is not (Key.LeftShift or Key.RightShift))
    {
        return;
    }

    var hovered = TileGridViewControl.GetHoveredItem();
    bool jumped = hovered is not null && TryJumpTo(hovered);

    HideGrid();
    if (jumped)
    {
        _ = LoadCurrentPhotoAsync();
    }
}

private void OnTileClicked(PhotoItem item)
{
    if (!TryJumpTo(item))
    {
        return;
    }

    HideGrid();
    _ = LoadCurrentPhotoAsync();
}

private bool TryJumpTo(PhotoItem item)
{
    int index = ViewModel.Photos.ToList().IndexOf(item);
    if (index < 0)
    {
        return false;
    }

    ViewModel.JumpTo(index);
    return true;
}

private void HideGrid()
{
    ViewModel.IsGridVisible = false;
    GridHost.Visibility = Visibility.Collapsed;
    SingleImageHost.Visibility = Visibility.Visible;
}
```

Add a case to `MainWindow_PreviewKeyDown`'s `switch`:

```csharp
case Key.LeftShift:
case Key.RightShift:
    ViewModel.IsGridVisible = true;
    GridHost.Visibility = Visibility.Visible;
    SingleImageHost.Visibility = Visibility.Collapsed;
    e.Handled = true;
    break;
```

- [ ] **Step 4: Build and manually verify**

Run: `dotnet run --project src/MasterImage.App -- "<folder with 10+ jpgs>"`
Expected:
- Holding `Shift` shows a grid of tiles; thumbnails appear as they scroll into view (first paint might show empty tiles briefly, then fill in).
- While still holding `Shift`, scrolling the mouse wheel changes tile size live.
- Releasing `Shift` returns to single-image view.
- Clicking a tile while the grid is visible (i.e., while still holding Shift and clicking) jumps to that photo and returns to single view.
- A gold star badge appears in the corner of any tile whose photo is currently marked (press `M` on a photo first, per Task 13, then hold `Shift` to confirm its tile shows the badge).

- [ ] **Step 5: Commit**

```bash
git add src/MasterImage.App/Views/TileGridView.xaml src/MasterImage.App/Views/TileGridView.xaml.cs src/MasterImage.App/MainWindow.xaml src/MasterImage.App/MainWindow.xaml.cs
git commit -m "Add tile grid view with Shift-hold overview and Shift+scroll resize"
```

---

## Task 15: L full preload + navigation overlay (filename/position/mark, fading)

**Files:**
- Create: `src/MasterImage.App/Views/NavigationOverlay.xaml`
- Create: `src/MasterImage.App/Views/NavigationOverlay.xaml.cs`
- Modify: `src/MasterImage.App/MainWindow.xaml`
- Modify: `src/MasterImage.App/MainWindow.xaml.cs`

- [ ] **Step 1: Create `NavigationOverlay.xaml`**

```xml
<!-- src/MasterImage.App/Views/NavigationOverlay.xaml -->
<UserControl x:Class="MasterImage.App.Views.NavigationOverlay"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             IsHitTestVisible="False">
    <Grid VerticalAlignment="Bottom" HorizontalAlignment="Left" Margin="16">
        <Border x:Name="OverlayPanel" Background="#AA000000" CornerRadius="4" Padding="10,6" Opacity="0">
            <StackPanel Orientation="Horizontal">
                <TextBlock x:Name="MarkIndicator" Text="&#9733; " Foreground="Gold" FontWeight="Bold" Visibility="Collapsed" />
                <TextBlock x:Name="FileNameText" Foreground="White" />
                <TextBlock x:Name="PositionText" Foreground="#CCCCCC" Margin="10,0,0,0" />
            </StackPanel>
        </Border>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create `NavigationOverlay.xaml.cs` with a show-then-fade animation**

```csharp
// src/MasterImage.App/Views/NavigationOverlay.xaml.cs
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using MasterImage.Core;

namespace MasterImage.App.Views;

public partial class NavigationOverlay : UserControl
{
    public NavigationOverlay()
    {
        InitializeComponent();
    }

    public void Show(PhotoItem photo, int index, int total, bool isMarked)
    {
        FileNameText.Text = Path.GetFileName(photo.PrimaryFilePath);
        PositionText.Text = $"{index + 1}/{total}";
        MarkIndicator.Visibility = isMarked ? Visibility.Visible : Visibility.Collapsed;
        FadeOutAfter(TimeSpan.FromSeconds(1.2));
    }

    // Longer on-screen time than Show(): a sentence is read-heavy compared with a filename.
    public void ShowMessage(string message)
    {
        SetMessage(message);
        FadeOutAfter(TimeSpan.FromSeconds(2.5));
    }

    // Stays put until something else replaces it — for long-running progress, where a fade-out
    // would hide the status while the operation is still going.
    public void ShowSticky(string message)
    {
        SetMessage(message);
        OverlayPanel.BeginAnimation(OpacityProperty, null);
        OverlayPanel.Opacity = 1;
    }

    private void SetMessage(string message)
    {
        FileNameText.Text = message;
        PositionText.Text = "";
        MarkIndicator.Visibility = Visibility.Collapsed;
    }

    private void FadeOutAfter(TimeSpan delay)
    {
        // Clear any in-flight animation first, otherwise the previous fade keeps running and
        // the panel we just set to fully opaque immediately starts vanishing again.
        OverlayPanel.BeginAnimation(OpacityProperty, null);
        OverlayPanel.Opacity = 1;

        OverlayPanel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(1.5))) { BeginTime = delay });
    }
}
```

`ShowMessage` is the general-purpose banner — used next to replace the interim `N`-key `MessageBox.Show` from Task 13 with a non-blocking overlay, since a modal dialog on every cull-move would break the keyboard-driven flow this app is built around. `ShowSticky` backs the `L` preload progress readout (see below), which must stay visible for the whole operation rather than fading after a couple of seconds.

The element is named `OverlayPanel`, not `Panel`: `x:Name="Panel"` would generate a field that shadows the `System.Windows.Controls.Panel` type in this file's scope — legal but needlessly confusing.

- [ ] **Step 3: Wire the overlay into `MainWindow`, and add `L` for full preload**

Update `MainWindow.xaml` — replace the `NavigationOverlayHost` line:

```xml
<ContentControl x:Name="NavigationOverlayHost">
    <local:NavigationOverlay x:Name="NavigationOverlayControl" />
</ContentControl>
```

In `MainWindow.xaml.cs`, update `LoadCurrentPhotoAsync` to also trigger the overlay:

```csharp
private async Task LoadCurrentPhotoAsync()
{
    var photo = ViewModel.CurrentPhoto;
    if (photo is null) return;

    var image = await Task.Run(() => Core.ImageLoader.TryLoadAtSize(photo.PrimaryFilePath, decodePixelWidth: 1920));
    SingleImageViewControl.SetImage(image);
    NavigationOverlayControl.Show(photo, ViewModel.CurrentIndex, ViewModel.Photos.Count, ViewModel.IsCurrentMarked);
}
```

Also call `NavigationOverlayControl.Show(...)` right after `ViewModel.ToggleMark();` in the `Key.M` case, so the mark indicator updates immediately without waiting for the next seek:

```csharp
case Key.M:
    ViewModel.ToggleMark();
    if (ViewModel.CurrentPhoto is not null)
    {
        NavigationOverlayControl.Show(ViewModel.CurrentPhoto, ViewModel.CurrentIndex, ViewModel.Photos.Count, ViewModel.IsCurrentMarked);
    }
    e.Handled = true;
    break;
```

Also replace the `Key.N` case (which used a blocking `MessageBox.Show` in Task 13) with the non-blocking overlay. `LoadCurrentPhotoAsync` (called below) also calls `NavigationOverlayControl.Show(...)` once the next photo decodes, which would otherwise race with and overwrite the cull summary — so this awaits that first, then shows the summary last:

```csharp
case Key.N:
    _ = HandleCullMoveAsync();
    e.Handled = true;
    break;
```

Add this method alongside `LoadCurrentPhotoAsync`:

```csharp
private async Task HandleCullMoveAsync()
{
    var result = ViewModel.MoveMarkedToSelected();
    RefreshGridItems(); // Photos was just reassigned — the grid's ItemsSource would otherwise
                        // still list the photos that just moved into selected/.
    await LoadCurrentPhotoAsync();

    string cullMessage = $"Moved {result.MovedFileCount} file(s) to selected/." +
        (result.Failures.Count > 0 ? $" {result.Failures.Count} failed (already existed in selected/)." : "");
    NavigationOverlayControl.ShowMessage(cullMessage);
}
```

Add the `L` case to the switch:

```csharp
case Key.L:
    _ = HandlePreloadAllAsync();
    e.Handled = true;
    break;
```

And add this method alongside `HandleCullMoveAsync`:

```csharp
private async Task HandlePreloadAllAsync()
{
    int total = ViewModel.Photos.Count;
    if (total == 0)
    {
        NavigationOverlayControl.ShowMessage("Nothing to preload — no photos in this folder.");
        return;
    }

    // Without a readout, L looks like it does nothing: on a big shoot the work takes tens of
    // seconds with no other visible effect (thumbnails only surface later, in the grid).
    NavigationOverlayControl.ShowSticky($"Preloading 0/{total}…");

    // Progress<T> marshals every report onto the UI thread, so on a folder of thousands that's
    // thousands of callbacks — repaint every 10th (and the last) rather than each one.
    var progress = new Progress<int>(done =>
    {
        if (done % 10 == 0 || done == total)
        {
            NavigationOverlayControl.ShowSticky($"Preloading {done}/{total}…");
        }
    });

    await ViewModel.PreloadAllAsync(progress);
    NavigationOverlayControl.ShowMessage($"Preloaded {total} thumbnails.");
}
```

- [ ] **Step 4: Build and manually verify**

Run: `dotnet run --project src/MasterImage.App -- "<folder with several jpgs>"`
Expected:
- Seeking Left/Right shows a bottom-left overlay with filename and "N/Total" that fades out after ~1.2s.
- Pressing `M` immediately shows/hides a gold star indicator in that overlay.
- Pressing `L` on a folder with many photos: check `.thumbnails/` afterward — every photo should have a corresponding cached `.jpg`, generated noticeably faster than scrolling through the grid one screen at a time would.

- [ ] **Step 5: Commit**

```bash
git add src/MasterImage.App/Views/NavigationOverlay.xaml src/MasterImage.App/Views/NavigationOverlay.xaml.cs src/MasterImage.App/MainWindow.xaml src/MasterImage.App/MainWindow.xaml.cs
git commit -m "Add L full-preload shortcut and fading navigation overlay"
```

---

## Task 16: Shortcuts overlay (I key)

**Files:**
- Create: `src/MasterImage.App/Views/ShortcutsOverlay.xaml`
- Create: `src/MasterImage.App/Views/ShortcutsOverlay.xaml.cs`
- Modify: `src/MasterImage.App/MainWindow.xaml`
- Modify: `src/MasterImage.App/MainWindow.xaml.cs`

- [ ] **Step 1: Create `ShortcutsOverlay.xaml`** reflecting the full reference table from spec §10

```xml
<!-- src/MasterImage.App/Views/ShortcutsOverlay.xaml -->
<UserControl x:Class="MasterImage.App.Views.ShortcutsOverlay"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Border Background="#DD000000">
        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Width="460">
            <TextBlock Text="Shortcuts" FontSize="20" FontWeight="Bold" Foreground="White" Margin="0,0,0,12" />
            <TextBlock Foreground="White" Text="Left / Right — seek previous/next photo" Margin="0,2" />
            <TextBlock Foreground="White" Text="Scroll (single view) — zoom in/out at cursor" Margin="0,2" />
            <TextBlock Foreground="White" Text="Click + drag (zoomed) — pan" Margin="0,2" />
            <TextBlock Foreground="White" Text="Shift (hold) — show tile grid overview" Margin="0,2" />
            <TextBlock Foreground="White" Text="Shift + scroll (grid open) — adjust tile size" Margin="0,2" />
            <TextBlock Foreground="White" Text="L — force full-folder thumbnail preload" Margin="0,2" />
            <TextBlock Foreground="White" Text="M — mark/unmark current photo" Margin="0,2" />
            <TextBlock Foreground="White" Text="N — move marked photos into selected/" Margin="0,2" />
            <TextBlock Foreground="White" Text="F — toggle fullscreen / windowed" Margin="0,2" />
            <TextBlock Foreground="White" Text="I — toggle this panel" Margin="0,2" />
            <TextBlock Foreground="White" Text="Esc — close panel / exit fullscreen" Margin="0,2" />
            <TextBlock Foreground="#CCCCCC" Text="Alt+F4 — close Master Image" Margin="0,2" />
        </StackPanel>
    </Border>
</UserControl>
```

Alt+F4 is listed deliberately: the window is borderless with no title bar and `Esc` intentionally never quits (so a stray Esc mid-cull can't lose your session), which leaves no otherwise-discoverable way to exit. The list is a plain `StackPanel` of `TextBlock`s rather than an `ItemsControl` with literal `Items` — same result, one less layer of indirection for what is static text.

- [ ] **Step 2: Create `ShortcutsOverlay.xaml.cs`** (no logic needed beyond the generated partial class)

```csharp
// src/MasterImage.App/Views/ShortcutsOverlay.xaml.cs
using System.Windows.Controls;

namespace MasterImage.App.Views;

public partial class ShortcutsOverlay : UserControl
{
    public ShortcutsOverlay()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Wire it into `MainWindow` with the `I` toggle**

Bind the host's visibility to the view model rather than assigning `Visibility` by hand. WPF ships `BooleanToVisibilityConverter`, and `MainViewModel` already raises `PropertyChanged` for `IsShortcutsOverlayVisible`, so one binding keeps the two in sync forever — otherwise every place that flips the flag (the `I` toggle, `Esc`) has to remember to also set `Visibility`, and the day one of them forgets, `Esc` clears the flag while the panel stays on screen.

Add the converter to `MainWindow.xaml`'s resources (just inside the `<Window>` tag, before the root `Grid`):

```xml
<Window.Resources>
    <BooleanToVisibilityConverter x:Key="BoolToVis" />
</Window.Resources>
```

and replace the `ShortcutsOverlayHost` line with:

```xml
<ContentControl x:Name="ShortcutsOverlayHost"
                Visibility="{Binding IsShortcutsOverlayVisible, Converter={StaticResource BoolToVis}}">
    <local:ShortcutsOverlay />
</ContentControl>
```

(The window's `DataContext` is already the `MainViewModel`, and `OnOpenRequestedFromAnotherProcess` re-assigns it, so the binding re-resolves against the new view model automatically.)

Now the `I` case is just a flag flip — add it to `MainWindow_PreviewKeyDown`'s switch:

```csharp
case Key.I:
    ViewModel.IsShortcutsOverlayVisible = !ViewModel.IsShortcutsOverlayVisible;
    e.Handled = true;
    break;
```

The existing `Key.Escape` case already sets `ViewModel.IsShortcutsOverlayVisible = false`, so it now closes the panel with no change needed.

- [ ] **Step 4: Build and manually verify**

Run: `dotnet run --project src/MasterImage.App -- "<any folder with photos>"`
Expected: Pressing `I` shows the full shortcut list centered on screen; pressing `I` again or `Esc` closes it.

- [ ] **Step 5: Commit**

```bash
git add src/MasterImage.App/Views/ShortcutsOverlay.xaml src/MasterImage.App/Views/ShortcutsOverlay.xaml.cs src/MasterImage.App/MainWindow.xaml src/MasterImage.App/MainWindow.xaml.cs
git commit -m "Add I shortcuts overlay"
```

---

## Task 17: End-to-end manual verification pass

**Files:** none (verification only)

**Known/expected behavior, not a bug:** opening any folder creates a hidden `.thumbnails` subfolder in it immediately, even before any thumbnail is generated — including in `My Pictures` when the app is launched with no arguments (it defaults there). This is deliberate: `ThumbnailCache`'s constructor creates *and hides* the folder up front, which is what guarantees it's already hidden before `MarksStore` writes `marks.json` into it. Making creation lazy would let whichever writer got there first create it un-hidden.

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test`
Expected: All tests across `MasterImage.Core.Tests` pass (35 tests from Tasks 2–10).

- [ ] **Step 2: Prepare a real test folder**

Create (or point at) a folder with at least 15 photos in mixed JPEG/PNG formats and non-sequential-looking filenames (e.g. some numbered DSC1/DSC2/.../DSC12).

- [ ] **Step 3: Walk through every row of the spec's keyboard reference table (§10) against the running app**

Run: `dotnet run --project src/MasterImage.App -- "<that folder>"`

Verify each:
- `Left`/`Right` seek through all photos and wrap at both ends.
- Scroll zooms in/out on the current photo; click-drag pans while zoomed.
- Holding `Shift` shows the grid; releasing returns to single view.
- Holding `Shift` and scrolling resizes tiles live.
- `L` preloads thumbnails for the whole folder (confirm via `.thumbnails/*.jpg` file count matching photo count).
- `M` toggles the mark indicator in the overlay; closing and reopening the app on the same folder preserves marks (confirm `.thumbnails/marks.json` contents).
- `N` moves marked photos (and both files of any pair) into `selected/`, shows a summary, and the app keeps working afterward with a valid current photo.
- `F` toggles fullscreen; `Esc` also exits fullscreen.
- `I` toggles the shortcuts panel; `Esc` also closes it.
- Opening a second photo (`dotnet run --project src/MasterImage.App -- "<other file>"` while the first instance is still open) reuses the existing window instead of opening a second one.

- [ ] **Step 4: Fix any discrepancies found, then commit**

If any check above fails, fix the specific file involved and re-verify just that behavior before moving on.

```bash
git add -A
git commit -m "Complete core viewer end-to-end verification pass"
```
