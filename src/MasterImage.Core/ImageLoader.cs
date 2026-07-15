using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MasterImage.Core;

public static class ImageLoader
{
    public static BitmapSource? TryLoadAtSize(string filePath, int decodePixelWidth)
    {
        return TryLoad(filePath, decodePixelWidth);
    }

    public static BitmapSource? TryLoadFullResolution(string filePath)
    {
        return TryLoad(filePath, decodePixelWidth: 0);
    }

    private static BitmapSource? TryLoad(string filePath, int decodePixelWidth)
    {
        try
        {
            ushort orientation = ReadExifOrientation(filePath);

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            BitmapSource decoded = RawFormats.IsRaw(filePath)
                ? LoadRawPreview(stream, decodePixelWidth)
                : LoadStandardImage(stream, decodePixelWidth);

            BitmapSource result = ApplyOrientation(decoded, orientation);
            if (!result.IsFrozen)
            {
                result.Freeze();
            }
            return result;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (FileFormatException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static BitmapSource LoadStandardImage(Stream stream, int decodePixelWidth)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelWidth > 0)
        {
            bitmap.DecodePixelWidth = decodePixelWidth;
        }
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    // RAW files carry a full-size JPEG the camera already rendered. Reading that is ~13x cheaper
    // than demosaicing the sensor data (measured: 285ms vs 3703ms on a 125MB 61MP ARW) and is
    // indistinguishable for culling, since it's the same rendering the camera would show you.
    //
    // BitmapCacheOption.None is load-bearing: OnLoad decodes the whole frame up front and costs
    // 1.5-3.4s, which throws away the entire benefit. DecodePixelWidth isn't available here (that's
    // a BitmapImage feature), so the preview is scaled down afterwards instead.
    private static BitmapSource LoadRawPreview(Stream stream, int decodePixelWidth)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None);

        // Preview is the camera's full-size render; Thumbnail is a small one. Prefer Preview, and
        // fall back rather than failing outright on a camera that only embeds a thumbnail.
        BitmapSource source = decoder.Preview
            ?? decoder.Thumbnail
            ?? throw new NotSupportedException("RAW file has no embedded preview or thumbnail.");

        if (decodePixelWidth > 0 && source.PixelWidth > decodePixelWidth)
        {
            double scale = decodePixelWidth / (double)source.PixelWidth;
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        }

        // Copy the pixels out before the stream closes: with CacheOption.None the decoder reads
        // lazily, and everything above is still just a promise until something realises it.
        var realised = new WriteableBitmap(new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0));
        realised.Freeze();
        return realised;
    }

    // EXIF orientation lives in *frame* metadata. BitmapImage.Metadata is NOT usable for this —
    // it throws NotSupportedException unconditionally ("BitmapMetadata is not available on
    // BitmapImage"), so orientation must be read from a BitmapFrame via the decoder instead.
    // This is a lightweight header-only read (BitmapCacheOption.None, no full pixel decode) on
    // its own read-only stream; any failure falls back to "normal" so a metadata quirk can
    // never prevent an image from loading.
    private static ushort ReadExifOrientation(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None);
            if (decoder.Frames.Count > 0
                && decoder.Frames[0].Metadata is BitmapMetadata metadata
                && metadata.GetQuery("System.Photo.Orientation") is { } value)
            {
                return Convert.ToUInt16(value);
            }
        }
        catch (Exception)
        {
            // No readable orientation metadata (e.g. PNG/BMP/GIF, an unsupported/corrupt file,
            // or a value WIC can't parse). Reading orientation is strictly best-effort — a
            // failure here must never stop the image itself from decoding — so swallow broadly
            // and treat as normal orientation. If the file is genuinely unloadable, the real
            // decode in TryLoad will surface that and return null.
        }
        return 1;
    }

    // Cameras only ever produce EXIF orientation 1 (normal), 3 (180deg), 6 (90deg CW), or
    // 8 (270deg CW / 90deg CCW) - the mirror-flip variants (2, 4, 5, 7) come only from certain
    // scanning/editing tools and are deliberately left un-rotated (angle 0) here as out of scope.
    private static BitmapSource ApplyOrientation(BitmapSource source, ushort orientation)
    {
        double angle = orientation switch
        {
            3 => 180,
            6 => 90,
            8 => 270,
            _ => 0,
        };

        return angle == 0 ? source : new TransformedBitmap(source, new RotateTransform(angle));
    }

    public static void SaveAsJpeg(BitmapSource source, string destinationPath, int quality = 85)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(source));

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var stream = File.Create(destinationPath);
        encoder.Save(stream);
    }
}
