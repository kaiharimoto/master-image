using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MasterImage.Core.Tests;

public static class TestImageFactory
{
    public static void WriteTestJpeg(string path, int width = 64, int height = 48)
    {
        var pixels = new byte[width * height * 3];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i % 256);
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static void WriteTestJpegWithOrientation(string path, int width, int height, ushort exifOrientation)
    {
        var pixels = new byte[width * height * 3];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i % 256);
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);

        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("System.Photo.Orientation", exifOrientation);

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, thumbnail: null, metadata: metadata, colorContexts: null));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
