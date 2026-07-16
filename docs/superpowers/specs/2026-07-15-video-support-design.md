# Master Image — Video Support Design Spec

Date: 2026-07-15
Status: Approved for planning (decisions taken autonomously — see §8)
Extends: `2026-07-08-master-image-design.md` (§5 viewing)

## 1. Purpose

Play the clips that sit alongside stills in a shoot. A video autoplays, loops, and can be muted —
individually per pane in compare mode. Everything else about the app (seeking, marking, culling,
the grid, the peek) treats a clip exactly as it treats a photo.

## 2. What makes this bigger than it looks

The app has one media type today, and it shows: everything downstream of `ImageLoader` works because
it is all `BitmapSource`. A video is not one. It needs a `MediaElement` — a different element with a
playback lifecycle, an audio state, and a file handle it holds open while playing.

Three consequences drive the design:

- **`PhotoImageCache` must not touch videos.** `Prefetch` warms 20 items ahead; handed 4GB clips it
  would feed each to WIC, burn every decode slot, and stall real photo read-ahead behind work that
  was always going to return null.
- **`ThumbnailCache` needs one branch, not a rewrite.** It caches a JPEG keyed by source filename
  and prunes by filename, so the grid, the `P` peek, `L` and orphan pruning all work for video the
  moment something can produce a first frame. Only generation is media-specific.
- **A playing video locks its file.** `N` would fail to move the clip currently on screen, and would
  do it quietly — `CullOperations` reports failures rather than throwing, so it'd read as "1 failed"
  with no hint why. Media must be released before the move.

## 3. Design

### 3.1 Rendering: WPF `MediaElement`

No new dependency. `MediaElement` plays through Media Foundation, i.e. whatever codecs Windows has —
which is the same bet the app already makes for RAW (WIC + the Raw Image Extension) rather than
bundling LibRaw. It brings the same failure mode and therefore the same fix: a codec the machine
lacks (HEVC being the likely one) gets named in plain English rather than leaving a black frame,
reusing the pattern in `MainWindow.DescribeLoadFailure`.

Rejected: LibVLCSharp / FFME. Both bundle an FFmpeg-sized native payload into a self-contained
release that already ships ~70MB, to solve a codec problem the target machine doesn't have.

### 3.2 `SingleImageView` shows either a photo or a video

The `MediaElement` goes **inside `SingleImageView`**, alongside the existing `<Image>`, with exactly
one visible at a time. This is the load-bearing decision: compare mode is two `SingleImageView`s, so
putting video there means compare gets video, per-pane zoom, and per-pane audio for free rather than
growing a parallel `CompareVideoView`.

The control keeps its single `MatrixTransform`, now applied to whichever element is showing — so
wheel-zoom and drag-pan work on video with no new code, and the zoom-lock mirroring in compare works
on video for the same reason.

`LoadedBehavior="Manual"` so playback is ours to drive; looping is `MediaEnded` → `Position = 0` →
`Play()`.

### 3.3 Audio

- **Single-image view**: a video plays with sound. `A` toggles mute, and **the choice sticks for the
  rest of the session** — so muting is one keystroke once, not once per clip. Autoplay with sound is
  what "autoplay looped with option to mute" asks for; stickiness is what stops that being hostile
  when you seek through twenty clips.
- **Compare mode**: both panes start **muted**, and `A` unmutes the active pane. Two clips talking
  over each other is never what anyone wants, so the default is silence and you choose a side. This
  is the "individually" requirement: mute is per-pane state, and `A` acts on whichever pane `K` has
  made active.

The session-wide preference and the per-pane flags are deliberately separate — a mute you set while
comparing shouldn't silently change what happens when you go back to single view.

### 3.4 Formats

`.mp4 .mov .m4v .avi .wmv .mkv .webm .mts .m2ts .3gp`

Chosen for what cameras actually write: MP4 and MOV cover most bodies and phones, MTS/M2TS is
AVCHD, AVI and WMV are legacy. Listing an extension is not a promise the machine can decode it —
that's Media Foundation's call, and §3.1 covers being honest when it can't.

### 3.5 Poster frames

`ThumbnailCache` branches on media type: photos decode through `ImageLoader` as today; videos get
their first frame from the **Windows Shell** (`IShellItemImageFactory`), which is the same image
Explorer shows and is already generated and cached by Windows.

Requested with `SIIGBF_THUMBNAILONLY` so a clip with no available thumbnail returns nothing rather
than a generic filetype icon — an icon in the grid would look like a real frame and lie about the
content.

Downstream of this, video is invisible: the frame is cached as a JPEG under `.thumbnails` exactly
like a photo's, so the grid, the peek, `L` and pruning need no changes at all.

### 3.6 Read-ahead

`PhotoImageCache.GetAsync`/`Prefetch` return null immediately for videos rather than queuing a
decode (§2). Videos are opened by `MediaElement` directly from disk when displayed; there is nothing
to warm.

### 3.7 Culling

`MainWindow` releases the media (clears the `MediaElement`'s source in the single view and both
compare panes) before `MoveMarkedToSelected` runs, then reloads. Without it, the clip on screen holds
its own file open and its move fails as a sharing violation — reported as a bare "1 failed" with no
explanation.

## 4. What is deliberately unchanged

- **Marks, `N`, the grid, `P`, compare navigation, the Escape gesture** — all operate on
  `PhotoItem`s and file paths, and are indifferent to what's inside the file.
- **`PhotoSet` pairing** stays RAW-specific. `DSC1.MP4` next to `DSC1.JPG` is two separate items,
  which is right: they're a clip and a still, not two renderings of one shutter press.

## 5. Testing

- **`VideoFormats.IsVideo`** recognises the §3.4 extensions, is case-insensitive, and rejects
  photos and RAW.
- **`PhotoSet`** lists videos as items and does not pair them with same-stem stills.
- **`PhotoImageCache`** returns null for a video without queuing a decode — asserted by observing
  that the cache holds no entry for it, since "didn't waste a decode slot" is the actual requirement.
- **Poster frames**: a real generated clip yields a non-null frame at the requested width; skipped
  when Media Foundation can't produce one on the test machine, consistent with the RAW tests.
- **Driving the window**: a video autoplays and loops; `A` mutes and the choice survives seeking to
  another clip; in compare both panes start muted and `A` acts only on the active one; `N` moves a
  clip that is currently playing.

## 6. Out of scope

- Scrubbing, pause, frame stepping, in/out points, playback speed.
- Audio waveform or level display.
- Video in the RAW+JPEG pairing model.
- Transcoding, or bundling codecs the machine lacks.
- Remembering per-file mute state across restarts.

## 7. Decisions taken without review

Implemented under an instruction to work autonomously. Each is reversible and worth a second look:

- **Sound on by default in single view, sticky mute** (§3.3) — reading "autoplay looped with option
  to mute" as sound-on-by-default. Muted-by-default is the other defensible reading.
- **Both panes muted on entering compare** (§3.3) — chosen over "the active pane is audible", which
  would move the audio every time `K` is pressed.
- **`A` for audio** — `M` is taken by marking. `A` is unbound and mnemonic.
- **Shell poster frames** (§3.5) over decoding a frame ourselves, for the same reason RAW uses WIC.
