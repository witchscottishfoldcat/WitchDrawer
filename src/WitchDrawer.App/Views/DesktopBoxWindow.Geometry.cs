using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WitchDrawer.Native.Windows;
using static WitchDrawer.Native.Windows.User32Interop;

namespace WitchDrawer.App.Views;

/// <summary>
/// 窗口几何：可视区域换算、工作区钳制、DPI 坐标变换。含多个带测试的纯静态函数。
/// </summary>
public partial class DesktopBoxWindow
{
    /// <summary>
    /// 初始布局稳定后强制重测。SizeToContent 窗口的首次测量以初始 HWND 尺寸为约束，
    /// 若内容之后不再变化（如折叠抽屉盒的封面），窗口会一直停留在错误的初始宽度上
    /// （封面两侧突出）。一次 InvalidateMeasure 即可让窗口贴合真实内容。
    /// </summary>
    internal void ResyncSizeToContent()
    {
        if (SizeToContent != SizeToContent.Manual)
        {
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Enables work-area clamping after the manager has restored the saved origin,
    /// loaded the box contents and completed the first stable SizeToContent pass.
    /// Startup SizeChanged events use provisional template dimensions and must not
    /// move the window before its final size is known.
    /// </summary>
    internal void EnableVisibleBoundsClamping()
    {
        _isVisibleBoundsClampingEnabled = true;
    }

    internal Rect GetVisibleBounds() =>
        ComputeVisibleBounds(Left, Top, ActualWidth, ActualHeight, WindowBorder.Margin);

    /// <summary>
    /// Returns the native window rectangle in virtual-desktop physical pixels.
    /// Cross-window and cross-monitor operations must use this coordinate space:
    /// WPF Left/Top are monitor-DPI-dependent DIPs and cannot be compared safely
    /// when monitors use different scale factors.
    /// </summary>
    internal bool TryGetWindowBoundsPixels(out Rect bounds)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != nint.Zero
            && GetWindowRect(handle, out var nativeBounds)
            && nativeBounds.Right > nativeBounds.Left
            && nativeBounds.Bottom > nativeBounds.Top)
        {
            bounds = new Rect(
                nativeBounds.Left,
                nativeBounds.Top,
                nativeBounds.Right - nativeBounds.Left,
                nativeBounds.Bottom - nativeBounds.Top);
            return true;
        }

        bounds = Rect.Empty;
        return false;
    }

    internal Rect GetVisibleBoundsPixels()
    {
        if (!TryGetWindowBoundsPixels(out var windowBounds))
        {
            return Rect.Empty;
        }

        return ComputeVisibleBoundsPixels(windowBounds, WindowBorder.Margin, GetDpiScale());
    }

    /// <summary>
    /// SizeToContent 窗口在首次显示前，HWND 矩形仍是初始尺寸而非内容尺寸，
    /// 直接读 <see cref="GetVisibleBoundsPixels"/> 会把正常落位误判为越界并错误钳制。
    /// 这里用 Measure 后的 <see cref="UIElement.DesiredSize"/>（按当前 DPI 换算物理像素）
    /// 替代 HWND 尺寸，供显示前的落位钳制使用；HWND 位置部分仍以真实矩形为准。
    /// </summary>
    internal Rect GetMeasuredVisibleBoundsPixels()
    {
        if (!TryGetWindowBoundsPixels(out var windowBounds))
        {
            return Rect.Empty;
        }

        var dpi = GetDpiScale();
        var measured = ComputeMeasuredWindowBoundsPixels(windowBounds, DesiredSize, dpi);
        return ComputeVisibleBoundsPixels(measured, WindowBorder.Margin, dpi);
    }

