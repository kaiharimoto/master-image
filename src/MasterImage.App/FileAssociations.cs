using System.Runtime.InteropServices;
using MasterImage.Core;
using Microsoft.Win32;

namespace MasterImage.App;

// Registers Master Image as an available handler for image and RAW files.
//
// Everything here writes under HKCU — per-user, so no admin rights, and it can't affect other
// accounts. Note this only *offers* the app for these types; Windows has not allowed an app to
// silently seize a default since Windows 8, so the last step is always the user confirming in
// Settings > Default apps. There is no supported API around that, by design.
public static class FileAssociations
{
    public const string ProgId = "MasterImage.Photo";
    private const string AppRegistrationName = "MasterImage";

    // Where the registration is written. Bundled together rather than passed as separate arguments
    // because they must be redirected as a set: tests point these at a scratch key, and redirecting
    // the classes path while leaving RegisteredApplications on the real hive would have the test
    // suite quietly rewriting the developer's live "Default apps" entry.
    public sealed record Location(string ClassesRoot, string RegisteredApplicationsKey)
    {
        public static readonly Location Machine =
            new(@"Software\Classes", @"Software\RegisteredApplications");
    }

    private static readonly string[] StandardExtensions =
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff"
    };

    public static IReadOnlyList<string> SupportedExtensions { get; } =
        StandardExtensions.Concat(RawFormats.Extensions).Distinct().ToList();

    public static void Register(string exePath, Location? location = null)
    {
        var target = location ?? Location.Machine;

        using (var progId = Registry.CurrentUser.CreateSubKey($@"{target.ClassesRoot}\{ProgId}"))
        {
            progId.SetValue(null, "Photo");

            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue(null, $"\"{exePath}\",0");

            using var command = progId.CreateSubKey(@"shell\open\command");
            // "%1" is what hands the double-clicked file to the app. Quoted, or any path with a
            // space in it arrives as several arguments and the app opens on the wrong thing.
            command.SetValue(null, $"\"{exePath}\" \"%1\"");
        }

        foreach (string extension in SupportedExtensions)
        {
            using var progIds = Registry.CurrentUser.CreateSubKey($@"{target.ClassesRoot}\{extension}\OpenWithProgids");
            // The value NAME is the ProgId and the data is ignored — that's the documented shape of
            // this key. Writing it again is a no-op, which is what makes reinstalling safe.
            progIds.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        RegisterInDefaultAppsUi(target);
        NotifyShell();
    }

    // Puts the app in Settings > Default apps, which is the only place the user can actually make it
    // the default.
    private static void RegisterInDefaultAppsUi(Location target)
    {
        string capabilities = $@"{target.ClassesRoot}\{AppRegistrationName}\Capabilities";

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

        using var registered = Registry.CurrentUser.CreateSubKey(target.RegisteredApplicationsKey);
        registered.SetValue(AppRegistrationName, capabilities);
    }

    public static void Unregister(Location? location = null)
    {
        var target = location ?? Location.Machine;

        Registry.CurrentUser.DeleteSubKeyTree($@"{target.ClassesRoot}\{ProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"{target.ClassesRoot}\{AppRegistrationName}", throwOnMissingSubKey: false);

        foreach (string extension in SupportedExtensions)
        {
            using var progIds = Registry.CurrentUser.OpenSubKey(
                $@"{target.ClassesRoot}\{extension}\OpenWithProgids", writable: true);

            // Only our value — this key is shared with every other app that handles the type.
            progIds?.DeleteValue(ProgId, throwOnMissingValue: false);
        }

        using (var registered = Registry.CurrentUser.OpenSubKey(target.RegisteredApplicationsKey, writable: true))
        {
            registered?.DeleteValue(AppRegistrationName, throwOnMissingValue: false);
        }

        NotifyShell();
    }

    // Without this, Explorer goes on showing the old associations until it's restarted.
    private static void NotifyShell() =>
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
