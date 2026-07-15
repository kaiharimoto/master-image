using System.IO;

namespace MasterImage.Core;

// The camera RAW formats Windows can decode.
//
// This list isn't aspirational — it's exactly what the installed "Microsoft Raw Image Decoder"
// registers itself for (read from its WIC codec registration), so every entry is genuinely
// decodable rather than merely hoped for. RAW support rides entirely on that codec, which ships as
// the free Raw Image Extension from the Microsoft Store.
public static class RawFormats
{
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".3fr", ".ari", ".arw", ".bay", ".cap", ".cr2", ".cr3", ".crw", ".dcs", ".dcr", ".drf",
        ".eip", ".erf", ".fff", ".iiq", ".k25", ".kdc", ".mef", ".mos", ".mrw", ".nef", ".nrw",
        ".orf", ".ori", ".pef", ".ptx", ".pxn", ".raf", ".raw", ".rw2", ".rwl", ".sr2", ".srf",
        ".srw", ".x3f", ".dng"
    };

    public static bool IsRaw(string filePath) => Extensions.Contains(Path.GetExtension(filePath));
}
