using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class RawFormatsTests
{
    [Theory]
    [InlineData("DSC09423.ARW")]
    [InlineData("photo.arw")]
    [InlineData("shot.NEF")]
    [InlineData("shot.CR2")]
    [InlineData("shot.CR3")]
    [InlineData("shot.DNG")]
    [InlineData("shot.RAF")]
    [InlineData("shot.ORF")]
    [InlineData("shot.RW2")]
    public void RecognisesRawExtensions(string fileName)
    {
        Assert.True(RawFormats.IsRaw(fileName));
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.JPEG")]
    [InlineData("photo.png")]
    [InlineData("photo.tif")]
    [InlineData("notes.txt")]
    [InlineData("no-extension")]
    public void DoesNotClaimNonRawFiles(string fileName)
    {
        Assert.False(RawFormats.IsRaw(fileName));
    }

    [Fact]
    public void ExtensionsAreLowercaseAndDotted()
    {
        // PhotoSet compares against Path.GetExtension(...).ToLowerInvariant(), so the set has to
        // be in that exact shape or nothing will ever match.
        Assert.All(RawFormats.Extensions, e =>
        {
            Assert.StartsWith(".", e);
            Assert.Equal(e.ToLowerInvariant(), e);
        });
    }
}
