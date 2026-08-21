using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Shell;

/// <summary>
/// 收纳项右键菜单里用户可执行的、可控的精简动作。
/// </summary>
public enum ItemContextAction
{
    /// <summary>没有选择（点击其它区域关闭菜单）。</summary>
    None,

    /// <summary>用默认程序打开文件/文件夹（仅普通文件/文件夹显示，作为第一项）。</summary>
    Open,

    /// <summary>以管理员身份运行（仅对可执行文件显示，作为第一项）。</summary>
    RunAsAdministrator,

    /// <summary>打开系统「属性」对话框。</summary>
    Properties,

    /// <summary>复制文件/文件夹到剪贴板（不改动原文件）。</summary>
    Copy,

    /// <summary>从收纳盒移除（由 WitchDrawer 安全服务原位还原）。</summary>
    RemoveFromBox,
}

/// <summary>
/// 为收纳项弹出一个固定、精简的原生右键菜单（不依赖 Windows 系统右键菜单，
/// 因此不会混入 7-Zip、杀毒、Git 等第三方 Shell 扩展，也不受系统语言影响）。
///
/// 菜单只保留与「桌面文件收纳」定位相符的几条:打开 / 以管理员身份运行 / 属性 /
/// 复制 / 移除。删除、重命名、剪切等会绕过 WitchDrawer 数据一致性的动作一律不提供。
/// </summary>
public static class ItemContextMenu
{
    private const int CommandOpen = 1;
    private const int CommandRunAs = 2;
    private const int CommandProperties = 3;
    private const int CommandCopy = 4;
    private const int CommandRemove = 5;

    private const uint MfString = 0x0000;
    private const uint MfSeparator = 0x0800;

    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmNonotify = 0x0080;
    private const uint TpmRightButton = 0x0002;

    /// <summary>
    /// 弹出菜单并返回用户选择的动作。必须在 WPF UI 线程调用，且 <paramref name="ownerHandle"/>
    /// 必须是有效的窗口句柄。
    /// </summary>
    /// <param name="ownerHandle">拥有菜单的窗口句柄。</param>
    /// <param name="showRunAs">是否显示「以管理员身份运行」项（调用方按文件扩展名决定）。</param>
    /// <param name="screenX">弹出位置的屏幕物理 X 坐标。</param>
    /// <param name="screenY">弹出位置的屏幕物理 Y 坐标。</param>
    public static ItemContextAction Show(
        nint ownerHandle,
        bool showRunAs,
        int screenX,
        int screenY)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePopupMenu failed.");
        }

        try
        {
            // 第一项：可执行文件用「以管理员身份运行」（提权打开），普通文件/文件夹
            // 用「打开」（它们无法提权）。
            if (showRunAs)
            {
                NativeMethods.AppendMenuW(menu, MfString, CommandRunAs, "以管理员身份运行");
            }
            else
            {
                NativeMethods.AppendMenuW(menu, MfString, CommandOpen, "打开");
            }

            NativeMethods.AppendMenuW(menu, MfSeparator, 0, string.Empty);
            NativeMethods.AppendMenuW(menu, MfString, CommandProperties, "属性");
            NativeMethods.AppendMenuW(menu, MfString, CommandCopy, "复制");
            NativeMethods.AppendMenuW(menu, MfSeparator, 0, string.Empty);
            NativeMethods.AppendMenuW(menu, MfString, CommandRemove, "从收纳盒移除");

            var commandId = NativeMethods.TrackPopupMenuEx(
                menu,
                TpmReturnCmd | TpmNonotify | TpmRightButton,
                screenX,
                screenY,
                ownerHandle,
                nint.Zero);

            return commandId switch
            {
                CommandOpen => ItemContextAction.Open,
                CommandRunAs => ItemContextAction.RunAsAdministrator,
                CommandProperties => ItemContextAction.Properties,
                CommandCopy => ItemContextAction.Copy,
                CommandRemove => ItemContextAction.RemoveFromBox,
                _ => ItemContextAction.None,
            };
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    /// <summary>
    /// 以管理员身份运行 <paramref name="path"/>，通过 ShellExecuteEx 的 runas verb 触发
    /// UAC 提权对话框。
    /// </summary>
    public static bool TryRunAsAdministrator(string path)
    {
        return TryShellExecute(path, "runas");
    }

    /// <summary>
    /// 打开 <paramref name="path"/> 的系统「属性」对话框。
    /// </summary>
    public static bool TryShowProperties(string path)
    {
        return TryShellExecute(path, "properties");
    }

    private static bool TryShellExecute(string path, string verb)
    {
        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = 0,
            hwnd = nint.Zero,
            lpVerb = verb,
            lpFile = path,
            lpParameters = null,
            lpDirectory = Path.GetDirectoryName(path),
            nShow = 1, // SW_SHOWNORMAL
        };

        if (!NativeMethods.ShellExecuteExW(ref info))
        {
            // ERROR_CANCELLED=0x000004C7：用户在 UAC 上点了「否」，不算错误。
            return Marshal.GetLastWin32Error() == 0x000004C7;
        }

        if (info.hProcess != nint.Zero)
        {
            NativeMethods.CloseHandle(info.hProcess);
        }

        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public nint hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public nint hInstApp;
        public nint lpIDList;
        public string? lpClass;
        public nint hkeyClass;
        public uint dwHotKey;
        public nint hIcon;
        public nint hProcess;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint CreatePopupMenu();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyMenu(nint hMenu);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool AppendMenuW(nint hMenu, uint uFlags, nint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShellExecuteExW(ref ShellExecuteInfo pExecInfo);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(nint hObject);
    }
}