    /// <summary>
    /// 与 <see cref="GetMeasuredVisibleBoundsPixels"/> 同理，但用布局完成后的
    /// <see cref="FrameworkElement.ActualWidth"/>/<see cref="FrameworkElement.ActualHeight"/>。
    /// SizeChanged 事件触发时 SizeToContent 的 HWND 可能尚未缩放到位，此时读 HWND
    /// 矩形会拿到初始尺寸；ActualWidth/Height 才是此刻的真实内容尺寸。
    /// </summary>
    internal Rect GetLayoutVisibleBoundsPixels()
    {
        if (!TryGetWindowBoundsPixels(out var windowBounds))
        {
            return Rect.Empty;
        }

        var dpi = GetDpiScale();
        var measured = ComputeMeasuredWindowBoundsPixels(
            windowBounds, new Size(ActualWidth, ActualHeight), dpi);
        return ComputeVisibleBoundsPixels(measured, WindowBorder.Margin, dpi);
    }

    internal static Rect ComputeMeasuredWindowBoundsPixels(
        Rect hwndBoundsPixels,
        Size desiredSizeDip,
        DpiScale dpi) =>
        new(
            hwndBoundsPixels.Left,
            hwndBoundsPixels.Top,
            Math.Max(0, desiredSizeDip.Width * dpi.DpiScaleX),
            Math.Max(0, desiredSizeDip.Height * dpi.DpiScaleY));

    internal DpiScale GetDpiScale() => VisualTreeHelper.GetDpi(this);

    internal void MoveWindowOriginPixels(double leftPixels, double topPixels)
    {
        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;
        if (handle == nint.Zero)
        {
            handle = helper.EnsureHandle();
        }

        SetWindowPos(
            handle,
            nint.Zero,
            ToNativeCoordinate(leftPixels),
            ToNativeCoordinate(topPixels),
            0,
            0,
            SetWindowPosNoSize | SetWindowPosNoActivate | SetWindowPosNoZOrder);
    }

    internal void MoveToVisibleOriginPixels(double visibleLeftPixels, double visibleTopPixels)
    {
        var origin = ComputeWindowOriginPixels(
            visibleLeftPixels,
            visibleTopPixels,
            WindowBorder.Margin,
            GetDpiScale());
        MoveWindowOriginPixels(origin.X, origin.Y);
    }

    /// <summary>
    /// <see cref="GetVisibleBounds"/> 的逆运算：把可视区域原点换算回窗口 Left/Top。
    /// 重叠消解在可视区域坐标系里计算，写回窗口位置时必须减去阴影留白 Margin，
    /// 否则每执行一次消解窗口就会按 Margin 平移一次（位置漂移）。
    /// </summary>
    internal void MoveToVisibleOrigin(double visibleLeft, double visibleTop)
    {
        var (left, top) = ComputeWindowOrigin(visibleLeft, visibleTop, WindowBorder.Margin);
        Left = left;
        Top = top;
    }

