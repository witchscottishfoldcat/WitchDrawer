namespace WitchDrawer.Native.Windows;

public static class DesktopDisplayMessages
{
    public static bool ChangesLayout(int message, nint wordParameter) =>
        message == 0x007E // WM_DISPLAYCHANGE
        || (message == 0x001A && wordParameter == 0x002F); // WM_SETTINGCHANGE / SPI_SETWORKAREA
}
