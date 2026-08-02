using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace WitchDrawer.App.Infrastructure;

internal static class ScreenBackdropCapture
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const int BlurPadding = 24;
    private const int SourceCopy = 0x00CC0020;
    private const int CaptureLayeredWindows = 0x40000000;

    public static BitmapSource? CaptureGaussianBlur(IntPtr windowHandle, double radius = 18)
    {
        if (windowHandle == IntPtr.Zero || !GetWindowRect(windowHandle, out var target))
        {
            return null;
        }

        var virtualLeft = GetSystemMetrics(SmXVirtualScreen);
        var virtualTop = GetSystemMetrics(SmYVirtualScreen);
        var virtualRight = virtualLeft + GetSystemMetrics(SmCxVirtualScreen);
        var virtualBottom = virtualTop + GetSystemMetrics(SmCyVirtualScreen);
        var captureLeft = Math.Max(virtualLeft, target.Left - BlurPadding);
        var captureTop = Math.Max(virtualTop, target.Top - BlurPadding);
        var captureRight = Math.Min(virtualRight, target.Right + BlurPadding);
        var captureBottom = Math.Min(virtualBottom, target.Bottom + BlurPadding);
        var captureWidth = captureRight - captureLeft;
        var captureHeight = captureBottom - captureTop;
        if (captureWidth <= 0 || captureHeight <= 0)
        {
            return null;
        }

        var source = CaptureScreenRegion(captureLeft, captureTop, captureWidth, captureHeight);
        if (source is null)
        {
            return null;
        }

        var blurred = RenderBlurred(source, captureWidth, captureHeight, radius);
        var crop = new CroppedBitmap(
            blurred,
            new Int32Rect(
                target.Left - captureLeft,
                target.Top - captureTop,
                target.Right - target.Left,
                target.Bottom - target.Top));
        crop.Freeze();
        return crop;
    }

    private static BitmapSource? CaptureScreenRegion(int left, int top, int width, int height)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, width, height);
        var previous = bitmap == IntPtr.Zero ? IntPtr.Zero : SelectObject(memoryDc, bitmap);
        try
        {
            if (memoryDc == IntPtr.Zero
                || bitmap == IntPtr.Zero
                || !BitBlt(
                    memoryDc,
                    0,
                    0,
                    width,
                    height,
                    screenDc,
                    left,
                    top,
                    SourceCopy | CaptureLayeredWindows))
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (previous != IntPtr.Zero)
            {
                _ = SelectObject(memoryDc, previous);
            }

            if (bitmap != IntPtr.Zero)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                _ = DeleteDC(memoryDc);
            }

            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static BitmapSource RenderBlurred(
        BitmapSource source,
        int width,
        int height,
        double radius)
    {
        var image = new Image
        {
            Source = source,
            Stretch = Stretch.Fill,
            Effect = new BlurEffect
            {
                Radius = radius,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Quality
            }
        };
        image.Measure(new Size(width, height));
        image.Arrange(new Rect(0, 0, width, height));
        image.UpdateLayout();

        var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        result.Render(image);
        result.Freeze();
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr destination,
        int destinationX,
        int destinationY,
        int width,
        int height,
        IntPtr source,
        int sourceX,
        int sourceY,
        int operation);
}
