# Installer, File Associations & Updates (Plan 4 of 4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship Master Image as a real installable app — a `Setup.exe` anyone can download from GitHub, that registers itself as an image/RAW handler, and can pull new versions from GitHub Releases on demand.

**Architecture:** Velopack provides both the installer and the updater, with GitHub Releases as the feed. It installs per-user to `%LocalAppData%\MasterImage\` with a **stable stub exe** at the root and the app itself in a `current\` folder that is replaced wholesale on update — so file associations point at the stub, never into `current\`. Associations are plain `HKCU\Software\Classes` registry writes, made from Velopack's install/uninstall hooks. Updates are manual (`U`), because a cull session shouldn't be interrupted.

**Tech Stack:** .NET 8, WPF, `Velopack` NuGet + `vpk` dotnet tool. Self-contained win-x64 publish. Unsigned.

**Environment notes:**
- The .NET SDK needs its PATH prepended in *every* shell call (it does not persist): Bash `export PATH="$PATH:/c/Program Files/dotnet" && dotnet ...`
- Run everything from `C:\Users\kaihu\Documents\projects\image viewer`.
- A build can fail with "file is locked by MasterImage.App" — run `taskkill //F //IM MasterImage.App.exe 2>/dev/null` first.
- Baseline: **91 tests passing, 0 build warnings**. Keep both.
- GitHub repo: `https://github.com/KAIHARI/master-image`. `gh` CLI is authenticated as KAIHARI. The repo does **not exist yet** and there is no git remote — Task 6 handles that.

---

## Task 1: Version the app and add Velopack

**Files:**
- Modify: `src/MasterImage.App/MasterImage.App.csproj`

- [ ] **Step 1: Add the Velopack package**

```bash
export PATH="$PATH:/c/Program Files/dotnet" && cd "/c/Users/kaihu/Documents/projects/image viewer"
dotnet add src/MasterImage.App/MasterImage.App.csproj package Velopack
```

- [ ] **Step 2: Add a version**

`vpk` needs a version, and the app will show it. Add to the existing `<PropertyGroup>` in `src/MasterImage.App/MasterImage.App.csproj`:

```xml
    <Version>1.0.0</Version>
    <AssemblyTitle>Master Image</AssemblyTitle>
    <Product>Master Image</Product>
```

- [ ] **Step 3: Build**

