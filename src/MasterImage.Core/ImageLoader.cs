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

            BitmapSource result = ApplyOrientation(bitmap, orientation);
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
