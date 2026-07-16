using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MasterImage.Core;

// A video's poster frame, taken from the Windows Shell — the same image Explorer shows, already
// generated and cached by Windows.
//
// The alternative was decoding a frame ourselves, which means standing up a media pipeline, seeking
// it, and rendering it, on a background thread, purely to get one bitmap. The Shell has done that
// work already. This is the same bargain the RAW path makes with WIC: use what Windows has rather
// than carrying a decoder.
public static class VideoThumbnail
{
    public static BitmapSource? TryExtract(string filePath, int targetPixelWidth)
    {
        IntPtr bitmapHandle = IntPtr.Zero;

        try
        {
            var factoryId = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref factoryId, out var factory);

            // Height is a bound, not a target: the Shell preserves aspect ratio, so asking for a
            // square box and letting it fit inside yields targetPixelWidth on the long edge.
            var size = new NativeSize { cx = targetPixelWidth, cy = targetPixelWidth };

            // THUMBNAILONLY is the important flag. Without it the Shell happily falls back to the
            // generic filetype icon for a clip it can't read — which would sit in the grid looking
            // exactly like a real frame and lie about what's in the file.
            factory.GetImage(size, SIIGBF.ResizeToFit | SIIGBF.ThumbnailOnly, out bitmapHandle);

            if (bitmapHandle == IntPtr.Zero) return null;

            var frame = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            // Frozen so the thumbnail pipeline's background threads can hand it to the UI thread —
            // same contract as everything ImageLoader returns.
            frame.Freeze();
            return frame;
        }
        catch (COMException)
        {
            // No thumbnail handler, no codec, or a corrupt file. Returning null matches
            // ImageLoader's contract: the caller shows a placeholder, nothing takes the app down.
            return null;
        }
        catch (ArgumentException)
        {
            // CreateBitmapSourceFromHBitmap rejects a degenerate bitmap.
            return null;
        }
        finally
        {
            // The HBITMAP is ours the moment GetImage succeeds; CreateBitmapSourceFromHBitmap
            // copies rather than taking ownership, so without this every thumbnail leaks a GDI
            // object — and GDI handles are a per-process quota, not just memory.
            if (bitmapHandle != IntPtr.Zero)
            {
                DeleteObject(bitmapHandle);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr bindContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(NativeSize size, SIIGBF flags, out IntPtr bitmapHandle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit = 0x00,
        ThumbnailOnly = 0x08,
    }
}
