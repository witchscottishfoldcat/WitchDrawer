using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace WitchDrawer.Native.Windows;

/// <summary>
/// Keeps the desktop-facing process at the same integrity level as Explorer.
/// Elevated WPF windows cannot accept OLE drops from the normal desktop because
/// Windows blocks the cross-integrity messages before WPF receives DragOver.
/// </summary>
public static class ProcessElevation
{
    private const uint TokenQuery = 0x0008;
    private const uint LogonWithProfile = 0x00000001;

    public static bool RequiresUnelevatedRelaunch()
    {
        var token = OpenCurrentProcessToken();
        try
        {
            if (!GetTokenInformation(
                    token,
                    TokenInformationClass.TokenElevationType,
                    out TokenElevationType elevationType,
                    sizeof(int),
                    out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return elevationType == TokenElevationType.Full;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    public static bool TryRelaunchCurrentProcessUnelevated(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        out int nativeErrorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        nativeErrorCode = 0;
        var elevatedToken = nint.Zero;
        var linkedToken = nint.Zero;
        var processInformation = default(ProcessInformation);

        try
        {
            elevatedToken = OpenCurrentProcessToken();
            if (!GetTokenInformation(
                    elevatedToken,
                    TokenInformationClass.TokenLinkedToken,
                    out TokenLinkedToken linkedTokenInformation,
                    Marshal.SizeOf<TokenLinkedToken>(),
                    out _))
            {
                nativeErrorCode = Marshal.GetLastWin32Error();
                return false;
            }

            linkedToken = linkedTokenInformation.Token;
            var startupInformation = new StartupInformation
            {
                Size = Marshal.SizeOf<StartupInformation>(),
            };
            var commandLine = new StringBuilder(BuildCommandLine(executablePath, arguments));

            if (!CreateProcessWithTokenW(
                    linkedToken,
                    LogonWithProfile,
                    executablePath,
                    commandLine,
                    creationFlags: 0,
                    environment: nint.Zero,
                    workingDirectory,
                    ref startupInformation,
                    out processInformation))
            {
                nativeErrorCode = Marshal.GetLastWin32Error();
                return false;
            }

            return true;
        }
        catch (Win32Exception exception)
        {
            nativeErrorCode = exception.NativeErrorCode;
            return false;
        }
        finally
        {
            if (processInformation.Thread != nint.Zero)
            {
                CloseHandle(processInformation.Thread);
            }

            if (processInformation.Process != nint.Zero)
            {
                CloseHandle(processInformation.Process);
            }

            if (linkedToken != nint.Zero)
            {
                CloseHandle(linkedToken);
            }

            if (elevatedToken != nint.Zero)
            {
                CloseHandle(elevatedToken);
            }
        }
    }

    internal static string BuildCommandLine(
        string executablePath,
        IReadOnlyList<string> arguments)
    {
        return string.Join(
            ' ',
            new[] { executablePath }
                .Concat(arguments)
                .Select(QuoteCommandLineArgument));
    }

    internal static string QuoteCommandLineArgument(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        if (argument.Length > 0
            && !argument.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return argument;
        }

        var result = new StringBuilder(argument.Length + 2);
        result.Append('"');
        var backslashCount = 0;

        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashCount++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', (backslashCount * 2) + 1);
                result.Append(character);
                backslashCount = 0;
                continue;
            }

            result.Append('\\', backslashCount);
            result.Append(character);
            backslashCount = 0;
        }

        result.Append('\\', backslashCount * 2);
        result.Append('"');
        return result.ToString();
    }

    private static nint OpenCurrentProcessToken()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return token;
    }

    private enum TokenInformationClass
    {
        TokenElevationType = 18,
        TokenLinkedToken = 19,
    }

    private enum TokenElevationType
    {
        Default = 1,
        Full = 2,
        Limited = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenLinkedToken
    {
        public nint Token;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInformation
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public nint Reserved2;
        public nint StandardInput;
        public nint StandardOutput;
        public nint StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
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
        TokenInformationClass tokenInformationClass,
        out TokenElevationType tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        nint tokenHandle,
        TokenInformationClass tokenInformationClass,
        out TokenLinkedToken tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithTokenW(
        nint token,
        uint logonFlags,
        string? applicationName,
        StringBuilder commandLine,
        uint creationFlags,
        nint environment,
        string? currentDirectory,
        ref StartupInformation startupInformation,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
