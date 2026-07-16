using System.IO;

namespace MasterImage.Core;

public static class VideoFormats
{
    // Chosen for what cameras actually write: MP4 and MOV cover most bodies and phones, MTS/M2TS is
    // AVCHD, AVI and WMV are legacy, MKV/WebM turn up in downloaded and screen-captured footage.
    //
    // Listing an extension is not a promise this machine can decode it — that's Media Foundation's
    // call, and a codec it lacks (HEVC being the likely one) surfaces as a named, plain-English
    // failure rather than a black frame. Same bargain the RAW path makes with WIC.
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".avi", ".wmv", ".mkv", ".webm", ".mts", ".m2ts", ".3gp",
    };

    public static bool IsVideo(string filePath) => Extensions.Contains(Path.GetExtension(filePath));

    public static IReadOnlyCollection<string> SupportedExtensions => Extensions;
}
