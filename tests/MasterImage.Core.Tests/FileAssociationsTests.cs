using System;
using System.Linq;
using MasterImage.App;
using Microsoft.Win32;
using Xunit;

namespace MasterImage.Core.Tests;

public class FileAssociationsTests : IDisposable
{
    // A scratch subtree of HKCU, so the suite never touches the real association hive — registering
    // the app for .jpg on the developer's machine as a side effect of running tests would be
    // unacceptable. Both the classes path AND the RegisteredApplications key are redirected here;
    // redirecting only the first would still let the tests rewrite the live "Default apps" entry.
    private const string ScratchParent = @"Software\MasterImageTests";

    private readonly string _root = ScratchParent + @"\" + Guid.NewGuid().ToString("N");
    private readonly FileAssociations.Location _scratch;

    public FileAssociationsTests()
    {
        _scratch = new FileAssociations.Location(
            ClassesRoot: $@"{_root}\Classes",
            RegisteredApplicationsKey: $@"{_root}\RegisteredApplications");
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

        // Drop the shared parent too once it's empty, so a test run leaves nothing at all behind in
        // the registry. Guarded on the subkey count so it can't delete another test's scratch area.
        using var parent = Registry.CurrentUser.OpenSubKey(ScratchParent);
        if (parent is not null && parent.SubKeyCount == 0)
        {
            Registry.CurrentUser.DeleteSubKeyTree(ScratchParent, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void RegisterCreatesAProgIdPointingAtTheGivenExe()
    {
        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _scratch);

        using var command = Registry.CurrentUser.OpenSubKey($@"{_scratch.ClassesRoot}\MasterImage.Photo\shell\open\command");
        Assert.NotNull(command);

        // The "%1" is what passes the double-clicked file to the app; without it the app opens with
        // no photo at all.
        Assert.Equal("\"C:\\Fake\\MasterImage.exe\" \"%1\"", command!.GetValue(null) as string);
    }

    [Fact]
    public void RegisterOffersTheAppForEveryStandardAndRawExtension()
    {
        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _scratch);

        foreach (string ext in new[] { ".jpg", ".png", ".tiff", ".arw", ".nef", ".cr3", ".dng" })
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{_scratch.ClassesRoot}\{ext}\OpenWithProgids");
            Assert.NotNull(key);
            Assert.Contains("MasterImage.Photo", key!.GetValueNames());
        }
    }

    [Fact]
    public void RegisterCoversEveryExtensionTheAppCanActuallyOpen()
    {
        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _scratch);

        // Anything the viewer can open should be offerable, or the association list quietly drifts
        // out of step with what the app supports.
        foreach (string ext in FileAssociations.SupportedExtensions)
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{_scratch.ClassesRoot}\{ext}\OpenWithProgids");
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
    public void RegisterAdvertisesTheAppInTheDefaultAppsUi()
    {
        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _scratch);

        // Windows reads RegisteredApplications to populate Settings > Default apps, which is the
        // only place the user can actually make this the default handler.
        using var registered = Registry.CurrentUser.OpenSubKey(_scratch.RegisteredApplicationsKey);
        Assert.NotNull(registered);
        Assert.Contains("MasterImage", registered!.GetValueNames());
    }

    // These two guard a bug this code actually had: RegisteredApplications was written to the real
    // hive regardless of the scratch location, so running the suite rewrote the developer's live
    // Default-apps entry — and it only ever *looked* clean because whichever test called Unregister
    // happened to run last.
    //
    // They compare before/after rather than asserting the real hive is empty. Once Master Image is
    // actually installed on a machine, those keys legitimately exist and hold real values; asserting
    // their absence would fail for a completely correct reason, on every user who installed the app.
    // What must hold is that a scratch-scoped Register leaves real state untouched.

    [Fact]
    public void RegisteringDoesNotTouchTheRealRegisteredApplicationsKey()
    {
        string? before = ReadRealRegisteredApplicationsEntry();

        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _scratch);

        Assert.Equal(before, ReadRealRegisteredApplicationsEntry());
    }

    [Fact]
    public void RegisteringDoesNotTouchTheRealClassesHive()
    {
        string? before = ReadRealProgIdCommand();

        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _scratch);

        Assert.Equal(before, ReadRealProgIdCommand());

        // Belt and braces: the fake path this test registers must never reach the real hive, whether
        // or not the app is installed.
        Assert.DoesNotContain(@"C:\Fake\MasterImage.exe", ReadRealProgIdCommand() ?? string.Empty);
    }

    private static string? ReadRealRegisteredApplicationsEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications");
        return key?.GetValue("MasterImage") as string;
    }

    private static string? ReadRealProgIdCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{FileAssociations.ProgId}\shell\open\command");
        return key?.GetValue(null) as string;
    }

    [Fact]
    public void UnregisterRemovesWhatRegisterAdded()
    {
        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _scratch);
        FileAssociations.Unregister(_scratch);

        Assert.Null(Registry.CurrentUser.OpenSubKey($@"{_scratch.ClassesRoot}\MasterImage.Photo"));

        using var registered = Registry.CurrentUser.OpenSubKey(_scratch.RegisteredApplicationsKey);
        Assert.DoesNotContain("MasterImage", registered?.GetValueNames() ?? Array.Empty<string>());

        using var jpg = Registry.CurrentUser.OpenSubKey($@"{_scratch.ClassesRoot}\.jpg\OpenWithProgids");
        // The .jpg key itself may survive (other apps register there too) — what must go is our
        // entry within it. A dangling ProgId would have Windows offering a deleted app.
        if (jpg is not null)
        {
            Assert.DoesNotContain("MasterImage.Photo", jpg.GetValueNames());
        }
    }

    [Fact]
    public void RegisterIsIdempotent()
    {
        // Reinstalling over an existing install must not throw or duplicate anything.
        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _scratch);
        FileAssociations.Register(@"C:\Fake\MasterImage.exe", _scratch);

        using var key = Registry.CurrentUser.OpenSubKey($@"{_scratch.ClassesRoot}\.jpg\OpenWithProgids");
        Assert.Single(key!.GetValueNames(), n => n == "MasterImage.Photo");
    }
}
