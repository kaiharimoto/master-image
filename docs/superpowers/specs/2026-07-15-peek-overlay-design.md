# Master Image — Peek Overlay (P) Design Spec

Date: 2026-07-15
Status: Approved for planning
Extends: `2026-07-08-master-image-design.md` (§5 viewing, §6 shortcuts)

## 1. Purpose

`P` toggles a glance at the photos either side of the one you're viewing: the previous in the lower
left, the next in the lower right. It answers "what's coming?" without leaving the photo you're on,
which during a cull is the question you ask before every arrow press.

This is deliberately scoped small and split out from video support, which is a separate project: a
second media type running through code that currently assumes `BitmapSource` throughout. The peek
needs none of that — it asks the thumbnail cache for a picture and draws it.

## 2. Design

### 2.1 Scope

`P` toggles the peek in **single-image view only**. It is ignored while:

- **Compare mode is up.** It's already showing two photos side by side; two more thumbnails would
  crowd the comparison the split exists to make.
- **The grid is open.** The grid is nothing but neighbours — a peek there is redundant.

The toggle survives seeking, culling and mode changes; it is not persisted across restarts (§5).

### 2.2 Layout

Two thumbnails, pinned to the bottom corners with the same 16px margin `NavigationOverlay` uses,
~150px wide at the photo's natural aspect ratio.

**`IsHitTestVisible="False"`**, matching `NavigationOverlay`. Not just because the peek is
informational: at fit-to-window a left-drag anywhere on the photo means "move the window" (there's
no title bar), so a hit-testable overlay sitting over the photo would silently eat that gesture in
two corners.

**The filename chip keeps the lower-left corner and draws over the previous thumbnail.** The chip
fades out 1.2s after a seek (`NavigationOverlay.FadeOutAfter`), so it covers the thumbnail only
while you're moving and clears the moment you stop to look — which is when the peek is worth
reading. Relocating the chip while the peek is on was considered and rejected: it moves a fixed
piece of UI for a transient overlap that resolves itself.

### 2.3 Content

- Previous = `CurrentIndex - 1`, next = `CurrentIndex + 1`, **wrapping exactly as `SeekPrevious`
  and `SeekNext` wrap** (`MainViewModel.cs:89-99`).

  The wrap is not a detail. Seeking wraps at both ends of the folder, so a peek that didn't wrap
  would show an empty corner on the first and last photo while the arrow key cheerfully took you
  round to the other end. The peek must never disagree with what the arrow will actually do.

- Fewer than two photos: nothing is shown. With one photo, previous and next are both that same
  photo, and showing a picture three times says nothing.

### 2.4 Source

The existing `ThumbnailPipeline` (512px, disk-cached in `.thumbnails`). No new decode path, no new
cache, and a folder that's been through `L` paints instantly. 512px comfortably exceeds the ~150px
displayed size, so the peek downscales rather than upscales.

### 2.5 Structure

A `PeekOverlay` user control, hosted in `MainWindow.xaml` alongside `NavigationOverlay` — the same
overlay-host pattern the window already uses. It exposes one method taking the two neighbouring
photos (or nothing), and `MainWindow` refreshes it where `LoadCurrentPhotoAsync` already ends.

**Reuses the existing `_loadGeneration` guard.** Thumbnails resolve asynchronously, so without it a
slow thumbnail from a photo you've already seeked past could land in the corner after you've moved
on — exactly the bug that guard exists to prevent for the main image.

The neighbour index arithmetic lives in a small testable seam rather than in the control, so the
wrapping rules in §2.3 can be tested without a window.

## 3. Interaction with existing behaviour

- **Culling (`N`)** reloads and can shrink the photo list; the peek refreshes from the reloaded list
  along with the main image, and falls back to showing nothing if the folder drops below two photos.
- **Entering compare or the grid** hides the peek without clearing the toggle, so leaving them
  restores it.
- **The shortcuts overlay** gains a `P` line under Viewing.

## 4. Testing

- **Neighbour maths**: wraps at both ends; a folder of exactly two photos yields distinct
  neighbours; fewer than two yields nothing; the neighbours always match where `SeekPrevious` and
  `SeekNext` would actually land.
- **Driving the window**: `P` shows both thumbnails in single-image view; they track seeking; they
  don't appear in compare mode or the grid; the toggle survives a round trip through both.

## 5. Out of scope

- Clicking a thumbnail to seek to it (it would take the window-drag gesture in two corners, and the
  arrows already do this).
- Showing more than one photo either side.
- Persisting the toggle across restarts.
- Any video-specific behaviour. Once videos are supported, a video neighbour is just whatever the
  thumbnail cache returns for it — the peek needs no knowledge of the media type.
