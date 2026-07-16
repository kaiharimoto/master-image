# Master Image — Compare Mode & Ergonomics Design Spec

Date: 2026-07-15
Status: Approved for planning
Extends: `2026-07-08-master-image-design.md` (§5 viewing, §6 shortcuts)

## 1. Purpose

Four independent changes to the viewer shell:

1. **Up/Down navigate** like Left/Right, which they currently don't.
2. **Compare mode** — put two photos side by side, browse either, and zoom them together or apart.
3. **Escape three times quits**, and Escape stops being a general-purpose back button.
4. **An application icon** — two triangles forming an M, white on black.

Compare mode is the substantial one; the rest are small. They ship together because three of the
four touch the same keyboard switch in `MainWindow_PreviewKeyDown`, and splitting them would mean
three passes over the same method.

## 2. Up/Down navigation

`Up` seeks previous (identical to `Left`), `Down` seeks next (identical to `Right`). Up-as-previous
matches reading order and the direction a vertical list scrolls.

Implemented by adding the keys to the existing `Key.Left` / `Key.Right` cases in
`MainWindow_PreviewKeyDown`. The grid continues to own all four arrows while it is open — the
early-return that defers to the grid (`MainWindow.xaml.cs:246`) already names `Up` and `Down`, so
the `ListBox`'s own across-wrapped-rows handling is preserved untouched.

## 3. Compare mode

### 3.1 Keys and behaviour

| Key | Action |
|---|---|
| `J` | Toggle compare mode on/off |
| `K` | Switch the active pane |
| `H` | Toggle zoom lock |
| Arrows | Seek the active pane |

`J` opens a 50/50 vertical split. Both panes start on the photo that was on screen; the **right pane
is active**, marked with a subtle border. Arrows browse the active pane through the folder,
wrapping, exactly as they do in single-image view. `K` moves the active marker to the other pane so
you can browse that one instead. `J` again exits compare mode, landing on whichever photo the active
pane was showing — so browsing in compare and then leaving deposits you where you were looking,
rather than snapping back to where you started.

### 3.2 Zoom lock

**On by default.** The dominant use for a side-by-side is judging two frames of the same shot for
focus or expression, and mirrored movement is the point of that comparison. Independent zoom is the
exception, so it's the toggle rather than the default.

When locked, any wheel-zoom or pan in either pane applies the same transform to both. When unlocked,
each pane zooms and pans independently.

This works because both panes are the same size and both use `Stretch="Uniform"`: the transform is
expressed in the pane's own coordinate space, so copying it mirrors *relative* framing correctly
even when the two images have different pixel dimensions or aspect ratios. Mirroring absolute image
coordinates would be wrong here and is not what's wanted.

### 3.3 Structure

A new `CompareView` user control holds two `SingleImageView` instances in a two-column `Grid`. It
slots into the host-swapping pattern `MainWindow` already uses for the grid (`SingleImageHost` /
`GridHost` toggled by `Visibility`, `MainWindow.xaml:26-31`), so compare mode is a third host rather
than a special case inside the single-image view.

Reusing `SingleImageView` for each pane inherits its wheel-zoom, drag-pan, cursor-anchored scaling,
and min/max scale clamps with no new code. Two additions to it:

- A settable transform property and a `TransformChanged` event, so `CompareView` can mirror one pane
  onto the other. A reentrancy guard prevents the mirrored assignment from echoing back.
- A way to suppress `WindowDragRequested`. In single-image view a drag on an unzoomed photo moves the
  window (there is no title bar); inside a pane that gesture should do nothing, since dragging the
  window from one half of a split is surprising and makes the panes feel like separate windows.

Navigation state lives in a `CompareState` class alongside `MainViewModel` — left index, right index,
and which pane is active, with `SeekNext`/`SeekPrevious` acting on the active one. Keeping it out of
the control makes it unit-testable in the same style as the existing `MainViewModelTests`.

Both panes decode through the existing `PhotoImageCache`, so compare mode inherits read-ahead and
costs nothing extra on an already-warm folder.

### 3.4 Interaction with existing modes

- **`M` marks the active pane's photo.** This mirrors the precedent set by the grid, where `M`
  already targets the browsed tile rather than whatever is behind it (`MainWindow.xaml.cs:396-413`) —
  the rule is "mark what you're looking at."
