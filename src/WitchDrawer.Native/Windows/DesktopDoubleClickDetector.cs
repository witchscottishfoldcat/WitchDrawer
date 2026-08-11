using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Windows;

public sealed class DesktopDoubleClickDetector
{
    private const int DoubleClickWidthMetric = 36; // SM_CXDOUBLECLK
    private const int DoubleClickHeightMetric = 37; // SM_CYDOUBLECLK

    private readonly uint _maximumDelay;
    private readonly int _maximumWidth;
    private readonly int _maximumHeight;
    private bool _hasFirstClick;
    private int _lastScreenX;
    private int _lastScreenY;
    private uint _lastTimestamp;

    public DesktopDoubleClickDetector()
        : this(
            GetDoubleClickTime(),
            GetSystemMetrics(DoubleClickWidthMetric),
            GetSystemMetrics(DoubleClickHeightMetric))
    {
    }

    internal DesktopDoubleClickDetector(uint maximumDelay, int maximumWidth, int maximumHeight)
    {
        _maximumDelay = maximumDelay;
        _maximumWidth = maximumWidth;
        _maximumHeight = maximumHeight;
    }

    public bool RegisterClick(int screenX, int screenY, uint timestamp, bool isDesktopBackground)
    {
        if (!isDesktopBackground)
        {
            Reset();
            return false;
        }

        var isDoubleClick = _hasFirstClick
            && unchecked(timestamp - _lastTimestamp) <= _maximumDelay
            && Math.Abs((long)screenX - _lastScreenX) * 2 <= _maximumWidth
            && Math.Abs((long)screenY - _lastScreenY) * 2 <= _maximumHeight;

        if (isDoubleClick)
        {
            Reset();
            return true;
        }

        _hasFirstClick = true;
        _lastScreenX = screenX;
        _lastScreenY = screenY;
        _lastTimestamp = timestamp;
        return false;
    }

    public void Reset()
    {
        _hasFirstClick = false;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
