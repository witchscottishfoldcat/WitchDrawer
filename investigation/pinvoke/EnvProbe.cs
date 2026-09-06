using System;
public static class EnvProbe
{
    public static void Main()
    {
        Console.WriteLine($"CommonApplicationData=[{Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)}]");
        Console.WriteLine($"ProgramData env=[{Environment.GetEnvironmentVariable("ProgramData")}]");
    }
}