```bash
export PATH="$PATH:/c/Program Files/dotnet" && taskkill //F //IM MasterImage.App.exe 2>/dev/null
dotnet build -c Release
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. If Velopack introduces warnings, report them.

- [ ] **Step 4: Commit**

```bash
git add src/MasterImage.App/MasterImage.App.csproj
git commit -m "Add Velopack and version the app"
```

---

## Task 2: File association registration

**Files:**
- Create: `src/MasterImage.App/FileAssociations.cs`
- Test: `tests/MasterImage.Core.Tests/FileAssociationsTests.cs`

This writes real registry keys under `HKCU\Software\Classes`. That's per-user and needs no admin, but
it is genuinely global state — so the tests use a **redirectable root key** rather than scribbling on
the real hive.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MasterImage.Core.Tests/FileAssociationsTests.cs
using System;
using System.Linq;
using MasterImage.App;
using Microsoft.Win32;
using Xunit;

namespace MasterImage.Core.Tests;

public class FileAssociationsTests : IDisposable
{
    // A scratch subtree of HKCU that mimics Software\Classes, so tests never touch the real
    // association hive — registering the app for .jpg on the developer's machine as a side effect
    // of running the suite would be unacceptable.
    private readonly string _root = @"Software\MasterImageTests\" + Guid.NewGuid().ToString("N");

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

    [Fact]
    public void RegisterCreatesAProgIdPointingAtTheGivenExe()
    {
        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _root);

        using var command = Registry.CurrentUser.OpenSubKey($@"{_root}\MasterImage.Photo\shell\open\command");
        Assert.NotNull(command);
        string? value = command!.GetValue(null) as string;

        // The "%1" is what passes the double-clicked file to the app; without it the app opens
        // with no photo.
        Assert.Equal("\"C:\\Fake\\MasterImage.exe\" \"%1\"", value);
    }

    [Fact]
    public void RegisterOffersTheAppForEveryStandardAndRawExtension()
    {
        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _root);

        foreach (string ext in new[] { ".jpg", ".png", ".tiff", ".arw", ".nef", ".cr3", ".dng" })
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{_root}\{ext}\OpenWithProgids");
            Assert.NotNull(key);
            Assert.Contains("MasterImage.Photo", key!.GetValueNames());
        }
    }

    [Fact]
    public void RegisterCoversEveryExtensionTheAppCanActuallyOpen()
    {
        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _root);

        // Anything the viewer can open should be offerable, or the association list quietly drifts
        // out of step with what the app supports.
        foreach (string ext in FileAssociations.SupportedExtensions)
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{_root}\{ext}\OpenWithProgids");
            Assert.NotNull(key);
        }
    }

    [Fact]
    public void SupportedExtensionsIncludesBothStandardImagesAndRaw()
    {
        Assert.Contains(".jpg", FileAssociations.SupportedExtensions);
        Assert.Contains(".arw", FileAssociations.SupportedExtensions);
        Assert.Contains(".nef", FileAssociations.SupportedExtensions);
        Assert.True(FileAssociations.SupportedExtensions.Count > 40,
            $"expected 8 standard + 36 raw, got {FileAssociations.SupportedExtensions.Count}");
    }

    [Fact]
    public void UnregisterRemovesWhatRegisterAdded()
    {
        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _root);
        FileAssociations.Unregister(_root);

        Assert.Null(Registry.CurrentUser.OpenSubKey($@"{_root}\MasterImage.Photo"));

        using var jpg = Registry.CurrentUser.OpenSubKey($@"{_root}\.jpg\OpenWithProgids");
        // The .jpg key itself may survive (other apps register there too) — what must go is our
        // entry within it. Leaving a dangling ProgId would make Windows offer a deleted app.
        if (jpg is not null)
        {
            Assert.DoesNotContain("MasterImage.Photo", jpg.GetValueNames());
        }
    }

    [Fact]
    public void RegisterIsIdempotent()
    {
        // Reinstalling over an existing install must not throw or duplicate anything.
        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _root);
        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _root);

        using var key = Registry.CurrentUser.OpenSubKey($@"{_root}\.jpg\OpenWithProgids");
        Assert.Single(key!.GetValueNames().Where(n => n == "MasterImage.Photo"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests --filter FileAssociationsTests`
Expected: FAIL to compile — `FileAssociations` does not exist.

- [ ] **Step 3: Implement `FileAssociations`**

