# Master Image — Installer, File Associations & Updates (Plan 4) Design Spec

Date: 2026-07-15
Status: Approved for planning
Supersedes: §12 of `2026-07-08-master-image-design.md` (which assumed WiX/Inno Setup and no updater)

## 1. Purpose

Turn the working app into something that installs like real software, sets itself up as the default
image viewer, and can be handed to strangers on GitHub — including telling them when there's a newer
version.

Three jobs:
1. **Install** — a `Setup.exe` anyone can download and run, no prerequisites.
2. **Associate** — become the default handler for image and RAW files.
3. **Update** — pull new versions from GitHub Releases.

## 2. Approach: Velopack

Uses **Velopack** (`Velopack` NuGet + the `vpk` CLI) for install and update. It's the maintained
successor to Squirrel by the same author, purpose-built for .NET desktop apps distributing via GitHub
Releases. One tool covers both jobs rather than bolting an installer (WiX/Inno) to a separate updater
(AutoUpdater.NET/NetSparkle), which is what the original spec implied.

### 2.1 Installed layout, and why it matters

Velopack installs per-user under `%LocalAppData%` (no admin rights needed):

```
%LocalAppData%\MasterImage\
  MasterImage.exe     <- stable execution stub; never changes
  Update.exe          <- the updater
  current\            <- the app itself; REPLACED WHOLESALE on every update
```

Two consequences drive the rest of this design:

- **File associations must point at the stable stub**, never into `current\`. The stub survives
  updates; the versioned content behind it doesn't. Pointing associations at `current\` would break
  every file association on the first update.
- **Nothing persistent may live in `current\`** — it's erased on update. We're already fine: the
  thumbnail cache and marks live beside the photos, not in the install directory.

### 2.2 Packaging

- **Self-contained** (`--self-contained -r win-x64`): bundles the .NET runtime so the app runs on a
  clean Windows 10/11 with nothing pre-installed. ~80–150MB download. The alternative saves ~100MB
  but adds a "go install the .NET 8 Desktop Runtime first" step that would lose most people.
- **Unsigned.** Users will see a SmartScreen "Windows protected your PC" prompt on first install and
  must click *More info → Run anyway*. Normal for an unsigned open-source project; the README says so
  up front so it doesn't read as malware. Signing can be added later (a `vpk` flag) without touching
  the architecture.

### 2.3 WPF entry point

Velopack must run before WPF starts, so the app needs an explicit `Main`:

```csharp
[STAThread]
private static void Main(string[] args)
{
    VelopackApp.Build()
        .OnAfterInstallFastCallback(_ => FileAssociations.Register())
        .OnBeforeUninstallFastCallback(_ => FileAssociations.Unregister())
        .Run();

    var app = new App();
    app.InitializeComponent();
    app.Run();
}
```

This requires the csproj to stop auto-generating an entry point:
`<ApplicationDefinition Remove="App.xaml"/>`, `<Page Include="App.xaml"/>`,
`<StartupObject>MasterImage.App.App</StartupObject>`.

**Risk to verify:** the existing single-instance logic reads the file path from
`StartupEventArgs.Args` in `OnStartup`. `app.Run()` still raises `OnStartup`, and `Args` lazily reads
`Environment.GetCommandLineArgs()`, so it should be unaffected — but this is the mechanism by which
"double-click a photo in Explorer" works, so the plan must actually test it rather than assume.
If it does break, the fix is to read `Environment.GetCommandLineArgs()` directly.

## 3. File associations

A new `FileAssociations` class in the App project writes to `HKCU\Software\Classes` (per-user, no
admin):

- A ProgID (`MasterImage.Photo`) with a friendly name, the app's icon, and a
  `shell\open\command` of `"<stable stub>" "%1"`.
- Every supported extension (§3.1) gets `HKCU\Software\Classes\<.ext>\OpenWithProgids\MasterImage.Photo`.
  This *offers* Master Image for those types without seizing them.
- `HKCU\Software\RegisteredApplications` + a capabilities key, so the app appears in Windows'
  *Default apps* UI.
- `SHChangeNotify(SHCNE_ASSOCCHANGED)` so Explorer notices immediately rather than after a reboot.

Registered on install, removed on uninstall, via Velopack's callbacks (30s budget — registry writes
are instant).

**Windows will not let any app silently seize defaults.** Since Windows 8 the final "make this the
default" step must be a human clicking in Settings; there is no supported API. So after install the
app opens *Settings → Default apps → Master Image*, where the user confirms. This is the same dance
every third-party browser and viewer does, and matches what was agreed in the original spec.

### 3.1 Extensions registered

Standard: `.jpg .jpeg .png .gif .bmp .webp .tif .tiff`
RAW: the 36 formats in `RawFormats.Extensions` (`.arw .nef .cr2 .cr3 .dng …`).

RAW associations are registered regardless of whether the Raw Image Extension is installed — the app
already explains that gap at load time rather than failing mutely.

## 4. Updates

Manual, on demand: **`U` checks for updates**, listed in the shortcuts overlay (`I`) so it's
discoverable. No automatic check — a cull session shouldn't be interrupted, and the app shouldn't
change under you unasked. The trade-off is accepted: you have to press `U` to find out.

```csharp
var mgr = new UpdateManager(new GithubSource("https://github.com/KAIHARI/master-image", null, false));
var update = await mgr.CheckForUpdatesAsync();     // null => already current
await mgr.DownloadUpdatesAsync(update);
mgr.ApplyUpdatesAndRestart(update);                // relaunches into the new version
```

Feedback goes through the existing navigation overlay: checking → "You're up to date" / "Version X
available — press U again to install" → downloading → restart. Two presses, so a stray `U` mid-cull
can't restart the app from under you.

**Running from a dev build** (i.e. not Velopack-installed), `UpdateManager.IsInstalled` is false and
any update call throws. `U` detects this and says "not installed — updates only work in an installed
copy" instead of crashing. This is the common case during development, so it must be handled, not
left to an exception.

Network failure, no GitHub release yet, or a private repo all surface as a plain overlay message
rather than an unhandled exception.

## 5. Release workflow

A `scripts/release.ps1` that:
1. `dotnet publish src/MasterImage.App -c Release -r win-x64 --self-contained -o publish/`
2. `vpk pack --packId MasterImage --packVersion <version> --packDir publish --mainExe MasterImage.App.exe`
3. Leaves `Releases/` containing `Setup.exe` + the release feed files.

Publishing is then `gh release create v<version> Releases/*` (the GitHub CLI is already authenticated
as KAIHARI). Kept as a script rather than a GitHub Action: releases are occasional and manual, and CI
would add a moving part with no current payoff.

Version lives in `MasterImage.App.csproj` (`<Version>`), read by `vpk` and shown by the app.

A `README.md` covers what it is, the shortcuts, the SmartScreen prompt, the Raw Image Extension
dependency, and how to set it as default.

## 6. Development is unaffected

Installing does not freeze the source tree. The installed copy under `%LocalAppData%` is separate;
the repo stays fully editable. `scripts/release.ps1` rebuilds and re-packages whenever wanted. The
only friction is that a running exe is locked, so the script closes the app first.

## 7. Out of scope

- Code signing (needs a purchased certificate).
- GitHub Actions / CI publishing.
- Delta updates (Velopack supports them; not worth configuring until releases are frequent).
- MSIX / Microsoft Store packaging.
- Non-Windows platforms.
