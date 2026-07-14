# WitchDrawer Project Plan

## MVP
- Implement normal boxes: real file/folder move into the app data directory.
- Implement mapping boxes: absolute-path references without moving files.
- Implement quick panel: `Ctrl+Alt+W`, all indexed items, search, and Shell open.
- Persist boxes and items in SQLite.
- Delete restores stored items to their original locations (desktop fallback if missing); mapping boxes only remove references.

## Next Milestone
- Target boxes bound to existing folders with two-way file-system sync.
- Rename and archive workflows for normal boxes.
- Tray icon, startup setting, and installer.
- Icon extraction and thumbnail cache with strict background processing.

## Later Milestone
- Magnetic access window attached to standard open/save dialogs.
- Split quick panels and browser-like file tabs.
- Performance instrumentation for launch time, hotkey latency, list render latency, and memory use.

## Acceptance Gates
- `dotnet build WitchDrawer.sln` passes.
- `dotnet test WitchDrawer.sln` passes.
- Manual smoke test covers drag into normal/mapping boxes, quick panel search, open, and delete.