```csharp
// src/MasterImage.App/FileAssociations.cs
using System.IO;
using System.Runtime.InteropServices;
using MasterImage.Core;
using Microsoft.Win32;

namespace MasterImage.App;

// Registers Master Image as an available handler for image and RAW files.
//
// Everything here writes under HKCU\Software\Classes — per-user, so no admin rights, and it can't
// break other accounts. Note this only *offers* the app for these types; Windows has not allowed an
// app to silently seize a default since Windows 8, so the last step is always the user confirming in
// Settings > Default apps. There is no supported API around that, by design.
public static class FileAssociations
{
    public const string ProgId = "MasterImage.Photo";
    private const string AppRegistrationName = "MasterImage";

    // The real hive. Tests pass their own scratch root so the suite never touches live associations.
    private const string ClassesRoot = @"Software\Classes";

    private static readonly string[] StandardExtensions =
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff"
    };

    public static IReadOnlyList<string> SupportedExtensions { get; } =
        StandardExtensions.Concat(RawFormats.Extensions).Distinct().ToList();

    public static void Register(string exePath, string classesRoot = ClassesRoot)
    {
        using (var progId = Registry.CurrentUser.CreateSubKey($@"{classesRoot}\{ProgId}"))
        {
            progId.SetValue(null, "Photo");
            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue(null, $"\"{exePath}\",0");
            using var command = progId.CreateSubKey(@"shell\open\command");
            // "%1" hands the double-clicked file to the app. Quoted, or paths with spaces break.
            command.SetValue(null, $"\"{exePath}\" \"%1\"");
        }

        foreach (string extension in SupportedExtensions)
        {
            using var progIds = Registry.CurrentUser.CreateSubKey($@"{classesRoot}\{extension}\OpenWithProgids");
            // An empty REG_SZ is the documented shape here: the value NAME is the ProgId, and the
            // data is ignored. Setting it repeatedly is a no-op, which is what makes reinstall safe.
            progIds.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        RegisterInDefaultAppsUi(exePath, classesRoot);
        NotifyShell();
    }

    // Puts the app in Settings > Default apps, which is where the user has to confirm the default.
    private static void RegisterInDefaultAppsUi(string exePath, string classesRoot)
    {
        string capabilities = $@"{classesRoot}\{AppRegistrationName}\Capabilities";

        using (var caps = Registry.CurrentUser.CreateSubKey(capabilities))
        {
            caps.SetValue("ApplicationName", "Master Image");
            caps.SetValue("ApplicationDescription", "Fast photo viewer and culling tool");

            using var assoc = caps.CreateSubKey("FileAssociations");
            foreach (string extension in SupportedExtensions)
            {
                assoc.SetValue(extension, ProgId);
            }
        }

        using var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications");
        registered.SetValue(AppRegistrationName, $@"{classesRoot}\{AppRegistrationName}\Capabilities");
    }

    public static void Unregister(string classesRoot = ClassesRoot)
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"{classesRoot}\{ProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"{classesRoot}\{AppRegistrationName}", throwOnMissingSubKey: false);

        foreach (string extension in SupportedExtensions)
        {
            using var progIds = Registry.CurrentUser.OpenSubKey($@"{classesRoot}\{extension}\OpenWithProgids", writable: true);
            // Delete only our value — the key is shared with every other app that handles this type.
            progIds?.DeleteValue(ProgId, throwOnMissingValue: false);
        }

        using (var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
        {
            registered?.DeleteValue(AppRegistrationName, throwOnMissingValue: false);
        }

        NotifyShell();
    }

    // Without this Explorer keeps showing the old associations until it restarts.
    private static void NotifyShell() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests --filter FileAssociationsTests`
Expected: `Passed! - Failed: 0, Passed: 6`

- [ ] **Step 5: Confirm the real hive was not touched**

The tests pass a scratch root, but that's exactly the sort of thing worth checking rather than trusting:

```bash
powershell -NoProfile -Command "if (Test-Path 'HKCU:\Software\Classes\MasterImage.Photo') { 'LEAKED into real hive' } else { 'real hive clean' }"
```
Expected: `real hive clean`. If it leaked, the tests wrote to the live registry and the `classesRoot`
parameter isn't being honoured somewhere — report it.

- [ ] **Step 6: Run the whole suite**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests`
Expected: `Passed! - Failed: 0, Passed: 97` (91 + 6).

- [ ] **Step 7: Commit**

```bash
git add src/MasterImage.App/FileAssociations.cs tests/MasterImage.Core.Tests/FileAssociationsTests.cs
git commit -m "Register image and RAW file associations under HKCU"
```

---

## Task 3: Velopack entry point

**Files:**
- Modify: `src/MasterImage.App/MasterImage.App.csproj`
- Modify: `src/MasterImage.App/App.xaml.cs`

Velopack has to run before WPF starts, which means taking over the entry point. **This is the riskiest
change in the plan**: it's the machinery by which double-clicking a photo in Explorer passes the file
path to the app, and that's the whole point of being the default viewer.

- [ ] **Step 1: Stop WPF generating an entry point**

In `src/MasterImage.App/MasterImage.App.csproj`, add to the existing `<PropertyGroup>`:

```xml
    <StartupObject>MasterImage.App.App</StartupObject>
