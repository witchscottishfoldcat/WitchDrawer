# WitchDrawer Agent Constraints

## Product Goal
- WitchDrawer is a lightweight Windows file drawer for daily desktop work.
- The primary UI must stay native WPF. Do not replace it with Electron or a WebView shell.
- MVP scope is limited to normal boxes, mapping boxes, and the quick panel.

## Architecture Boundaries
- `WitchDrawer.App` owns WPF windows, view models, drag/drop, and hotkey wiring.
- `WitchDrawer.Core` owns models, SQLite persistence, file import rules, search, and safety checks.
- `WitchDrawer.Native` owns Windows integrations such as Shell open, recycle bin, and global hotkeys.
- All file mutations must flow through Core services. UI code must not directly move, delete, or rename user files.

## Performance Rules
- Do not perform file scanning, file moves, SQLite writes, icon extraction, or thumbnail generation on the UI thread.
- Keep file lists virtualized. Avoid large visual trees, real-time blur, oversized shadows, and heavy third-party UI libraries.
- Any new runtime dependency must include a short rationale for startup, memory, and background CPU cost.
- Treat 120Hz as an animation budget: UI-frame work should target an 8.33 ms frame budget.

## File Safety
- Normal boxes move files into `%LocalAppData%\WitchDrawer\Boxes\{BoxId}` only after validating the destination path.
- Mapping boxes never move, copy, or shortcut source files; they store absolute references only.
- Delete must use the recycle bin abstraction by default. Do not permanently delete user files in MVP flows.
- Name conflicts must be resolved by suffixing ` (1)`, ` (2)`, etc.

## Tests
- Changes involving file moves, name conflicts, deletion, SQLite persistence, or search must include focused tests.
- `dotnet build WitchDrawer.sln` and `dotnet test WitchDrawer.sln` should pass before handoff.

