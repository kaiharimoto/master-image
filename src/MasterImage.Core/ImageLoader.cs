using System.IO;
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
            return bitmap;
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

    public static void SaveAsJpeg(BitmapSource source, string destinationPath, int quality = 85)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(source));

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var stream = File.Create(destinationPath);
        encoder.Save(stream);
    }
}