```

and add this new `<ItemGroup>`:

```xml
  <ItemGroup>
    <ApplicationDefinition Remove="App.xaml" />
    <Page Include="App.xaml" />
  </ItemGroup>
```

- [ ] **Step 2: Add the `Main` method**

In `src/MasterImage.App/App.xaml.cs`, add `using Velopack;` at the top, and add this method inside the
`App` class (put it directly above `OnStartup`):

```csharp
    // Velopack must run before WPF starts: on an update or uninstall it needs to do its work and
    // exit without ever showing a window. Taking over Main is how it gets that chance.
    //
    // The install/uninstall hooks are where file associations are (de)registered. They're given the
    // STABLE stub exe, not this assembly's own path — Velopack replaces the whole current\ folder on
    // every update, so an association pointing at the running exe would break the first time the app
    // updated itself.
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => FileAssociations.Register(StubExePath()))
            .OnAfterUpdateFastCallback(_ => FileAssociations.Register(StubExePath()))
            .OnBeforeUninstallFastCallback(_ => FileAssociations.Unregister())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    // %LocalAppData%\MasterImage\MasterImage.exe — the launcher Velopack keeps at the root of the
    // install, which forwards to whatever is currently in current\. Stable across updates, which is
    // exactly what a file association needs. Falls back to this exe when running a dev build, where
    // there is no stub.
    private static string StubExePath()
    {
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MasterImage.App.exe");
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);

        if (currentDir.Name.Equals("current", StringComparison.OrdinalIgnoreCase) && currentDir.Parent is not null)
        {
            string stub = Path.Combine(currentDir.Parent.FullName, "MasterImage.exe");
            if (File.Exists(stub))
            {
                return stub;
            }
        }

        return exe;
    }
```

`OnAfterUpdateFastCallback` re-registers deliberately: it's cheap, idempotent, and it repairs the
associations if a previous version registered them wrongly.

**These three callback names come from Velopack's documented `VelopackApp` API, but verify them
against the actual package rather than trusting this plan** — if any doesn't exist, the build will
say so. Run `vpk --help` / inspect `VelopackApp` and report the real names rather than guessing at a
substitute. The same applies to `UpdateManager.IsInstalled` in Task 4.

- [ ] **Step 3: Build**

```bash
export PATH="$PATH:/c/Program Files/dotnet" && taskkill //F //IM MasterImage.App.exe 2>/dev/null
dotnet build -c Release
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

A likely failure here is a duplicate-entry-point error (`CS0017`) if the `ApplicationDefinition
Remove` didn't take effect. If so, report the actual error rather than guessing.

- [ ] **Step 4: VERIFY THE COMMAND-LINE PATH STILL WORKS — this is the critical check**

The single-instance code reads the file to open from `StartupEventArgs.Args` in `OnStartup`. With a
custom `Main`, WPF no longer generates the entry point, so this must be proven, not assumed — if it
broke, every double-click from Explorer would open the app on the wrong folder.

