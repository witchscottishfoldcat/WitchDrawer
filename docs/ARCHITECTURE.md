# WitchDrawer Architecture

## Runtime
- Target runtime: .NET 10 LTS, locked by `global.json`.
- UI: WPF on `net10.0-windows`.
- Persistence: SQLite at `%LocalAppData%\WitchDrawer\witchdrawer.db`.
- User file storage for normal boxes: `%LocalAppData%\WitchDrawer\Boxes\{BoxId}`.

## Layers
- `WitchDrawer.App`: WPF shell, main drawer, quick panel, drag/drop, command binding, and hotkey message handling.
- `WitchDrawer.Core`: `Box`, `DrawerItem`, SQLite repository, import/delete/open orchestration, path validation, and file-name conflict handling.
- `WitchDrawer.Native`: Windows Shell, desktop integration, icon extraction, update/restart process helpers, and global hotkeys.

Core defines abstractions for native operations. Native implements them. App composes the concrete services.

## Dependency Direction

Project references remain `App -> Core`, `App -> Native -> Core`. Core has no WPF or Native dependency. `App.xaml.cs` composes runtime objects; no additional runtime package or DI framework was introduced.

## View Model Responsibilities

- `MainViewModel`: navigation, box collection, selection, and file commands.
- `SettingsViewModel`: theme, transparency, auto-hide, and desktop preferences.
- `UpdateViewModel`: update checks, progress, and confirmation events.
- `ArchiveViewModel`: archived todo loading, restore, delete, and undo.
- `MaintenanceViewModel`: data migration and diagnostic log export.
- `UiOperationState`: shared busy state and status text for page operations. Child view models do not reference MainViewModel or the quick panel.

## Settings Storage

New settings components depend on `ISettingsStore`. `SettingsService` owns background reads and ordered writes; `SettingsRepository` owns settings SQL. It reuses the main repository's connection factory and migration guard, so settings access cannot bypass the migration lock. Original settings methods on DrawerService and DrawerRepository remain thin compatibility forwarders.

## Content Synchronization

File and todo services share one `BoxChangeNotifier`. Successful content mutations publish affected box IDs after commit. A cross-box move publishes source and target together; failed operations do not report successful changes. Notification failures cannot turn committed file operations into apparent failures.

`BoxContentSyncCoordinator` dispatches to the WPF UI thread, merges affected box IDs, and refreshes MainViewModel, QuickPanelViewModel, and DesktopBoxManager independently. A failed surface does not block other surfaces. The main window queues changes while busy and replays them afterwards. Refreshes do not publish further changes. The coordinator unsubscribes during shutdown.

The quick panel remains lazy until first opened. Hidden desktop boxes reload when shown again. File-operation gates, durable intent, compensation, and recovery remain coordinated within DrawerService.

## Native Adapters

- `IDesktopIntegration` is implemented by `WindowsDesktopIntegration` for startup registry settings and desktop icon visibility.
- `IUpdateInstaller` is implemented by `WindowsUpdateInstaller`. Core downloads, verifies hashes, and extracts packages; Native owns update scripts, helper processes, and elevation.
- `ShellIconExtractor` owns native HICON/HBITMAP extraction and safe handles. App's `ShellIconProvider` copies pixels to frozen WPF images off the UI thread and retains its bounded cache.
- Native owns Windows restart helpers; App owns the decision to restart and the window lifecycle.
- Windows updater and restart regression tests live in `WitchDrawer.Native.Tests`; Core tests do not reference Native.

## Data Flow
- Startup creates app directories, initializes SQLite schema, and creates the default normal and mapping boxes if the database is empty.
- Dragging into a normal box moves the file or folder into that box's storage directory, then persists a `DrawerItem`.
- Dragging into a mapping box stores the original absolute path only. The source file remains untouched.
- Quick panel reloads indexed items from SQLite and filters in memory for fast interactive search.

## File Safety
- Destination paths are normalized and verified to stay inside the target box storage root.
- Normal-box name conflicts use `name (1).ext`, `name (2).ext`, and so on.
- Delete restores stored items to their original `SourcePath`; if that directory is missing, files fall back to the desktop. Name conflicts use `name (1).ext`, `name (2).ext`, and so on.
- File moves use same-volume rename when possible and fall back to copy-then-delete across volumes.
- Deleting a storage box restores items one-by-one and only removes the box when every restore succeeds.
- Mapping items are removed from SQLite only; their source files are not changed.

## Performance Budget
- UI thread must not perform file IO, SQLite writes, or thumbnail/icon extraction.
- List controls must keep virtualization enabled.
- Quick panel should open from hotkey in under 200 ms for normal MVP-sized indexes.
- Idle CPU should stay near 0%, and idle memory should be kept under 150 MB where practical.

