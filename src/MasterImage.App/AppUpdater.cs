using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace MasterImage.App;

// Checks GitHub Releases for a newer build, on demand only.
//
// Deliberately manual (the U key): a cull session shouldn't be interrupted by a download, and the app
// shouldn't restart itself out from under someone mid-pass. The accepted cost is that you only find
// out when you ask.
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
            // No release published yet, no network, private repo... none of that should take the app
            // down mid-session; the user just wants to know it didn't work.
            return new CheckResult(false, $"Couldn't check for updates: {ex.Message}");
        }
    }

    // Downloads the update found by CheckAsync and restarts into it. The process exits inside
    // ApplyUpdatesAndRestart, so nothing after that line runs on success.
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
