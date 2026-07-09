# Master Image — Design Spec

Date: 2026-07-08
Status: Approved for planning

## 1. Purpose

A fast, keyboard-driven Windows photo viewer built for a photographer's culling workflow: open a shoot, seek through it instantly (including RAW), zoom out to a tile overview to compare shots, mark keepers, and move them into a `selected` folder — all without leaving the keyboard. Speed is the top priority throughout; every design choice below is made in service of that.

Not a general-purpose editor: no crop/adjust/export. It replaces "double-click a photo and look at it," including as the Windows default handler for image files.

## 2. Tech Stack

- **.NET 8 + WPF**, single Windows desktop app. Requires installing the .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`) in the dev environment; ships self-contained so end users don't need .NET installed separately.
- **WIC** (`System.Windows.Media.Imaging.BitmapDecoder`/`BitmapImage`) for JPEG/PNG/GIF/BMP/WebP/TIFF decode, using `DecodePixelWidth`/`DecodePixelHeight` to decode directly at the resolution needed (thumbnail or fit-to-window) rather than full-resolution + resize.
- **LibRaw** (via a thin P/Invoke wrapper) for RAW files (CR2, NEF, ARW, DNG, and other LibRaw-supported formats) — used *only* to extract the embedded JPEG preview each camera writes into the RAW file. No full demosaic decode path in v1 (see §5).
- **nvJPEG** (NVIDIA CUDA) as an optional accelerator for the bulk thumbnail pipeline, auto-detected with automatic CPU fallback (see §6).
- WPF's own compositor (Direct3D-backed) already GPU-accelerates on-screen rendering/panning/zooming — no extra work needed for that part.

Rejected alternatives: Python+PySide6 (packaging overhead, per-call latency risk for keyboard-driven seeking), Electron (weaker OS shell integration for default-app registration, slower large-grid rendering).

## 3. Supported Formats

- Standard: JPEG, PNG, GIF (static), BMP, WebP, TIFF.
- RAW: CR2, NEF, ARW, DNG, and other formats LibRaw supports, via embedded-preview extraction.

## 4. Folder & Photo Model

- Opening any image opens its **containing folder, non-recursively** (subfolders are not scanned or included in seeking/grid).
- Files are grouped into `PhotoItem`s by filename stem: a RAW+JPEG pair from the same shot (e.g. `DSC001.NEF` + `DSC001.JPG`) is treated as a single entry everywhere — one tile in the grid, one stop in left/right seeking, one mark. Display prefers the RAW's embedded preview when both exist.
- The `.thumbnails` folder and any `selected` folder in the directory are excluded from the photo list.
- **Sort order:** filenames sorted in natural/alphanumeric order (e.g. `DSC2.jpg` before `DSC10.jpg`, not lexicographic), ascending. This matches capture sequence for the common single-camera-per-folder case. Not EXIF-capture-time-based in v1 — a folder merged from multiple cards/cameras with overlapping filename sequences won't necessarily be in shot order.

## 5. Image Display Pipeline

- Opening a photo shows the fastest available source, in order of preference: existing cached thumbnail (if large enough for the current view) → RAW embedded preview (via LibRaw) or WIC reduced-resolution decode → never a full-resolution decode-then-downscale.
- Mouse scroll (no modifier), single-image view: zoom in/out centered on the cursor. Click-drag pans when zoomed in.
- **RAW zoom fidelity (explicit decision):** there is no full demosaic decode path. Zooming past the embedded preview's native resolution digitally upscales that preview. This keeps every interaction instant with no exceptions; the trade-off is that critical focus/sharpness can't be 100% verified at extreme zoom from within the app. Accepted trade-off per product owner — full demosaic decode is out of scope for v1.

## 6. Thumbnail & Preload Pipeline

- **No automatic background work.** Opening a folder does not proactively generate thumbnails or previews. This is a deliberate choice for predictable resource usage — the app does nothing until you ask it to.
- **On-demand generation:** when the tile grid (Shift) is opened, thumbnails are generated for on-screen tiles only, as they scroll into view, on a background thread pool (sized to `Environment.ProcessorCount`), and written to the on-disk cache (§7). Off-screen tiles remain ungenerated until scrolled to.
- **`L` — force full preload:** generates and caches thumbnails (and extracts RAW embedded previews) for every photo in the current folder immediately, using all available worker threads (and the GPU path if available — see below), instead of waiting for tiles to scroll into view. Useful before a long browsing/culling session on a large folder.
- **GPU acceleration ("superspeed" mode):** at startup, the app detects whether an NVIDIA CUDA-capable GPU is present. If so, the thumbnail pipeline (both on-demand and `L`-triggered) routes JPEG decode/resize work through nvJPEG for batch GPU decoding — this is where the bulk win comes from, since RAW embedded previews and most source photos are JPEG. If no compatible GPU is found, the same pipeline runs on CPU (WIC-based decode/resize) with no functional difference — GPU is purely an accelerator, never a hard dependency.
- Regenerating a thumbnail at a larger size (e.g. tile size increased past what's cached) replaces the cached file; the existing cached thumbnail is shown upscaled momentarily to avoid a blank tile while the higher-res version regenerates.

## 7. Tile Grid (Overview Mode)

- **Trigger:** holding `Shift` shows the grid; releasing it returns to single-image view, jumping to whichever tile is under the cursor (or staying on the current photo if the cursor isn't over a tile). This is a press-and-hold interaction, not a toggle.
- **Tile size:** while the grid is open, holding `Shift` and scrolling adjusts the tile size (effectively the grid column count) live.
- **Rendering:** a virtualized panel recycles a fixed pool of Image elements rather than instantiating one per photo, so scrolling stays smooth into the thousands of tiles.
- Mark state (§8) is visually indicated on tiles (e.g. a badge) same as in single-image view.

## 8. Marking & Culling Workflow

- **`M` — mark/unmark:** toggles the current photo (and its RAW+JPEG pair, treated as one unit per §4) as "selected." Visually indicated in both single-image view and the grid.
- **Persistence:** marks are saved to `.thumbnails/marks.json` per folder, so they survive closing and reopening the app — a culling pass can span multiple sittings.
- **`N` — move marked photos:** creates a `selected/` subfolder in the current directory (if it doesn't already exist) and moves every currently-marked photo — both files of a pair — into it. This is an immediate, non-recursive, current-folder-only operation.

## 9. Window & Navigation

- **Default window:** borderless, distraction-free — the photo fills the window, no title bar/menu/toolbar. A small overlay (filename, position like "12/340", mark indicator) appears on navigation/marking and fades out.
- **`F` — fullscreen toggle:** switches between the default windowed-borderless state and true fullscreen (fills the monitor, hides the taskbar).
- **`Left` / `Right`:** seek to the previous/next photo in the folder's (non-recursive) list.
- **`I` — shortcuts overlay:** toggles a help panel listing every shortcut in this spec.
- **`Esc`:** closes the shortcuts overlay if open; else exits fullscreen if in fullscreen; otherwise no-op. Esc never closes the app (avoids accidental exits mid-session) — Alt+F4 (standard Windows, works regardless of window chrome) is the close mechanism.
- **Single-instance:** opening a photo (from Explorer/"Open with") while Master Image is already running reuses the existing window and navigates to that photo/folder, rather than spawning a second process. Avoids a cold-start cost on every double-click and matches how most image viewers behave.

## 10. Full Keyboard/Mouse Reference

| Input | Action |
|---|---|
| `Left` / `Right` | Seek previous/next photo |
| Scroll (single view) | Zoom in/out centered on cursor |
| Click + drag (zoomed) | Pan |
| `Shift` (hold) | Show tile grid; release jumps to hovered tile |
| `Shift` + scroll (grid open) | Adjust tile size |
| `L` | Force full-folder thumbnail/preview preload |
| `M` | Mark/unmark current photo (pair-aware) |
| `N` | Create `selected/` and move marked photos into it |
| `F` | Toggle fullscreen / windowed |
| `I` | Toggle shortcuts overlay |
| `Esc` | Close overlay, or exit fullscreen, or no-op |

## 11. On-Disk Cache Layout

Inside every viewed folder:
```
.thumbnails/
  manifest.json   -- filename -> { sourceMtime, sourceSize, thumbPath } for staleness detection
  marks.json      -- list of marked filename stems
  <hash>.jpg      -- cached thumbnail/preview per photo
```
- `.thumbnails` is marked hidden so it doesn't clutter Explorer.
- Staleness: a cached thumbnail is regenerated if the source file's mtime/size no longer matches the manifest entry.
- Orphan pruning: entries whose source file no longer exists are dropped lazily the next time `manifest.json` is loaded for that folder.

## 12. Installation & Default App Registration

- App name: **Master Image**.
- Installer (WiX or Inno Setup — decided during implementation) installs to `%LocalAppData%\Programs\MasterImage` (no admin rights required).
- Registers the app and all supported extensions (§3) under `HKCU\Software\Classes` and `HKCU\Software\RegisteredApplications`.
- Windows requires explicit user confirmation to change default file-type handlers (a security measure since Windows 8 — no app can silently take over defaults). The installer/first-run flow registers Master Image as a valid handler and then opens Windows' native "Choose default apps" confirmation, pre-filled, so the user only has to click confirm.

## 13. Explicitly Out of Scope (v1)

- Recursive/subfolder browsing.
- Any editing: rotate, crop, delete, adjustments.
- Full RAW demosaic decode (see §5 trade-off).
- Automatic/background preloading without `L` or opening the grid.
- Non-Windows platforms.
