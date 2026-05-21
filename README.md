# WitchDrawer

WitchDrawer is a lightweight Windows desktop file drawer built with native WPF. It is designed for desktop beautification and daily file staging: drag common files into small desktop drawers, open them quickly, and keep temporary work material organized without turning the UI into a heavy Electron/WebView app.

> Current status: MVP prototype.

## Features

- **Normal box**: moves dropped files or folders into WitchDrawer's app-data storage.
- **Mapping box**: stores absolute path references only; source files stay where they are.
- **Desktop drawer windows**: each box appears as a clean floating desktop drawer. They are not topmost, so normal application windows can cover them.
- **File icons**: dropped files show system-style icons when available.
- **Drag out**: items can be dragged back out from a drawer as file drops.
- **Delete item**: select an item and press `Delete`, or use the main window remove action.
- **Delete box**: the main page can delete the selected box.
- **Quick panel**: press `Ctrl+Alt+W` to search and open items across all boxes.
- **Themes**: includes a clean style and a glass-style theme.

## MVP Scope

Implemented in the first round:

- Normal boxes
- Mapping boxes
- Desktop drawer windows
- Quick panel
- SQLite persistence
- Recycle-bin based delete flow

Not implemented yet:

- Target boxes bound to existing folders with two-way sync
- Magnetic access window beside open/save dialogs
- Installer
- Tray icon and auto-start settings
- Thumbnail cache and advanced file previews

## Tech Stack

- .NET 10
- WPF
- Win32 API wrappers
- SQLite
- CommunityToolkit.Mvvm
- xUnit

The project intentionally avoids Electron, WebView shells, and heavy third-party UI frameworks.

## Repository Layout

```text
WitchDrawer.sln
src/
  WitchDrawer.App/       WPF UI, windows, view models, drag/drop, hotkey wiring
  WitchDrawer.Core/      models, SQLite persistence, file import/delete rules
  WitchDrawer.Native/    Shell open, recycle bin, global hotkeys
tests/
  WitchDrawer.Core.Tests/
docs/
  ARCHITECTURE.md
  PROJECT_PLAN.md
```

## Requirements

- Windows 10/11
- .NET SDK `10.0.300` or compatible .NET 10 SDK

The SDK version is locked by `global.json`.

## Build

```powershell
dotnet build WitchDrawer.sln
```

If `dotnet` is not on `PATH` but the local SDK is installed under the current user:

```powershell
C:\Users\Administrator\.dotnet\dotnet.exe build WitchDrawer.sln
```

The debug executable is generated at:

```text
src/WitchDrawer.App/bin/Debug/net10.0-windows/WitchDrawer.App.exe
```

## Test

```powershell
dotnet test WitchDrawer.sln
```

Current focused tests cover:

- default box creation
- normal-box file move
- mapping-box reference import
- duplicate file-name suffixing
- item delete through trash abstraction
- normal-box delete through trash abstraction
- mapping-box delete without touching source files

## Runtime Data

WitchDrawer stores app data under:

```text
%LocalAppData%\WitchDrawer
```

Important paths:

```text
%LocalAppData%\WitchDrawer\witchdrawer.db
%LocalAppData%\WitchDrawer\Boxes\{BoxId}
%LocalAppData%\WitchDrawer\logs
```

Normal boxes move files into `Boxes\{BoxId}`. Mapping boxes only store references in SQLite.

## File Safety Rules

- Normal boxes validate the target path before moving files.
- Name conflicts are resolved as `name (1).ext`, `name (2).ext`, etc.
- Mapping boxes never move, copy, or shortcut source files.
- Delete operations use the recycle bin abstraction by default.
- UI code should not directly mutate user files; file changes should flow through `WitchDrawer.Core`.

## Performance Rules

- No file scanning, file moving, SQLite writes, icon extraction, or thumbnail work on the UI thread.
- File lists must stay virtualized.
- Animations should use `Opacity` and `Transform`.
- Avoid real-time blur, oversized shadows, and large visual trees.
- Target idle CPU is near `0%`; target idle memory is under `150 MB` where practical.

## Development Notes

- Read `AGENTS.md` before modifying the project.
- Keep WPF as the primary UI.
- Add focused tests when changing file movement, deletion, persistence, or search behavior.
- Run both build and test before handing off changes:

```powershell
dotnet build WitchDrawer.sln
dotnet test WitchDrawer.sln
```

## Known Limitations

- Desktop drawers are regular non-topmost WPF windows. They are meant to sit on the desktop and be covered by normal apps, but they are not currently embedded into Explorer's desktop icon layer.
- The glass theme is implemented with lightweight translucent WPF surfaces; it avoids heavy runtime blur for performance.
- Quick panel is intentionally topmost because it is a temporary hotkey-driven access panel.