    /// <summary>
    /// SizeToContent 窗口以左上角为锚点随内容向右下生长。内容尺寸变化（切换图标预设、
    /// 固定格数、增删项目）可能把右/下边缘推出工作区——表现为盒子边缘被屏幕"吞掉"。
    /// 尺寸变化后把可视区域钳回工作区；只做显示性校正，不写回已保存位置。
    /// </summary>
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_mappingViewTransitionVisibleOriginPixels is Point anchoredOrigin
            && IsVisible
            && e.PreviousSize != e.NewSize)
        {
            MoveToVisibleOriginPixels(anchoredOrigin.X, anchoredOrigin.Y);
            return;
        }

        if (!ShouldClampVisibleBounds(
                _isVisibleBoundsClampingEnabled,
                _isMappingViewTransitioning,
                _isRollTransitioning,
                IsVisible,
                e.PreviousSize != e.NewSize))
        {
            return;
        }

        // 尺寸必须取布局结果（ActualWidth/Height）而非 HWND 矩形：SizeToContent 的
        // HWND 缩放与 SizeChanged 事件不同步，事件触发时 HWND 可能仍是初始尺寸，
        // 读 HWND 会把正常窗口误判为越界并错误钳回左上（首次显示时必现）。
        var bounds = GetLayoutVisibleBoundsPixels();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var workArea = GetWorkAreaPixels();
        if (workArea.IsEmpty)
        {
            return;
        }
        var visibleLeft = bounds.Left;
        var visibleTop = bounds.Top;
        if (bounds.Right > workArea.Right)
        {
            visibleLeft = workArea.Right - bounds.Width;
        }

        if (bounds.Bottom > workArea.Bottom)
        {
            visibleTop = workArea.Bottom - bounds.Height;
        }

        // 盒子比工作区还大时，左/上钳制优先，保证标题栏可见。
        visibleLeft = Math.Max(workArea.Left, visibleLeft);
        visibleTop = Math.Max(workArea.Top, visibleTop);
        if (Math.Abs(visibleLeft - bounds.Left) > 0.5
            || Math.Abs(visibleTop - bounds.Top) > 0.5)
        {
            MoveToVisibleOriginPixels(visibleLeft, visibleTop);
        }
    }

    internal static bool ShouldClampVisibleBounds(
        bool isClampingEnabled,
        bool isMappingViewTransitioning,
        bool isRollTransitioning,
        bool isVisible,
        bool sizeChanged) =>
        isClampingEnabled
        && !isMappingViewTransitioning
        && !isRollTransitioning
        && isVisible
        && sizeChanged;

    internal static Rect ComputeVisibleBounds(
        double windowLeft,
        double windowTop,
        double windowWidth,
        double windowHeight,
        Thickness margin) =>
        new(
            windowLeft + margin.Left,
            windowTop + margin.Top,
            Math.Max(0, windowWidth - margin.Left - margin.Right),
            Math.Max(0, windowHeight - margin.Top - margin.Bottom));

    internal static (double Left, double Top) ComputeWindowOrigin(
        double visibleLeft,
        double visibleTop,
        Thickness margin) =>
        (visibleLeft - margin.Left, visibleTop - margin.Top);

    internal static Rect ComputeVisibleBoundsPixels(
        Rect windowBoundsPixels,
        Thickness marginDip,
        DpiScale dpi) =>
        new(
            windowBoundsPixels.Left + (marginDip.Left * dpi.DpiScaleX),
            windowBoundsPixels.Top + (marginDip.Top * dpi.DpiScaleY),
            Math.Max(
                0,
                windowBoundsPixels.Width
                - ((marginDip.Left + marginDip.Right) * dpi.DpiScaleX)),
            Math.Max(
                0,
                windowBoundsPixels.Height
                - ((marginDip.Top + marginDip.Bottom) * dpi.DpiScaleY)));

    internal static Point ComputeWindowOriginPixels(
        double visibleLeftPixels,
        double visibleTopPixels,
        Thickness marginDip,
        DpiScale dpi) =>
        new(
            visibleLeftPixels - (marginDip.Left * dpi.DpiScaleX),
            visibleTopPixels - (marginDip.Top * dpi.DpiScaleY));

    /// <summary>
    /// Gets the current monitor work area in virtual-desktop physical pixels.
    /// This coordinate space remains stable across monitor DPI boundaries.
    /// </summary>
    internal Rect GetWorkAreaPixels()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return Rect.Empty;
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return Rect.Empty;
        }

        var info = new NativeMonitorInfo
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return Rect.Empty;
        }

        return new Rect(
            info.WorkArea.Left,
            info.WorkArea.Top,
            info.WorkArea.Right - info.WorkArea.Left,
            info.WorkArea.Bottom - info.WorkArea.Top);
    }

    internal static Rect GetPrimaryWorkAreaPixels()
    {
        // The primary monitor owns virtual-desktop origin (0,0).
        var monitor = MonitorFromPoint(new NativePoint(), MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return Rect.Empty;
        }

        var info = new NativeMonitorInfo
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return Rect.Empty;
        }

        return new Rect(
            info.WorkArea.Left,
            info.WorkArea.Top,
            info.WorkArea.Right - info.WorkArea.Left,
            info.WorkArea.Bottom - info.WorkArea.Top);
    }
}
