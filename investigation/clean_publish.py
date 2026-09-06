import os, subprocess

DOTNET = r"C:\Program Files\dotnet\dotnet.exe"
WORKDIR = r"D:\ADM\C#\WitchDrawer"

env = {
    "PATH": r"C:\Program Files\dotnet;C:\WINDOWS\system32;C:\WINDOWS",
    "SystemRoot": r"C:\WINDOWS",
    "SystemDrive": "C:",
    "COMSPEC": r"C:\WINDOWS\system32\cmd.exe",
    "USERPROFILE": r"C:\Users\Administrator",
    "HOMEDRIVE": "C:",
    "HOMEPATH": r"\Users\Administrator",
    "USERNAME": "Administrator",
    "APPDATA": r"C:\Users\Administrator\AppData\Roaming",
    "LOCALAPPDATA": r"C:\Users\Administrator\AppData\Local",
    "ProgramData": r"C:\ProgramData",
    "ProgramFiles": r"C:\Program Files",
    "ProgramFiles(x86)": r"C:\Program Files (x86)",
    "TEMP": r"C:\Users\Administrator\AppData\Local\Temp",
    "TMP": r"C:\Users\Administrator\AppData\Local\Temp",
    "OS": "Windows_NT",
    "PROCESSOR_ARCHITECTURE": "AMD64",
    "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    "DOTNET_NOLOGO": "1",
    "NUGET_PACKAGES": r"C:\Users\Administrator\.nuget\packages",
}

cmd = [
    DOTNET, "publish", r"src\WitchDrawer.App\WitchDrawer.App.csproj",
    "-c", "Release", "-r", "win-x64", "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-o", r"publish\v1.3.7",
]
print("RUN:", " ".join(cmd), flush=True)
r = subprocess.run(cmd, cwd=WORKDIR, env=env, capture_output=True)
out = r.stdout.decode("utf-8", errors="replace")
err = r.stderr.decode("utf-8", errors="replace")
print("exit:", r.returncode)
print("--- stdout (tail) ---")
print(out[-5000:])
print("--- stderr (tail) ---")
print(err[-3000:])
