namespace WitchDrawer.Core;

public sealed record AppPaths(string RootDirectory)
{
    public string BoxesDirectory => Path.Combine(RootDirectory, "Boxes");

    public string DatabasePath => Path.Combine(RootDirectory, "witchdrawer.db");

    public string LogsDirectory => Path.Combine(RootDirectory, "logs");

    public static AppPaths ForCurrentUser()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppPaths(Path.Combine(localAppData, "WitchDrawer"));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(BoxesDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}

