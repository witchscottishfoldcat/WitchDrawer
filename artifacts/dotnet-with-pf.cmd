@echo off
set "ProgramFiles=C:\Program Files"
set "ProgramFiles(x86)=C:\Program Files (x86)"
set "ProgramW6432=C:\Program Files"
set "PATH=C:\Program Files\dotnet;%PATH%"
set "WORKDIR=%~1"
shift
set "ARGS=%1"
:collect
shift
if "%~1"=="" goto run
set "ARGS=%ARGS% %~1"
goto collect
:run
dotnet build-server shutdown 1>nul 2>&1
cd /d "%WORKDIR%"
dotnet %ARGS%
