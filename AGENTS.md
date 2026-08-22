# WitchDrawer Agent Constraints

## Product Goal
- WitchDrawer is a lightweight Windows file drawer for daily desktop work.
- The primary UI must stay native WPF. Do not replace it with Electron or a WebView shell.
- MVP scope is limited to normal boxes, mapping boxes, and the quick panel.

## Architecture Boundaries
- `WitchDrawer.App` owns WPF windows, view models, drag/drop, and hotkey wiring.
- `WitchDrawer.Core` owns models, SQLite persistence, file import rules, search, and safety checks.
- `WitchDrawer.Native` owns Windows integrations such as Shell open and global hotkeys.
- All file mutations must flow through Core services. UI code must not directly move, delete, or rename user files.

## Performance Rules
- Do not perform file scanning, file moves, SQLite writes, icon extraction, or thumbnail generation on the UI thread.
- Keep file lists virtualized. Avoid large visual trees, real-time blur, oversized shadows, and heavy third-party UI libraries.
- Any new runtime dependency must include a short rationale for startup, memory, and background CPU cost.
- Treat 120Hz as an animation budget: UI-frame work should target an 8.33 ms frame budget.

## File Safety
- Normal boxes move files into `%LocalAppData%\WitchDrawer\Boxes\{BoxId}` only after validating the destination path.
- Mapping boxes never move, copy, or shortcut source files; they store absolute references only.
- Delete restores stored items to their original locations by default; if the original directory is missing, restore falls back to the desktop. Mapping boxes only remove references.
- File moves must support cross-volume paths (rename on same volume, copy-then-delete otherwise).
- Name conflicts must be resolved by suffixing ` (1)`, ` (2)`, etc.

## Tests
- Changes involving file moves, name conflicts, deletion, SQLite persistence, or search must include focused tests.
- `dotnet build WitchDrawer.sln` and `dotnet test WitchDrawer.sln` should pass before handoff.

## Release Process
- The release version is defined in `Directory.Build.props` (`<Version>`). Keep the README version badge and release notes aligned with it.
- Before packaging, run `dotnet build WitchDrawer.sln --configuration Release` and `dotnet test WitchDrawer.sln --configuration Release`.
- Use `tools\Publish-WitchDrawer.ps1 -Version X.Y.Z` as the single packaging entry point. It publishes `win-x64` self-contained single-file output, embeds native WPF runtime libraries with `IncludeNativeLibrariesForSelfExtract=true`, creates the portable ZIP, compiles the Inno Setup installer, and writes SHA-256 sidecars.
- The script must be able to find Inno Setup 6 under both `Program Files` and `%LOCALAPPDATA%\Programs\Inno Setup 6`. If `ISCC.exe` is unavailable, stop the release instead of publishing a portable-only release.
- Expected assets under `publish\` are:
  - `WitchDrawer-Setup-vX.Y.Z-x64.exe`
  - `WitchDrawer-Setup-vX.Y.Z-x64.exe.sha256`
  - `WitchDrawer-vX.Y.Z-win-x64.zip`
  - `WitchDrawer-vX.Y.Z-win-x64.zip.sha256`
- Validate the ZIP contents and compare each sidecar with `Get-FileHash` before uploading. The ZIP must package the complete publish directory; never manually archive only `WitchDrawer.App.exe`, because WPF native dependencies may be emitted beside it.
- For a new GitHub release, push the matching `vX.Y.Z` tag and upload all four assets. For a packaging-only correction to an existing release, use `gh release upload vX.Y.Z ... --clobber`, then verify with `gh release view vX.Y.Z --json assets,url`.
- Do not consider a release complete unless GitHub shows both an installable `Setup.exe` and a portable ZIP, each with its matching SHA-256 file. Remote release publication is an external write and requires explicit user authorization.

