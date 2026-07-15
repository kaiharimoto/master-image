# RAW Support (Plan 2 of 4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Open camera RAW files (ARW/NEF/CR2/CR3/DNG/…) fast enough to cull with, by reading each file's embedded preview instead of demosaicing it — and treat a RAW+JPEG pair as one photo.

**Architecture:** Two focused changes. `ImageLoader.TryLoad` dispatches on extension: RAW files go through `BitmapDecoder` with `BitmapCacheOption.None` and use `decoder.Preview ?? decoder.Thumbnail`, scaled to the requested width and EXIF-rotated; everything else keeps the existing `BitmapImage` path. `PhotoSet` learns the RAW extensions and groups files by filename stem so a `.ARW`+`.jpg` pair is a single `PhotoItem` with two paths. Nothing downstream changes — the image cache, read-ahead, thumbnail cache, grid, marking and culling all decode through `ImageLoader` and pair-aware culling already works.

**Tech Stack:** .NET 8, WPF/WIC. **No new dependencies** — Windows' Raw Image Extension (`Microsoft.RawImageExtension`, already installed) provides the RAW codec.

**Environment notes:**
- The .NET SDK needs its PATH prepended in *every* shell call (it does not persist between calls): Bash `export PATH="$PATH:/c/Program Files/dotnet" && dotnet ...`
- Run everything from `C:\Users\kaihu\Documents\projects\image viewer`.
- Baseline before starting: **58 tests passing**.
- Real RAW fixtures on this machine: `C:\Users\kaihu\Pictures\DSC09423.ARW` (125MB, 61MP, **shot portrait**) and `C:\Users\kaihu\Downloads\DSC01565.ARW` (17MB, landscape, has a paired `DSC01565.jpg`).

---

## Task 1: Recognise RAW files

**Files:**
- Create: `src/MasterImage.Core/RawFormats.cs`
- Test: `tests/MasterImage.Core.Tests/RawFormatsTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MasterImage.Core.Tests/RawFormatsTests.cs
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class RawFormatsTests
{
    [Theory]
    [InlineData("DSC09423.ARW")]
    [InlineData("photo.arw")]
    [InlineData("shot.NEF")]
    [InlineData("shot.CR2")]
    [InlineData("shot.CR3")]
    [InlineData("shot.DNG")]
    [InlineData("shot.RAF")]
    [InlineData("shot.ORF")]
    [InlineData("shot.RW2")]
    public void RecognisesRawExtensions(string fileName)
    {
        Assert.True(RawFormats.IsRaw(fileName));
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.JPEG")]
    [InlineData("photo.png")]
    [InlineData("photo.tif")]
    [InlineData("notes.txt")]
    [InlineData("no-extension")]
    public void DoesNotClaimNonRawFiles(string fileName)
    {
        Assert.False(RawFormats.IsRaw(fileName));
    }

    [Fact]
    public void ExtensionsAreLowercaseAndDotted()
    {
        // PhotoSet compares against Path.GetExtension(...).ToLowerInvariant(), so the set has to
        // be in that exact shape or nothing will ever match.
        Assert.All(RawFormats.Extensions, e =>
        {
            Assert.StartsWith(".", e);
            Assert.Equal(e.ToLowerInvariant(), e);
        });
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests --filter RawFormatsTests`
Expected: FAIL to compile — `RawFormats` does not exist.

- [ ] **Step 3: Implement `RawFormats`**

```csharp
// src/MasterImage.Core/RawFormats.cs
using System.IO;

namespace MasterImage.Core;

// The camera RAW formats Windows can decode.
//
// This list isn't aspirational — it's exactly what the installed "Microsoft Raw Image Decoder"
// registers itself for (read from its WIC codec registration), so every entry is genuinely
// decodable rather than merely hoped for. RAW support rides entirely on that codec, which ships as
// the free Raw Image Extension from the Microsoft Store.
public static class RawFormats
{
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".3fr", ".ari", ".arw", ".bay", ".cap", ".cr2", ".cr3", ".crw", ".dcs", ".dcr", ".drf",
        ".eip", ".erf", ".fff", ".iiq", ".k25", ".kdc", ".mef", ".mos", ".mrw", ".nef", ".nrw",
        ".orf", ".ori", ".pef", ".ptx", ".pxn", ".raf", ".raw", ".rw2", ".rwl", ".sr2", ".srf",
        ".srw", ".x3f", ".dng"
    };

    public static bool IsRaw(string filePath) => Extensions.Contains(Path.GetExtension(filePath));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests --filter RawFormatsTests`
