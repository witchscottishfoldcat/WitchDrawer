using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Windows;

/// <summary>
/// Reports process elevation so the UI can explain Windows restrictions on file
/// drops from unelevated windows without changing the user's launch permissions.
/// </summary>
public static class ProcessElevation
{
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationInformationClass = 20;

    public static bool IsCurrentProcessElevated()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            if (!GetTokenInformation(
                    token,
                    TokenElevationInformationClass,
                    out int isElevated,
                    sizeof(int),
                    out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            // TokenElevationType.Full only detects UAC split-token elevation.
            // TokenElevation also covers administrators running with UAC disabled.
            return isElevated != 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        nint processHandle,
        uint desiredAccess,
        out nint tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        nint tokenHandle,
        int tokenInformationClass,
        out int tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
