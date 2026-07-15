# Master Image — RAW Support (Plan 2) Design Spec

Date: 2026-07-15
Status: Approved for planning
Supersedes: §2/§3/§5 of `2026-07-08-master-image-design.md` where they specify LibRaw

## 1. Purpose

Open camera RAW files (Sony ARW, Nikon NEF, Canon CR2/CR3, DNG, …) fast enough to cull with —
seeking through a RAW shoot must feel the same as seeking through JPEGs.

## 2. What changed from the original design, and why

The original spec called for **LibRaw via P/Invoke** to extract each RAW's embedded JPEG preview,
on the reasoning that a full demosaic decode (0.5–2s) would make seeking unusable. Investigation
against the real files on this machine showed LibRaw is unnecessary:

- **Windows already decodes RAW.** Microsoft's *Raw Image Extension*
  (`Microsoft.RawImageExtension`) is installed and registers a WIC codec — so the existing
  WIC-based `ImageLoader` already opens `.ARW` today, with no new code.
- **The full decode is indeed far too slow**, confirming the original concern: 3703ms for a 125MB
  61MP ARW, 1232ms for a 17MB ARW.
- **WIC exposes the embedded preview directly**, via `BitmapDecoder.Preview` — which is exactly
  what LibRaw was wanted for. Measured end-to-end (open → preview → scale to 1920 → freeze):

  | File | Full decode | Embedded preview | Speedup |
  |---|---|---|---|
  | DSC09423.ARW (125MB, 61MP) | 3703 ms | **285 ms** | 12.9× |
  | DSC01565.ARW (17MB) | 1232 ms | **93 ms** | 13.2× |

  For reference, a 39MB camera JPEG full-decodes in ~1068ms — so RAW-via-preview is *faster than
  the JPEGs we already handle*, because we read the small embedded preview instead of parsing the
  whole file.

Dropping LibRaw removes a native dependency, a P/Invoke layer, and its packaging burden, and gains
**broader format coverage** than the original spec listed (see §4).

## 3. Design

### 3.1 `ImageLoader` — a RAW branch

`TryLoad` dispatches on file extension:

- **Non-RAW** (unchanged): `BitmapImage` + `DecodePixelWidth`.
- **RAW**: open the file (`FileStream`, `FileShare.Read`, `using`), then
  `BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None)`.
  `CacheOption.None` is essential — `OnLoad` forces a full decode up front and costs 1.5–3.4s,
  destroying the entire benefit. Take `decoder.Preview ?? decoder.Thumbnail`, scale down to the
  requested width with a `TransformedBitmap`/`ScaleTransform` when it exceeds it, apply EXIF
  orientation, freeze.

**EXIF orientation must be applied to the preview.** Measured: the full-decode path returns
1279×1920 (portrait) for DSC09423.ARW while the raw preview path returns 1920×1280 (landscape) —
the embedded preview carries the sensor's landscape pixels plus an orientation tag. Without
applying it, portrait RAW shots display sideways. This reuses the existing `ApplyOrientation`
helper; `ReadExifOrientation` already reads orientation via `BitmapFrame` metadata and works for
RAW containers.

Both the existing return-null-on-failure contract and the deterministic file-handle release
(`using` on the stream) are preserved.

### 3.2 `PhotoSet` — RAW extensions and RAW+JPEG pairing

- Add the RAW extensions (§4) to the supported set.
- **Pair by filename stem.** A `DSC01565.ARW` + `DSC01565.jpg` pair becomes a single `PhotoItem`
  holding both paths: one tile, one stop when seeking, one mark, and `N` moves both files together.
  `PhotoItem` already models this (`FilePaths` is a list) — Plan 1 simply never populated more than
  one.
- **Display prefers the RAW** when a pair exists, i.e. the RAW is `PrimaryFilePath` (order
  `FilePaths` RAW-first). This matches the original spec and is justified by the measurements: the
  RAW's embedded preview (93ms) is not a slow path.
- Sorting is unaffected: the natural sort already orders by filename, and a pair contributes one
  entry.

### 3.3 Missing-codec handling

RAW decoding depends on the *Raw Image Extension* being installed. It is present on the target
machine but is not guaranteed on a fresh Windows install, and its absence would otherwise surface
as an unexplained broken-image placeholder.

`ImageLoader` exposes a way to distinguish "this is a RAW file we couldn't decode" from "not an
image", and `MainWindow` shows a plain-English overlay message on that case — naming the Raw Image
Extension and that it's free from the Microsoft Store — rather than failing silently.

### 3.4 What is deliberately unchanged

Everything downstream of `ImageLoader` needs no modification, because it all decodes through
`ImageLoader`: `PhotoImageCache` + read-ahead (so RAW seeking is instant once warm), `ThumbnailCache`
/ `ThumbnailPipeline` (grid thumbnails at 512px — measured 57–224ms per RAW), marking, and culling.

## 4. Supported RAW formats

Taken from the installed decoder's own registration rather than assumed:

`.3FR .ARI .ARW .BAY .CAP .CR2 .CR3 .CRW .DCS .DCR .DRF .EIP .ERF .FFF .IIQ .K25 .KDC .MEF .MOS
.MRW .NEF .NRW .ORF .ORI .PEF .PTX .PXN .RAF .RAW .RW2 .RWL .SR2 .SRF .SRW .X3F .DNG`

Covers Nikon (NEF/NRW), Canon (CR2/CR3/CRW), Sony (ARW/SR2/SRF), Fuji (RAF), Olympus (ORF/ORI),
Panasonic (RW2), Pentax (PEF/PTX), Adobe (DNG), Phase One (IIQ/CAP), Hasselblad (3FR/FFF), Sigma
(X3F), and others — broader than the original spec's LibRaw list.

## 5. Testing

- **`ImageLoader` RAW path** against the real `.ARW` fixtures on this machine: returns a non-null
  image, at the requested width, correctly oriented (portrait shot must come back portrait).
- **Missing/undecodable RAW** returns null without throwing, consistent with the existing contract.
- **`PhotoSet` pairing**: a stem with both `.ARW` and `.jpg` yields one `PhotoItem` with two
  `FilePaths`, RAW first; RAW-only and JPEG-only stems each yield one single-path item; pairing does
  not disturb natural sort order.
- **Cull moves both halves of a pair** — already covered by an existing `CullOperations` test, which
  passes a two-path `PhotoItem`; the new coverage is that `PhotoSet` actually produces one.
- Tests needing a real RAW are skipped when the fixture is absent, so the suite stays green on a
  machine without them.

## 6. Out of scope

- Full RAW demosaic decode (the 13× cost is the whole reason for this design).
- Bundling LibRaw as a fallback for machines lacking the Raw Image Extension.
- Editing, white-balance, or any RAW development controls.
- XMP/sidecar files.
