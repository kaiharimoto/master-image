using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class VideoFormatsTests
{
    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("clip.mov")]
    [InlineData("clip.m4v")]
    [InlineData("clip.avi")]
    [InlineData("clip.wmv")]
    [InlineData("clip.mkv")]
    [InlineData("clip.webm")]
    [InlineData("clip.mts")]   // AVCHD, straight off a camera
    [InlineData("clip.m2ts")]
    [InlineData("clip.3gp")]
    public void RecognisesTheFormatsCamerasWrite(string path) =>
        Assert.True(VideoFormats.IsVideo(path));

    [Theory]
    [InlineData("CLIP.MP4")]
    [InlineData("Clip.MoV")]
    [InlineData(@"C:\shoot\DSC_0001.MTS")]
    public void IsCaseInsensitiveAndPathTolerant(string path) =>
        Assert.True(VideoFormats.IsVideo(path));

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.png")]
    [InlineData("photo.tiff")]
    [InlineData("photo.webp")] // one letter from .webm — the pair most likely to be confused
    [InlineData("photo.ARW")]
    [InlineData("photo.CR3")]
    [InlineData("notes.txt")]
    [InlineData("noextension")]
    public void RejectsStillsAndEverythingElse(string path) =>
        Assert.False(VideoFormats.IsVideo(path));
}