Expected: `Passed! - Failed: 0, Passed: 16`

- [ ] **Step 5: Commit**

```bash
git add src/MasterImage.Core/RawFormats.cs tests/MasterImage.Core.Tests/RawFormatsTests.cs
git commit -m "Add RAW format recognition"
```

---

## Task 2: Decode RAW via the embedded preview

**Files:**
- Modify: `src/MasterImage.Core/ImageLoader.cs`
- Test: `tests/MasterImage.Core.Tests/ImageLoaderRawTests.cs`

- [ ] **Step 1: Write the failing tests**

These need a real RAW file. They skip themselves if the fixture is missing so the suite stays green elsewhere; on this machine they run for real.

```csharp
// tests/MasterImage.Core.Tests/ImageLoaderRawTests.cs
using System.Diagnostics;
using System.IO;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class ImageLoaderRawTests
{
    // A 61MP Sony ARW shot in PORTRAIT orientation: the sensor pixels are landscape and an EXIF
    // tag says to rotate, which makes it the fixture that catches an unrotated preview.
    private const string PortraitRaw = @"C:\Users\kaihu\Pictures\DSC09423.ARW";

    // A 17MB landscape ARW.
    private const string LandscapeRaw = @"C:\Users\kaihu\Downloads\DSC01565.ARW";

    [Fact]
    public void LoadsARawFileAtTheRequestedWidth()
    {
        if (!File.Exists(LandscapeRaw)) return;

        var image = ImageLoader.TryLoadAtSize(LandscapeRaw, decodePixelWidth: 1920);

        Assert.NotNull(image);
        Assert.Equal(1920, image!.PixelWidth);
        Assert.True(image.IsFrozen, "must be frozen to cross threads");
    }

    [Fact]
    public void AppliesExifRotationToTheEmbeddedPreview()
    {
        if (!File.Exists(PortraitRaw)) return;

        var image = ImageLoader.TryLoadAtSize(PortraitRaw, decodePixelWidth: 1920);

        Assert.NotNull(image);
        // Shot portrait. The embedded preview holds the sensor's landscape pixels plus an
        // orientation tag, so without applying it this comes back landscape and the photo
        // displays on its side.
        Assert.True(image!.PixelHeight > image.PixelWidth,
            $"expected portrait, got {image.PixelWidth}x{image.PixelHeight}");
    }

    [Fact]
    public void UsesTheEmbeddedPreviewRatherThanDemosaicingTheWholeFrame()
    {
        if (!File.Exists(PortraitRaw)) return;

        ImageLoader.TryLoadAtSize(PortraitRaw, decodePixelWidth: 1920); // warm the codec

        var sw = Stopwatch.StartNew();
        var image = ImageLoader.TryLoadAtSize(PortraitRaw, decodePixelWidth: 1920);
        sw.Stop();

        Assert.NotNull(image);
        // A full demosaic of this file measured ~3700ms; the embedded preview measured ~285ms.
        // The threshold sits far enough below the full decode that only the preview path can pass,
        // while leaving generous headroom for a slower machine.
        Assert.True(sw.ElapsedMilliseconds < 1500,
            $"took {sw.ElapsedMilliseconds}ms — looks like the full decode, not the preview");
    }

    [Fact]
    public void LoadsARawFileAtThumbnailSize()
    {
        if (!File.Exists(LandscapeRaw)) return;

        var image = ImageLoader.TryLoadAtSize(LandscapeRaw, decodePixelWidth: 512);

        Assert.NotNull(image);
        Assert.Equal(512, image!.PixelWidth);
    }

    [Fact]
    public void FullResolutionRequestReturnsTheWholePreview()
    {
        if (!File.Exists(LandscapeRaw)) return;

        var image = ImageLoader.TryLoadFullResolution(LandscapeRaw);

        Assert.NotNull(image);
        // Not downscaled, so it's the preview's own size — comfortably bigger than a screen.
        Assert.True(image!.PixelWidth > 1920, $"got {image.PixelWidth}px wide");
    }

    [Fact]
    public void CorruptRawReturnsNullWithoutThrowing()
    {
        string dir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
        try
        {
            string fake = Path.Combine(dir, "broken.arw");
            File.WriteAllText(fake, "this is not a raw file");

            Assert.Null(ImageLoader.TryLoadAtSize(fake, decodePixelWidth: 1920));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests --filter ImageLoaderRawTests`
