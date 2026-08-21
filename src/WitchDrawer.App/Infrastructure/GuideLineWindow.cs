using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Infrastructure;

public sealed class GuideLineWindow : Window
{
    private const double OverlayThickness = 4;
    private static readonly nint WindowPositionTopmost = -1;
    private const uint SetWindowPosNoActivate = 0x0010;

    private readonly bool _isVertical;
    private readonly Line _line;
    private HwndSource? _source;

    public GuideLineWindow(bool isVertical)
    {
        _isVertical = isVertical;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        Width = OverlayThickness;
        Height = OverlayThickness;

        _line = new Line
        {
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }),
            SnapsToDevicePixels = true
        };

        // Dynamically reference active AccentBrush theme resource
        _line.SetResourceReference(Shape.StrokeProperty, "AccentBrush");

        var canvas = new Canvas();
        canvas.Children.Add(_line);
        Content = canvas;
    }

    public void UpdateLine(double x1, double y1, double x2, double y2)
    {
        var bounds = CalculateOverlayBounds(_isVertical, x1, y1, x2, y2);
        var handle = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(
            handle,
            WindowPositionTopmost,
            ToNativeCoordinate(bounds.Left),
            ToNativeCoordinate(bounds.Top),
            Math.Max(1, ToNativeCoordinate(bounds.Width)),
            Math.Max(1, ToNativeCoordinate(bounds.Height)),
            SetWindowPosNoActivate);

        // The HWND uses virtual-desktop physical pixels. Line geometry remains
        // local WPF content, so convert only the local lengths back to DIPs.
        var dpi = VisualTreeHelper.GetDpi(this);
        var widthDip = bounds.Width / dpi.DpiScaleX;
        var heightDip = bounds.Height / dpi.DpiScaleY;
        Width = widthDip;
        Height = heightDip;

        if (_isVertical)
        {
            _line.X1 = widthDip / 2;
            _line.X2 = widthDip / 2;
            _line.Y1 = y1 <= y2 ? 0 : heightDip;
            _line.Y2 = y1 <= y2 ? heightDip : 0;
            return;
        }

        _line.X1 = x1 <= x2 ? 0 : widthDip;
        _line.X2 = x1 <= x2 ? widthDip : 0;
        _line.Y1 = heightDip / 2;
        _line.Y2 = heightDip / 2;
    }

    internal static Rect CalculateOverlayBounds(
        bool isVertical,
        double x1,
        double y1,
        double x2,
        double y2)
    {
        if (isVertical)
        {
            return new Rect(
                x1 - (OverlayThickness / 2),
                Math.Min(y1, y2),
                OverlayThickness,
                Math.Max(1, Math.Abs(y2 - y1)));
        }

        return new Rect(
            Math.Min(x1, x2),
            y1 - (OverlayThickness / 2),
            Math.Max(1, Math.Abs(x2 - x1)),
            OverlayThickness);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        NonActivatingOverlayWindow.Configure(handle);
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowMessageHook);
    }

    protected override void OnClosed(EventArgs e)
    {
        _source?.RemoveHook(WindowMessageHook);
        _source = null;
        base.OnClosed(e);
    }

    private static nint WindowMessageHook(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (NonActivatingOverlayWindow.IsNonClientHitTestMessage(message))
        {
            handled = true;
            return NonActivatingOverlayWindow.TransparentHitTestResult;
        }

        return nint.Zero;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    private static int ToNativeCoordinate(double value) =>
        checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
}