- **`Shift` still opens the grid** over compare mode; closing it returns to compare with both panes
  and the active marker intact.
- **`N` (cull move)** behaves as it does today, operating on all marked photos regardless of mode.
  After the move, both pane indices are clamped into the reloaded photo list.

## 4. Escape

### 4.1 Escape stops being a back button

Every mode already has a dedicated toggle: `Shift` for the grid, `I` for the shortcuts panel, `F`
for fullscreen, `J` for compare. Escape's cascade (`MainWindow.xaml.cs:258-272`) duplicated those
toggles, so it is removed for the grid and the shortcuts panel.

Escape retains only the two "big mode" exits, where exiting on Escape is a strong enough OS-wide
convention to be worth the redundancy:

- Exit fullscreen.
- Exit compare mode.

### 4.2 Three presses quit

**Every** Escape counts toward the quit, including one that exited fullscreen or compare. The count
resets when 1.5s elapses since the last press, or when any other key is pressed.

On the first press the overlay shows `Press Esc twice more to quit`, and on the second
`Press Esc once more to quit`. Without this the gesture would be undiscoverable — nothing else in
the app hints that Escape has a cumulative effect. The message uses the existing
`NavigationOverlay.ShowMessage`, so it self-dismisses on the same timer as every other transient
message.

**Known consequence, accepted:** three rapid Escapes starting from fullscreen will quit — the press
that exits fullscreen still counts as one. This follows directly from "every Escape counts". The
1.5s window and the on-screen countdown are what keep it from firing by accident.

## 5. Application icon

Two white triangles on black, forming an M — a large near peak and a smaller peak to its right,
overlapping, separated by a thin black seam where they cross. The asymmetry reads as depth, which
suits a photo tool, and the silhouette still reads as an M.

- Authored as an SVG master committed to the repo, so it can be regenerated at any size.
- Exported to a multi-resolution `.ico` (16, 32, 48, 64, 128, 256 px). The 16px rendering is the
  binding constraint: it must stay legible in the taskbar and in Explorer's small-icon views.
- Wired in three places, all of which are separate and all of which are user-visible:
  - `<ApplicationIcon>` in `MasterImage.App.csproj` — the exe's own icon in Explorer.
  - `Icon` on `MainWindow` — the icon in the Alt+Tab switcher.
  - The `vpk pack --icon` argument in `scripts/release.ps1` — installed shortcuts and the
    Add/Remove Programs entry. Without this the installed app would show a default icon even though
    the exe has one.
- File associations pick the icon up from the exe automatically via the existing `DefaultIcon`
  registration in `FileAssociations`, so no change is needed there.

## 6. Shortcuts overlay

`ShortcutsOverlay.xaml` is the app's only in-product documentation and is currently accurate; every
change above invalidates part of it. It gains a `Compare` section (`J`/`K`/`H`), Up/Down under
Viewing, and a corrected Escape line. The `Alt+F4 — close Master Image` line gains the Esc×3
alternative.

## 7. Testing

- **`CompareState`**: `J` seeds both panes from the current index; arrows move only the active pane
  and wrap at both ends; `K` flips the active pane and arrows then move the other one; exiting
  returns the active pane's index. Empty and single-photo folders don't throw or wrap incorrectly.
- **Escape counting**: three presses within the window quit; a press after the window has elapsed
  restarts the count at one; an intervening non-Escape key resets it; a press that exits fullscreen
  still counts. Tested against an extracted counter class rather than by driving the window, so the
  logic is testable without a message loop.
- **Zoom mirroring**: with lock on, a transform applied to one pane appears on the other and the
  mirroring terminates (no reentrant loop); with lock off, the other pane is unchanged.
- **Up/Down**: seek in the same direction as Left/Right — covered by the existing `MainViewModel`
  seek tests, since the keys route into the same `SeekNext`/`SeekPrevious`.
- The icon is verified by eye at 16px, not by test.

## 8. Out of scope

- More than two compare panes.
- Comparing across folders; both panes browse the current photo set.
- A difference/onion-skin blend between the panes.
- Syncing zoom by absolute image coordinates rather than pane-relative framing (§3.2).
- Persisting compare mode, the active pane, or the zoom-lock setting across restarts.
- Remapping Escape's quit gesture, or making the 1.5s window configurable.