```bash
export PATH="$PATH:/c/Program Files/dotnet" && cd "/c/Users/kaihu/Documents/projects/image viewer"
taskkill //F //IM MasterImage.App.exe 2>/dev/null
TEST="/c/Users/kaihu/AppData/Local/Temp/MasterImage-argtest"
rm -rf "$TEST"; mkdir -p "$TEST"
cp /c/Users/kaihu/Pictures/DSC02581.JPG "$TEST/" 2>/dev/null
cp /c/Users/kaihu/Pictures/DSC02599.JPG "$TEST/" 2>/dev/null
timeout 12 ./src/MasterImage.App/bin/Release/net8.0-windows/MasterImage.App.exe "C:\\Users\\kaihu\\AppData\\Local\\Temp\\MasterImage-argtest\\DSC02599.JPG" > /tmp/argtest.log 2>&1
echo "EXIT: $?"; cat /tmp/argtest.log
```
Expected: exit 124 (still running = didn't crash) and an empty log.

Exit 124 only proves it launched. To prove the **argument** arrived, check that the app opened on the
photo that was passed rather than the first in the folder: the passed file is `DSC02599.JPG`, which
sorts last. Add a temporary line at the end of `MainWindow`'s constructor:
`System.IO.File.AppendAllText(@"C:\Users\kaihu\AppData\Local\Temp\mi-arg.txt", $"{ViewModel.CurrentPhoto?.Stem}\n");`
rebuild, re-run the command above, then `cat /c/Users/kaihu/AppData/Local/Temp/mi-arg.txt`.
Expected: `DSC02599` (the file passed), **not** `DSC02581` (the folder's first photo).
Remove the temporary line and rebuild afterwards. **Report what it printed.**

- [ ] **Step 5: Clean up and run the suite**

```bash
taskkill //F //IM MasterImage.App.exe 2>/dev/null
rm -rf "/c/Users/kaihu/AppData/Local/Temp/MasterImage-argtest" /c/Users/kaihu/AppData/Local/Temp/mi-arg.txt
export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests
```
Expected: `Passed! - Failed: 0, Passed: 97`.

- [ ] **Step 6: Commit**

```bash
git add src/MasterImage.App/MasterImage.App.csproj src/MasterImage.App/App.xaml.cs
git commit -m "Take over the entry point so Velopack can run before WPF"
```

---

## Task 4: Check for updates with U

**Files:**
- Create: `src/MasterImage.App/AppUpdater.cs`
- Modify: `src/MasterImage.App/MainWindow.xaml.cs`
- Modify: `src/MasterImage.App/Views/ShortcutsOverlay.xaml`

- [ ] **Step 1: Create `AppUpdater`**

```csharp
// src/MasterImage.App/AppUpdater.cs
using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace MasterImage.App;

// Checks GitHub Releases for a newer build, on demand only.
//
// Deliberately manual (the U key): a cull session shouldn't be interrupted by a download, and the app
// shouldn't restart itself out from under someone mid-edit. The cost is that you have to ask.
public sealed class AppUpdater
{
    private const string RepositoryUrl = "https://github.com/KAIHARI/master-image";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _pending;

    // False when running a dev build rather than an installed copy — which is the normal case during
    // development, and where every UpdateManager call throws. Callers must check this first.
    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    public sealed record CheckResult(bool UpdateAvailable, string Message);

    public async Task<CheckResult> CheckAsync()
    {
        if (!IsInstalled)
        {
            return new CheckResult(false, "Updates only work in an installed copy — this is a dev build.");
        }

        try
        {
            _pending = await _manager.CheckForUpdatesAsync().ConfigureAwait(true);

            return _pending is null
                ? new CheckResult(false, $"You're up to date (v{CurrentVersion}).")
                : new CheckResult(true, $"v{_pending.TargetFullRelease.Version} available — press U again to install.");
        }
        catch (Exception ex)
        {
            // No release published yet, no network, private repo... none of it should take the app
            // down mid-session; the user just wants to know it didn't work.
            return new CheckResult(false, $"Couldn't check for updates: {ex.Message}");
        }
    }

    // Downloads the update found by CheckAsync and restarts into it. The app exits inside
    // ApplyUpdatesAndRestart, so nothing after it runs.
    public async Task<string?> DownloadAndApplyAsync()
    {
        if (_pending is null)
        {
            return "Nothing to install — check for updates first.";
        }

        try
        {
            await _manager.DownloadUpdatesAsync(_pending).ConfigureAwait(true);
            _manager.ApplyUpdatesAndRestart(_pending);
            return null;
        }
        catch (Exception ex)
        {
            return $"Update failed: {ex.Message}";
        }
    }
}
```

- [ ] **Step 2: Wire `U` into `MainWindow`**

In `src/MasterImage.App/MainWindow.xaml.cs`, add this field next to the other fields (near `_imageCache`):

```csharp
    private readonly AppUpdater _updater = new();
```

Add this case to the `MainWindow_PreviewKeyDown` switch:

```csharp
            case Key.U:
                _ = HandleUpdateCheckAsync();
                e.Handled = true;
                break;
```

And add this method alongside `HandlePreloadAllAsync`:

```csharp
    // First U checks; a second U (once something's pending) installs and restarts. Two presses on
    // purpose: a stray U mid-cull must not be able to restart the app.
    private async Task HandleUpdateCheckAsync()
    {
        var result = await _updater.CheckAsync();

        if (!result.UpdateAvailable)
        {
            NavigationOverlayControl.ShowMessage(result.Message);
            return;
        }

        if (!_updateOffered)
        {
            _updateOffered = true;
            NavigationOverlayControl.ShowMessage(result.Message);
            return;
        }

        NavigationOverlayControl.ShowSticky("Downloading update…");
        string? error = await _updater.DownloadAndApplyAsync();

        // Only reached if the update failed — a success restarts the app.
        if (error is not null)
        {
            _updateOffered = false;
            NavigationOverlayControl.ShowMessage(error);
        }
    }
```

and this field next to `_updater`:

```csharp
    private bool _updateOffered;
```

- [ ] **Step 3: Add `U` to the shortcuts overlay**

In `src/MasterImage.App/Views/ShortcutsOverlay.xaml`, add this line directly after the
`I — toggle this panel` TextBlock:

```xml
            <TextBlock Foreground="White" Text="U — check for updates" Margin="0,2" />
```

- [ ] **Step 4: Build and verify the dev-build path**

```bash
export PATH="$PATH:/c/Program Files/dotnet" && taskkill //F //IM MasterImage.App.exe 2>/dev/null
dotnet build -c Release
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

Then launch a dev build and press nothing — the point of this check is simply that constructing
`AppUpdater` against a non-installed build doesn't throw at startup:

```bash
timeout 10 ./src/MasterImage.App/bin/Release/net8.0-windows/MasterImage.App.exe > /tmp/upd.log 2>&1
echo "EXIT: $?"; cat /tmp/upd.log
```
Expected: exit 124, empty log. (`U` itself can only be exercised by hand; it should report "this is a
dev build".)

- [ ] **Step 5: Run the suite**

Run: `export PATH="$PATH:/c/Program Files/dotnet" && dotnet test tests/MasterImage.Core.Tests`
Expected: `Passed! - Failed: 0, Passed: 97`.

- [ ] **Step 6: Commit**

```bash
git add src/MasterImage.App/AppUpdater.cs src/MasterImage.App/MainWindow.xaml.cs src/MasterImage.App/Views/ShortcutsOverlay.xaml
git commit -m "Check for updates from GitHub Releases with U"
```

---

## Task 5: Release script

**Files:**
- Create: `scripts/release.ps1`
- Create: `README.md`

- [ ] **Step 1: Install the vpk tool**

```bash
export PATH="$PATH:/c/Program Files/dotnet" && dotnet tool install -g vpk
```
If it's already installed, `dotnet tool update -g vpk`. Confirm with `vpk --help` (you may need
`export PATH="$PATH:/c/Users/kaihu/.dotnet/tools"`).

- [ ] **Step 2: Create `scripts/release.ps1`**

**Check the flags against `vpk pack --help` before running this** — the flag names below
(`--packId`, `--packVersion`, `--packDir`, `--mainExe`, `--packTitle`) are from Velopack's docs, but
CLI flags drift between versions. If one is rejected, use the real name from `--help` and report the
difference rather than dropping the flag.

```powershell
# scripts/release.ps1 — build a distributable Setup.exe.
#
# Publishes self-contained (bundles the .NET runtime) so the download runs on a clean Windows with
# nothing pre-installed. ~150MB, which is the price of not making every user install a runtime first.
#
# Usage:  .\scripts\release.ps1 -Version 1.0.1
param(
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root

try {
    # A running copy locks its own exe and would fail the publish.
    Get-Process MasterImage.App -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    $publish = Join-Path $root 'publish'
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

    Write-Host "Publishing $Version..." -ForegroundColor Cyan
    dotnet publish src/MasterImage.App/MasterImage.App.csproj `
        -c Release -r win-x64 --self-contained true `
        -p:Version=$Version -o $publish
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }

    Write-Host "Packaging $Version..." -ForegroundColor Cyan
    vpk pack `
        --packId MasterImage `
        --packVersion $Version `
        --packDir $publish `
        --mainExe MasterImage.App.exe `
        --packTitle "Master Image"
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

    Write-Host ""
    Write-Host "Done. Artifacts in .\Releases:" -ForegroundColor Green
    Get-ChildItem (Join-Path $root 'Releases') | ForEach-Object { "  $($_.Name)  ($([math]::Round($_.Length/1MB,1)) MB)" }
    Write-Host ""
    Write-Host "Publish to GitHub with:" -ForegroundColor Yellow
    Write-Host "  gh release create v$Version .\Releases\* --title `"v$Version`" --notes `"...`""
}
finally {
    Pop-Location
}
```

- [ ] **Step 3: Create `README.md`**

```markdown
# Master Image

A fast, keyboard-driven photo viewer and culling tool for Windows. Built for going through a shoot
quickly: seek instantly, compare in a tile grid, mark keepers, and move them out in one keystroke.

Opens JPEG, PNG, GIF, BMP, WebP, TIFF and camera RAW (Sony ARW, Nikon NEF, Canon CR2/CR3, Adobe DNG,
Fuji RAF, Olympus ORF, Panasonic RW2 and ~30 more).

## Install

Download `Setup.exe` from the [latest release](https://github.com/KAIHARI/master-image/releases) and
run it. It installs per-user — no admin rights needed.

**Windows will show a "Windows protected your PC" warning.** The app isn't code-signed (certificates
cost a few hundred a year). Click **More info → Run anyway**.

To make it your default viewer: after installing, open **Settings → Apps → Default apps → Master
Image** and set the file types you want. Windows requires you to confirm this yourself — no app is
allowed to take defaults silently.

## Shortcuts

| Key | Action |
|---|---|
| `←` / `→` | Previous / next photo |
| Scroll | Zoom at the cursor |
| Drag | Pan (zoomed in) / move the window (not zoomed) |
| Double-click | Maximise / restore |
| `F` | Fullscreen |
| `Shift` | Toggle the tile grid |
| Arrows (in grid) | Browse tiles |
| `Enter` / click | Open the selected tile |
| `1` / `2` / `3` | Smaller / bigger / default tile size |
| `M` | Mark / unmark |
| `N` | Move marked photos into `selected/` |
| `L` | Preload the folder for instant seeking |
| `I` | Show all shortcuts |
| `U` | Check for updates |
| `Esc` | Close panel / close grid / exit fullscreen |
| `Alt+F4` | Quit |

## RAW support

RAW decoding uses Windows' **Raw Image Extension** — a free Microsoft Store download. Without it, RAW
files won't open and the app will tell you so. Install it from the Microsoft Store if you shoot RAW.

RAW files open via the camera's embedded preview rather than a full demosaic, which is roughly 13x
faster and looks the same for culling purposes.

## How it stores things

- Thumbnails are cached in a hidden `.thumbnails` folder next to your photos.
- Marks are remembered in `.thumbnails/marks.json`, so they survive closing the app.
- `N` moves marked photos into a `selected/` subfolder. Nothing is ever deleted.

## Building from source

Needs the .NET 8 SDK.

```
dotnet build -c Release
dotnet test tests/MasterImage.Core.Tests
.\scripts\release.ps1 -Version 1.0.0    # produces .\Releases\Setup.exe
```
```

- [ ] **Step 4: Build a release and confirm the artifacts**

```bash
cd "/c/Users/kaihu/Documents/projects/image viewer"
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Version 1.0.0
```
Expected: a `Releases\` folder containing `MasterImage-win-Setup.exe` (or similar) plus a `.nupkg` and
`RELEASES`-style feed file. Report the actual filenames and sizes. The Setup should be on the order of
100–200MB because the runtime is bundled — if it's ~2MB, the publish wasn't self-contained and users
would need .NET installed.

- [ ] **Step 5: Keep build output out of git**

Add to `.gitignore`:

```
publish/
Releases/
```

- [ ] **Step 6: Commit**

```bash
git add scripts/release.ps1 README.md .gitignore
git commit -m "Add release script and README"
```

---

## Task 6: Install it, and prove the whole thing works

**Files:** none (verification only)

This is the task that actually answers the original request: is it installed, and is it the default?

- [ ] **Step 1: Run the installer**

```bash
cd "/c/Users/kaihu/Documents/projects/image viewer"
taskkill //F //IM MasterImage.App.exe 2>/dev/null
ls -1 Releases/
```
Then run the Setup exe from `Releases\`. It installs to `%LocalAppData%\MasterImage` and launches.

- [ ] **Step 2: Confirm the install layout**

```bash
ls -1 "/c/Users/kaihu/AppData/Local/MasterImage/"
```
Expected: `MasterImage.exe` (the stable stub), `Update.exe`, and `current\`. If the stub is missing,
the file associations will be pointing at something that won't survive an update — stop and report.

- [ ] **Step 3: Confirm associations were registered, and at the STABLE path**

```bash
powershell -NoProfile -Command "
(Get-ItemProperty 'HKCU:\Software\Classes\MasterImage.Photo\shell\open\command' -EA SilentlyContinue).'(default)'
'--- registered for: ---'
@('.jpg','.arw','.nef','.cr3') | ForEach-Object {
  \$k = \"HKCU:\Software\Classes\$_\OpenWithProgids\"
  if (Test-Path \$k) { \"\$_ -> \$((Get-Item \$k).GetValueNames() -join ',')\" } else { \"\$_ -> NOT REGISTERED\" }
}
'--- in Default apps UI? ---'
(Get-ItemProperty 'HKCU:\Software\RegisteredApplications' -EA SilentlyContinue).MasterImage
"
```
Expected: the command points at `...\AppData\Local\MasterImage\MasterImage.exe` — **the stub at the
root, NOT anything under `current\`**. If it points into `current\`, associations will break on the
first update; report it rather than continuing.

- [ ] **Step 4: Make it the default**

```bash
powershell -NoProfile -Command "Start-Process 'ms-settings:defaultapps'"
```
Find **Master Image** in the list and set it as default for `.jpg` (and any other types wanted).
Windows requires this to be done by hand — there's no API.

- [ ] **Step 5: The real test — double-click a photo in Explorer**

```bash
explorer "C:\Users\kaihu\AppData\Local\Temp\MasterImage-testshoot"
```
Double-click a photo. Master Image should open **on that photo**, not on the first one in the folder —
which proves the `"%1"` argument survived the entry-point change end to end, through Explorer, the
stub exe, and the single-instance handoff.

Then, with it still open, double-click a *different* photo: the existing window should jump to it
rather than a second copy of the app opening.

- [ ] **Step 6: Confirm U reports sensibly**

Press `U` in the installed copy. With no GitHub release published yet, expect a plain message
(something like "Couldn't check for updates: ...") rather than a crash. This is the installed path, so
it should no longer say "this is a dev build".

- [ ] **Step 7: Report**

Report what happened at each step, especially:
- the exact `shell\open\command` value (stub vs `current\`),
- whether double-click opened the right photo,
- whether a second double-click reused the window,
- what `U` said.
