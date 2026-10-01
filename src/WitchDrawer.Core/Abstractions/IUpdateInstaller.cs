namespace WitchDrawer.Core.Abstractions;

public sealed record UpdateInstallRequest(
    string UpdateId, string TempRoot, string PayloadDirectory,
    string AppDirectory, string AppExecutablePath, string ExecutableName,
    string LogPath, int ProcessId, long ProcessStartTimeUtcTicks);

/// <summary>Starts the platform updater after Core has validated and prepared the package.</summary>
public interface IUpdateInstaller
{
    Task<bool> StartAsync(UpdateInstallRequest request);
}