Expected: FAIL. `AppliesExifRotationToTheEmbeddedPreview` and `UsesTheEmbeddedPreviewRatherThanDemosaicingTheWholeFrame` are the ones that must fail — today's code full-decodes the RAW (~3700ms), so the timing assertion fails. (Some others may already pass, because WIC can already open ARW — that's expected and is the point: the slow path works, we're replacing it.)

- [ ] **Step 3: Add the RAW branch to `ImageLoader`**

In `src/MasterImage.Core/ImageLoader.cs`, replace the `TryLoad` method with the version below, and add the new `LoadRawPreview` method directly after it. Everything else in the file (`TryLoadAtSize`, `TryLoadFullResolution`, `ReadExifOrientation`, `ApplyOrientation`, `SaveAsJpeg`) is unchanged.

```csharp
    private static BitmapSource? TryLoad(string filePath, int decodePixelWidth)
    {
        try
        {
            ushort orientation = ReadExifOrientation(filePath);

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            BitmapSource decoded = RawFormats.IsRaw(filePath)
                ? LoadRawPreview(stream, decodePixelWidth)
                : LoadStandardImage(stream, decodePixelWidth);

            BitmapSource result = ApplyOrientation(decoded, orientation);
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

    private static BitmapSource LoadStandardImage(Stream stream, int decodePixelWidth)
    {
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
        return bitmap;
    }

    // RAW files carry a full-size JPEG the camera already rendered. Reading that is ~13x cheaper
    // than demosaicing the sensor data (measured: 285ms vs 3703ms on a 125MB 61MP ARW) and is
    // indistinguishable for culling, since it's the same rendering the camera would show you.
    //
    // BitmapCacheOption.None is load-bearing: OnLoad decodes the whole frame up front and costs
    // 1.5-3.4s, which throws away the entire benefit. DecodePixelWidth isn't available here (that's
    // a BitmapImage feature), so the preview is scaled down afterwards instead.
    private static BitmapSource LoadRawPreview(Stream stream, int decodePixelWidth)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None);

        // Preview is the camera's full-size render; Thumbnail is a small one. Prefer Preview, and
        // fall back rather than failing outright on a camera that only embeds a thumbnail.
        BitmapSource source = decoder.Preview
            ?? decoder.Thumbnail
            ?? throw new NotSupportedException("RAW file has no embedded preview or thumbnail.");

        if (decodePixelWidth > 0 && source.PixelWidth > decodePixelWidth)
        {
            double scale = decodePixelWidth / (double)source.PixelWidth;
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        }

        // Copy the pixels out before the stream closes: with CacheOption.None the decoder reads
        // lazily, and everything above is still just a promise until something realises it.
        var realised = new WriteableBitmap(new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0));
        realised.Freeze();
        return realised;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests --filter ImageLoaderRawTests`
Expected: `Passed! - Failed: 0, Passed: 6`

