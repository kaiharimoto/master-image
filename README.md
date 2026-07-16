# Master Image

A fast, keyboard-driven photo viewer and culling tool for Windows. Built for going through a shoot
quickly: seek instantly, compare in a tile grid, mark the keepers, and move them out in one keystroke.

Opens JPEG, PNG, GIF, BMP, WebP, TIFF, and camera RAW — Sony ARW, Nikon NEF, Canon CR2/CR3, Adobe
DNG, Fuji RAF, Olympus ORF, Panasonic RW2 and ~30 more.

## Install

Download `Setup.exe` from the [latest release](https://github.com/KAIHARI/master-image/releases) and
run it. It installs per-user, so no admin rights are needed.

**Windows will show a "Windows protected your PC" warning.** The app isn't code-signed (certificates
cost a few hundred a year). Click **More info → Run anyway**.

To make it your default viewer: after installing, open **Settings → Apps → Default apps → Master
Image** and pick the file types you want. Windows requires you to confirm this yourself — no app has
been allowed to seize defaults silently since Windows 8.

## Shortcuts

| Key | Action |
|---|---|
| `←` / `→` | Previous / next photo |
| `↑` / `↓` | The same as `←` / `→` |
| Scroll | Zoom in/out at the cursor |
| Drag | Pan (when zoomed) / move the window (when not) |
| Double-click | Maximise / restore |
| `F` | Fullscreen |
| `Shift` | Toggle the tile grid |
| Arrows (in grid) | Browse tiles |
| `Enter` / click | Open the selected tile |
| `1` / `2` / `3` | Smaller / bigger / default tile size |
| `J` | Compare two photos side by side |
| `K` | Switch which pane the arrows drive |
| `H` | Zoom both panes together / separately |
| `M` | Mark / unmark |
| `N` | Move marked photos into `selected/` |
| `L` | Preload the folder for instant seeking |
| `I` | Show all shortcuts |
| `U` | Check for updates |
| `Esc` | Exit fullscreen / compare |
| `Esc` ×3, or `Alt+F4` | Quit |

## Comparing

`J` splits the view in two, with the photo you were on in both panes. The arrows drive whichever pane
is active (it has a blue border); `K` switches which one that is. So you pin a reference on one side
and flip through candidates on the other.

`H` toggles zoom lock. Locked (the default), zooming or paning either pane does the same to the
other — which is the point when you're checking two frames of the same shot for focus. Panes keep
their zoom as you seek, so you can zoom to 100% once and then browse.

## Why it's fast

Seeking is the most-used action in a cull, and decoding a 40MB camera JPEG takes about a second — so
the app never decodes on demand if it can help it. It reads ahead around wherever you are, and keeps
a large in-memory cache sized from your machine's RAM (on a well-specced box, an entire shoot). The
result is that seeking is instant in both directions, including back to photos you've already seen.

RAW files open via the camera's embedded preview rather than a full demosaic — measured ~13x faster
(285ms vs 3703ms on a 125MB 61MP ARW) and identical for culling purposes, since it's the same
rendering the camera would show you.

## RAW support

RAW decoding uses Windows' **Raw Image Extension**, a free Microsoft Store download. Without it, RAW
files won't open and the app says so rather than showing a blank frame. Install it from the Microsoft
Store if you shoot RAW.

## How it stores things

- Thumbnails are cached in a hidden `.thumbnails` folder next to your photos.
- Marks live in `.thumbnails/marks.json`, so they survive closing the app — a cull can span days.
- `N` **moves** marked photos into a `selected/` subfolder. Nothing is ever deleted.
- A photo shot RAW+JPEG (`DSC1.ARW` + `DSC1.jpg`) counts as one photo: one tile, one mark, and `N`
  moves both files together.

## Building from source

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet build -c Release
dotnet test tests/MasterImage.Core.Tests
.\scripts\release.ps1 -Version 1.0.0    # produces .\Releases\Setup.exe
```

## License

MIT
