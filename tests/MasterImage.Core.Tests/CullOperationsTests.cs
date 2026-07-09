using System;
using System.IO;
using System.Linq;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class CullOperationsTests : IDisposable
{
    private readonly string _tempDir;

    public CullOperationsTests()
    {
        _tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "MasterImageTests_" + Guid.NewGuid())).FullName;
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void MovesEachFileOfEachMarkedItemIntoSelectedFolder()
    {
        string pathA = Path.Combine(_tempDir, "DSC1.jpg");
        string pathB = Path.Combine(_tempDir, "DSC2.jpg");
        File.WriteAllBytes(pathA, new byte[] { 1 });
        File.WriteAllBytes(pathB, new byte[] { 2 });

        var marked = new[]
        {
            new PhotoItem("DSC1", new[] { pathA }),
            new PhotoItem("DSC2", new[] { pathB })
        };

        var result = CullOperations.MoveMarkedToSelectedFolder(_tempDir, marked);

        Assert.Equal(2, result.MovedFileCount);
        Assert.Empty(result.Failures);
        Assert.True(File.Exists(Path.Combine(_tempDir, "selected", "DSC1.jpg")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "selected", "DSC2.jpg")));
        Assert.False(File.Exists(pathA));
    }

    [Fact]
    public void MovesBothFilesOfAPairTogether()
    {
        string rawPath = Path.Combine(_tempDir, "DSC1.NEF");
        string jpgPath = Path.Combine(_tempDir, "DSC1.JPG");
        File.WriteAllBytes(rawPath, new byte[] { 1 });
        File.WriteAllBytes(jpgPath, new byte[] { 2 });

        var marked = new[] { new PhotoItem("DSC1", new[] { rawPath, jpgPath }) };

        var result = CullOperations.MoveMarkedToSelectedFolder(_tempDir, marked);

        Assert.Equal(2, result.MovedFileCount);
        Assert.True(File.Exists(Path.Combine(_tempDir, "selected", "DSC1.NEF")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "selected", "DSC1.JPG")));
    }

    [Fact]
    public void ReportsFailureWithoutThrowingWhenDestinationAlreadyExists()
    {
        string sourcePath = Path.Combine(_tempDir, "DSC1.jpg");
        File.WriteAllBytes(sourcePath, new byte[] { 1 });
        Directory.CreateDirectory(Path.Combine(_tempDir, "selected"));
        File.WriteAllBytes(Path.Combine(_tempDir, "selected", "DSC1.jpg"), new byte[] { 9 });

        var marked = new[] { new PhotoItem("DSC1", new[] { sourcePath }) };

        var result = CullOperations.MoveMarkedToSelectedFolder(_tempDir, marked);

        Assert.Equal(0, result.MovedFileCount);
        Assert.Single(result.Failures);
        Assert.True(File.Exists(sourcePath));
    }
}