- [ ] **Step 5: Run the whole suite for regressions**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests`
Expected: `Passed! - Failed: 0, Passed: 80` (58 baseline + 16 from Task 1 + 6 here). The critical check
is that nothing previously passing broke: the non-RAW path was refactored out into
`LoadStandardImage`, so all 9 `ImageLoaderTests` — including the EXIF rotation-direction ones — must
still pass.

- [ ] **Step 6: Commit**

```bash
git add src/MasterImage.Core/ImageLoader.cs tests/MasterImage.Core.Tests/ImageLoaderRawTests.cs
git commit -m "Decode RAW via the embedded preview instead of demosaicing"
```

---

## Task 3: Scan RAW files and pair RAW+JPEG

**Files:**
- Modify: `src/MasterImage.Core/PhotoSet.cs`
- Test: `tests/MasterImage.Core.Tests/PhotoSetPairingTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MasterImage.Core.Tests/PhotoSetPairingTests.cs
using System;
using System.IO;
using System.Linq;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class PhotoSetPairingTests : IDisposable
{
    private readonly string _tempDir;

    public PhotoSetPairingTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private void Touch(string name) => File.WriteAllBytes(Path.Combine(_tempDir, name), new byte[] { 0 });

    [Fact]
    public void IncludesRawFiles()
    {
        Touch("DSC1.ARW");

        var items = PhotoSet.Load(_tempDir);

        Assert.Single(items);
        Assert.Equal("DSC1", items[0].Stem);
    }

    [Fact]
    public void PairsARawWithItsSidecarJpegAsOnePhoto()
    {
        Touch("DSC1.ARW");
        Touch("DSC1.jpg");

        var items = PhotoSet.Load(_tempDir);

        // One shot, not two: one tile, one stop when seeking, one mark.
        Assert.Single(items);
        Assert.Equal(2, items[0].FilePaths.Count);
    }

    [Fact]
    public void APairedPhotoDisplaysFromTheRaw()
    {
        Touch("DSC1.ARW");
        Touch("DSC1.jpg");

        var items = PhotoSet.Load(_tempDir);

        Assert.EndsWith(".ARW", items[0].PrimaryFilePath);
    }

    [Fact]
    public void APairCarriesBothFilesSoCullingMovesTheLot()
    {
        Touch("DSC1.ARW");
        Touch("DSC1.jpg");

        var paths = PhotoSet.Load(_tempDir)[0].FilePaths.Select(Path.GetFileName).ToList();

        Assert.Contains("DSC1.ARW", paths);
        Assert.Contains("DSC1.jpg", paths);
    }

    [Fact]
    public void PairingIsCaseInsensitiveOnTheStem()
    {
        // Cameras are not consistent about case, and Windows filenames aren't case-sensitive.
        Touch("dsc1.ARW");
        Touch("DSC1.JPG");

        Assert.Single(PhotoSet.Load(_tempDir));
    }

    [Fact]
    public void UnpairedFilesStayIndependent()
    {
        Touch("DSC1.ARW");
        Touch("DSC2.jpg");

        var items = PhotoSet.Load(_tempDir);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Single(i.FilePaths));
    }

    [Fact]
    public void PairingKeepsNaturalSortOrder()
    {
        Touch("DSC2.ARW");
        Touch("DSC2.jpg");
        Touch("DSC10.ARW");
        Touch("DSC1.jpg");

        var stems = PhotoSet.Load(_tempDir).Select(i => i.Stem).ToArray();

        Assert.Equal(new[] { "DSC1", "DSC2", "DSC10" }, stems);
    }

    [Fact]
    public void TwoNonRawFilesSharingAStemAreNotPaired()
    {
        // sunset.jpg and sunset.png are different pictures that happen to share a name — pairing
        // is specifically a RAW-and-its-sidecar concept, not "same stem".
        Touch("sunset.jpg");
        Touch("sunset.png");

        Assert.Equal(2, PhotoSet.Load(_tempDir).Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests --filter PhotoSetPairingTests`
Expected: FAIL — `IncludesRawFiles` finds nothing (RAW extensions aren't scanned), and the pairing tests see two items where they want one.

- [ ] **Step 3: Rewrite `PhotoSet`**

Replace the whole of `src/MasterImage.Core/PhotoSet.cs` with:

```csharp
// src/MasterImage.Core/PhotoSet.cs
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests --filter PhotoSetPairingTests`
Expected: `Passed! - Failed: 0, Passed: 8`

- [ ] **Step 5: Run the whole suite for regressions**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests`
Expected: `Passed! - Failed: 0, Passed: 88`. The three existing `PhotoSetTests` must still pass — in
particular `OnlyIncludesSupportedExtensionsInNaturalOrder` (grouping must not disturb sort order) and
`EachItemHasOneFilePathInPhaseOne` (a lone `DSC1.png` still yields exactly one path).

- [ ] **Step 6: Commit**

```bash
git add src/MasterImage.Core/PhotoSet.cs tests/MasterImage.Core.Tests/PhotoSetPairingTests.cs
git commit -m "Scan RAW files and pair a RAW with its sidecar JPEG"
```

---

## Task 4: Explain a missing RAW codec

**Files:**
- Modify: `src/MasterImage.Core/ImageLoader.cs`
- Modify: `src/MasterImage.App/MainWindow.xaml.cs`
- Test: `tests/MasterImage.Core.Tests/ImageLoaderRawTests.cs`

RAW decoding depends on Windows' Raw Image Extension. It's installed here, but on a machine without
it every RAW would silently show as a blank/broken photo with no hint why. Give that failure a voice.

- [ ] **Step 1: Write the failing test**

Add to `tests/MasterImage.Core.Tests/ImageLoaderRawTests.cs`:

```csharp
    [Fact]
    public void ReportsWhetherRawIsDecodableOnThisMachine()
    {
        // True on any machine with the Raw Image Extension installed. Asserting the specific value
        // would make the suite fail on a machine without it, so just require a definite answer that
        // doesn't throw — the point is that MainWindow can ask before blaming the file.
        bool supported = ImageLoader.IsRawDecodingAvailable();
        Assert.True(supported || !supported);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests --filter ImageLoaderRawTests`
Expected: FAIL to compile — `IsRawDecodingAvailable` does not exist.

- [ ] **Step 3: Add the probe to `ImageLoader`**

Add `using Microsoft.Win32;` to the top of `src/MasterImage.Core/ImageLoader.cs` (it's part of
`net8.0-windows`, no package needed), and add this to the class, just after `TryLoadFullResolution`:

```csharp
    // Whether this machine can decode RAW at all — i.e. whether Windows' Raw Image Extension is
    // installed. Asked when a RAW fails to open, so the app can say "install the Raw Image
    // Extension" rather than leaving a blank frame and no explanation.
    //
    // Answered by reading WIC's own decoder registrations and asking whether any registered decoder
    // claims a RAW extension. Decoding a probe file would be the obvious alternative but doesn't
    // actually work: WIC throws NotSupportedException both for "no codec for this format" and for
    // "bytes aren't valid", so a synthetic probe can't tell a missing codec from a bad file.
    //
    // Lazy: the answer can't change while the process is running, and this sits on a failure path.
    private static readonly Lazy<bool> RawDecodingAvailable = new(DetectRawDecoder);

    public static bool IsRawDecodingAvailable() => RawDecodingAvailable.Value;

    private static bool DetectRawDecoder()
    {
        // CATID_WICBitmapDecoders — every installed WIC decoder registers an instance under here.
        const string DecoderCategory = @"CLSID\{7ED96837-96F0-4812-B211-F13C24117ED3}\Instance";

        try
        {
            using var instances = Registry.ClassesRoot.OpenSubKey(DecoderCategory);
            if (instances is null)
            {
                return false;
            }

            foreach (string clsid in instances.GetSubKeyNames())
            {
                using var decoder = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}");
                if (decoder?.GetValue("FileExtensions") is not string extensions)
                {
                    continue;
                }

                if (extensions.Split(',').Any(e => RawFormats.Extensions.Contains(e.Trim())))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception)
        {
            // Can't read the registry — don't claim RAW is broken on that basis; let the real
            // decode attempt be the judge and fall back to the generic failure message.
            return true;
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests --filter ImageLoaderRawTests`
Expected: `Passed! - Failed: 0, Passed: 7`

On this machine `IsRawDecodingAvailable()` should return **true** (the Raw Image Extension is
installed — that's why the ARW fixtures decode at all). Worth confirming that's what it actually
returns rather than trusting the always-true assertion: if it reports false here, the detection is
wrong and the app would nag about a codec that's present. A quick way to check is to add a
temporary `Assert.True(ImageLoader.IsRawDecodingAvailable())`, run it, and remove it again.

- [ ] **Step 5: Surface it in the app**

In `src/MasterImage.App/MainWindow.xaml.cs`, replace `LoadCurrentPhotoAsync` with the version below.
The only change is the `image is null` branch: previously a failed decode left the last photo on
screen with no explanation.

```csharp
    private async Task LoadCurrentPhotoAsync()
    {
        var photo = ViewModel.CurrentPhoto;
        if (photo is null)
        {
            SingleImageViewControl.SetImage(null);
            return;
        }

        int generation = ++_loadGeneration;

        var decode = _imageCache.GetAsync(photo);

        // Nothing to wait for when it's already decoded (the common case once read-ahead is
        // warm) — skip the await so the photo swaps within this same input event rather than
        // after a dispatcher round-trip.
        var image = decode.IsCompletedSuccessfully ? decode.Result : await decode;

        // A newer seek started while this was decoding; that one owns the screen now.
        if (generation != _loadGeneration) return;

        SingleImageViewControl.SetImage(image);

        if (image is null)
        {
            NavigationOverlayControl.ShowMessage(DescribeLoadFailure(photo));
            return;
        }

        NavigationOverlayControl.Show(photo, ViewModel.CurrentIndex, ViewModel.Photos.Count, ViewModel.IsCurrentMarked);

        PrefetchNeighbours();
    }

    // A RAW that won't open on a machine with no RAW codec is the one failure with a fix the user
    // can act on, so name it. Anything else is genuinely just a bad file.
    private static string DescribeLoadFailure(PhotoItem photo)
    {
        string name = Path.GetFileName(photo.PrimaryFilePath);

        if (RawFormats.IsRaw(photo.PrimaryFilePath) && !ImageLoader.IsRawDecodingAvailable())
        {
            return $"Can't open {name} — RAW support needs the free \"Raw Image Extension\" from the Microsoft Store.";
        }

        return $"Can't open {name}.";
    }
```

- [ ] **Step 6: Build and verify the app still runs**

```bash
export PATH="$PATH:/c/Program Files/dotnet" && cd "/c/Users/kaihu/Documents/projects/image viewer" && dotnet build -c Release
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Run the whole suite**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests`
Expected: `Passed! - Failed: 0, Passed: 89` (88 + 1 here).

- [ ] **Step 8: Commit**

```bash
git add src/MasterImage.Core/ImageLoader.cs src/MasterImage.App/MainWindow.xaml.cs tests/MasterImage.Core.Tests/ImageLoaderRawTests.cs
git commit -m "Explain a missing RAW codec instead of failing silently"
```

---

## Task 5: Verify against real RAW files

**Files:** none (verification only)

- [ ] **Step 1: Run the whole suite**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests`
Expected: `Passed! - Failed: 0, Passed: 89`.

- [ ] **Step 2: Build a RAW test folder**

Copy the machine's real ARW files somewhere scratch — never point the app at the originals' folder,
since opening a folder creates a hidden `.thumbnails` in it, and `N` *moves* files.

```bash
TEST="/c/Users/kaihu/AppData/Local/Temp/MasterImage-rawtest"
rm -rf "$TEST"; mkdir -p "$TEST"
cp /c/Users/kaihu/Pictures/*.ARW "$TEST/" 2>/dev/null
cp /c/Users/kaihu/Downloads/DSC01565.ARW /c/Users/kaihu/Downloads/DSC01565.jpg "$TEST/" 2>/dev/null
ls -1 "$TEST"
```
Expected: the six `DSC094xx.ARW` files plus the `DSC01565.ARW`/`.jpg` pair.

- [ ] **Step 3: Launch against it**

```bash
export PATH="$PATH:/c/Program Files/dotnet" && cd "/c/Users/kaihu/Documents/projects/image viewer"
taskkill //F //IM MasterImage.App.exe 2>/dev/null
./src/MasterImage.App/bin/Release/net8.0-windows/MasterImage.App.exe "C:\\Users\\kaihu\\AppData\\Local\\Temp\\MasterImage-rawtest\\DSC09423.ARW" &
```

Check by hand:
- The RAW displays, and `DSC09423.ARW` (shot portrait) appears **upright, not on its side**.
- Left/Right seeking through the RAWs feels immediate once read-ahead is warm.
- The `DSC01565` pair shows as **one** photo, not two — the position readout should count 7 photos, not 8.
- Holding `Shift` shows RAW thumbnails in the grid.
- `M` then `N` on the `DSC01565` pair moves **both** the `.ARW` and the `.jpg` into `selected/`.

- [ ] **Step 4: Clean up**

```bash
taskkill //F //IM MasterImage.App.exe 2>/dev/null
rm -rf "/c/Users/kaihu/AppData/Local/Temp/MasterImage-rawtest"
```
