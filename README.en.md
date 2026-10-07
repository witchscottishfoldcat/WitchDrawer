<p align="center">
  <img src="src/WitchDrawer.App/Assets/app.png" alt="WitchDrawer" width="128" height="128" />
</p>

<h1 align="center">WitchDrawer</h1>

<p align="center">
  <img src="https://img.shields.io/badge/version-1.4.3-blue" alt="Version 1.4.3" />
  <img src="https://img.shields.io/badge/platform-Windows%20x64-blue" alt="Windows x64" />
  <img src="https://img.shields.io/badge/.NET-10.0-purple" alt=".NET 10" />
</p>

<p align="center">
  <a href="README.md">简体中文</a> · English · <a href="https://github.com/witchscottishfoldcat/WitchDrawer/releases/latest">Download</a> · <a href="docs/releases/v1.4.3.md">Release notes (Chinese)</a>
</p>

WitchDrawer is a lightweight Windows desktop file organizer built with native WPF. Keep frequently used files in desktop boxes, organize them with drag and drop, and open them through quick search.

[![WitchDrawer desktop preview](docs/images/witchdrawer-desktop-showcase.png)](https://www.bilibili.com/video/BV1zx3c6eEX8/)

[Watch the video demo](https://www.bilibili.com/video/BV1zx3c6eEX8/)

## Download and run

**Windows 11 x64** is recommended. Some features may be incompatible with Windows 10.

Download from [GitHub Releases](https://github.com/witchscottishfoldcat/WitchDrawer/releases/latest):

- **Installer**: run `WitchDrawer-Setup-vX.Y.Z-x64.exe` and follow the setup wizard.
- **Portable**: extract the entire `WitchDrawer-vX.Y.Z-win-x64.zip` archive, then run `WitchDrawer.App.exe`.

Both packages are self-contained and require no separate .NET runtime installation. A separate `.sha256` checksum file is provided for each package.

## Features

| Box | Purpose |
| --- | --- |
| Normal | Manage files stored in the app's data directory, with standard and pixel styles |
| Mapping | Store absolute references to files or folders while keeping source files in place |
| Drawer | Use the same file storage behavior as normal boxes, with an expandable file panel |
| To-do | Add, edit, complete and archive tasks, with completion tracking |

- **Quick panel**: search file items across boxes by name, path or box name.
- **File actions**: drag files in, out or between boxes; copy, paste, rename, copy paths and locate shortcut targets from the context menu.
- **Appearance**: three themes, adjustable opacity, colors, corners and icon sizes, plus auto-hide.
- **Desktop controls**: saved box positions; roll up normal and mapping boxes; hide desktop icons, with an optional toggle by double-clicking empty desktop space.
- **Everyday use**: system tray, startup at sign-in, update checks and diagnostic log export.

## Getting started

1. Launch the app and create a box on the home page. Choose a **mapping box** for project folders or files that should stay in their original locations.
2. Drag files or folders into the box and double-click an icon to open it.
3. Press `Ctrl+Alt+W` to search and open files in the quick panel. You can change this shortcut in settings.

Run with normal privileges for desktop drag and drop. When the app runs as administrator, Windows restricts incoming drops from non-elevated File Explorer or the desktop.

### File behavior

| Action | Normal (including pixel style) and drawer boxes | Mapping boxes |
| --- | --- | --- |
| Drag in from File Explorer | **Move** files or folders into the data directory | Add path references only |
| Paste files | Create copies and keep source files | Add path references only |
| Rename | Rename stored files; use the new name when restoring | Change the reference's display name only |
| Delete an item or box | Restore stored files to their original directory; fall back to the desktop if that directory no longer exists | Remove references only |

Name conflicts when storing or restoring files receive suffixes such as ` (1)` and ` (2)`. Mapping references do not track external moves; an item cannot be opened if its source path no longer exists.

Deleting a single to-do item can be undone within **10 seconds**, while the app remains running. Deleting an entire to-do box also deletes its archive history and cannot be undone. The completion rate includes all unarchived tasks in the box, regardless of date.

### Keyboard shortcuts

These file shortcuts apply in desktop file boxes. Select an item before copying, renaming or copying its path.

| Shortcut | Action |
| --- | --- |
| `Ctrl+C` / `Ctrl+V` | Copy / paste files |
| `Ctrl+Shift+C` | Copy the current file path |
| `F2` | Rename |
| `Delete` | Remove an item, restoring its file or removing its reference as described above |

## Data location

The default data directory is `%LocalAppData%\WitchDrawer\`. The portable version uses this directory too:

```text
witchdrawer.db     SQLite database
Boxes\{BoxId}\     Files in normal boxes (including pixel style) and drawer boxes
logs\             Runtime logs
```

You can migrate the directory using the data storage location setting. Restart to apply the change; the original directory remains as a backup. The `WITCHDRAWER_DATA_DIR` environment variable overrides the data directory and takes precedence over settings.

## Development and build

Requires Windows and .NET SDK `10.0.300` or a compatible .NET 10 SDK (see [global.json](global.json)). Run from the repository root:

```powershell
dotnet build WitchDrawer.sln
dotnet test WitchDrawer.sln
.\dev.ps1
```

`dev.ps1` launches the app in Debug mode. `build.ps1` builds the solution in Release mode.

- `src/WitchDrawer.App`: WPF windows, MVVM, drag and drop, and hotkey wiring.
- `src/WitchDrawer.Core`: models, SQLite persistence, search and safe file operations.
- `src/WitchDrawer.Native`: Win32 Shell, global hotkeys and desktop integration.
- `tests/`: App, Core and Native tests.

Packaging requires Inno Setup 6. The version is defined in [Directory.Build.props](Directory.Build.props):

```powershell
dotnet build WitchDrawer.sln --configuration Release
dotnet test WitchDrawer.sln --configuration Release
.\tools\Publish-WitchDrawer.ps1 -Version 1.4.3
```

The script creates a self-contained Windows x64 installer, a complete portable ZIP and a SHA-256 file for each in `publish/`. It fails if the Inno Setup compiler is unavailable. Before publishing, verify that the fully extracted ZIP launches, compare checksums with `Get-FileHash`, and upload all four files.

## Feedback and support

Report problems in [Issues](https://github.com/witchscottishfoldcat/WitchDrawer/issues), including the app version, Windows version and steps to reproduce. Export diagnostic logs from the About page if needed; the archive contains no database or user file contents.

Author: **Thewitchcat** · [Website](https://www.witchcat.cn) · [Email](mailto:witchscottishfoldcat@gmail.com) · [Support](https://www.witchcat.cn/zh/support) (include your ID to be listed in the acknowledgments)

## Licenses

- **Code, build scripts and configuration**: [PolyForm Noncommercial 1.0.0](LICENSE).
- **Documentation and media assets**, including `docs/` and `src/WitchDrawer.App/Assets/`: [CC BY-NC-SA 4.0](LICENSE-DOCS).

Both licenses are noncommercial. See the full license files for their terms.
