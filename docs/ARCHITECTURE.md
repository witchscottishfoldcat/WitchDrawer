# WitchDrawer Architecture

## Runtime
- Target runtime: .NET 10 LTS, locked by `global.json`.
- UI: WPF on `net10.0-windows`.
- Persistence: SQLite at `%LocalAppData%\WitchDrawer\witchdrawer.db`.
- User file storage for normal boxes: `%LocalAppData%\WitchDrawer\Boxes\{BoxId}`.

## Layers
- `WitchDrawer.App`: WPF shell, main drawer, quick panel, drag/drop, command binding, and hotkey message handling.
- `WitchDrawer.Core`: `Box`, `DrawerItem`, SQLite repository, import/delete/open orchestration, path validation, and file-name conflict handling.
- `WitchDrawer.Native`: Shell open, recycle bin integration, and `RegisterHotKey`/`UnregisterHotKey` wrappers.

Core defines abstractions for native operations. Native implements them. App composes the concrete services.

## Data Flow
- Startup creates app directories, initializes SQLite schema, and creates the default normal and mapping boxes if the database is empty.
- Dragging into a normal box moves the file or folder into that box's storage directory, then persists a `DrawerItem`.
- Dragging into a mapping box stores the original absolute path only. The source file remains untouched.
- Quick panel reloads indexed items from SQLite and filters in memory for fast interactive search.

## File Safety
- Destination paths are normalized and verified to stay inside the target box storage root.
- Normal-box name conflicts use `name (1).ext`, `name (2).ext`, and so on.
- Delete uses `IFileTrash`; the Windows implementation sends files or directories to the recycle bin.
- Mapping items are removed from SQLite only; their source files are not changed.

## Performance Budget
- UI thread must not perform file IO, SQLite writes, or thumbnail/icon extraction.
- List controls must keep virtualization enabled.
- Quick panel should open from hotkey in under 200 ms for normal MVP-sized indexes.
- Idle CPU should stay near 0%, and idle memory should be kept under 150 MB where practical.

