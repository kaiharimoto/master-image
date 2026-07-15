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

    // Writes a JPEG whose LEFT half is solid black and RIGHT half is solid white (a vertical
    // split), tagged with the given EXIF orientation. Large solid regions survive JPEG
    // compression cleanly, so post-rotation a sample pixel's brightness reveals which way the
    // image actually rotated — enough to tell 90deg (orientation 6) from 270deg (orientation 8),
    // which a dimensions-only check cannot (both produce the same swapped size).
    public static void WriteTwoToneJpegWithOrientation(string path, int width, int height, ushort exifOrientation)
    {
        var pixels = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte v = (byte)(x < width / 2 ? 0 : 255);
                int idx = (y * width + x) * 3;
                pixels[idx] = v;
                pixels[idx + 1] = v;
                pixels[idx + 2] = v;
            }
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

    // A PNG carries no System.Photo.Orientation tag, so it exercises ImageLoader's
    // "metadata present but no orientation key -> normal orientation" fallback branch.
    public static void WriteTestPng(string path, int width, int height)
    {
        var pixels = new byte[width * height * 3];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i % 256);
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
