<p align="center">
  <img src="https://raw.githubusercontent.com/witchscottishfoldcat/WitchDrawer/main/src/WitchDrawer.App/Assets/app.png" alt="WitchDrawer Logo" width="128" height="128" />
</p>

<h1 align="center">WitchDrawer</h1>

WitchDrawer is a lightweight Windows desktop file drawer built with native WPF. It is designed for desktop beautification and daily file staging: drag common files into small desktop drawers, open them quickly, and keep temporary work material organized without turning the UI into a heavy Electron/WebView app.

WitchDrawer 是一款基于原生 WPF 构建的轻量级 Windows 桌面文件抽屉。专为桌面美化和日常文件收纳设计：将常用文件拖入桌面小抽屉，快速打开，让临时工作资料井然有序，而无需使用沉重的 Electron/WebView 应用。

> Current status: MVP prototype. / 当前状态：MVP 原型。

## Features / 功能特性

- **Normal box / 普通抽屉**: moves dropped files or folders into WitchDrawer's app-data storage. / 将拖入的文件或文件夹移入 WitchDrawer 的应用数据存储目录。
- **Mapping box / 映射抽屉**: stores absolute path references only; source files stay where they are. / 仅存储绝对路径引用，源文件保留在原位。
- **Desktop drawer windows / 桌面抽屉窗口**: each box appears as a clean floating desktop drawer. They are not topmost, so normal application windows can cover them. / 每个抽屉显示为干净的浮动桌面窗口。它们不是置顶窗口，普通应用窗口可以覆盖它们。
- **File icons / 文件图标**: dropped files show system-style icons when available. / 拖入的文件在可用时显示系统风格图标。
- **Drag out / 拖出**: items can be dragged back out from a drawer as file drops. / 可以将项目从抽屉中拖出作为文件放置。
- **Delete item / 删除项目**: select an item and press `Delete`, or use the main window remove action. / 选中项目后按 `Delete` 键，或使用主窗口的移除操作。
- **Delete box / 删除抽屉**: the main page can delete the selected box. / 主页面可以删除选中的抽屉。
- **Quick panel / 快捷面板**: press `Ctrl+Alt+W` to search and open items across all boxes. / 按 `Ctrl+Alt+W` 跨所有抽屉搜索并打开项目。
- **Themes / 主题**: includes a clean style and a glass-style theme. / 包含简洁风格和毛玻璃风格主题。

## MVP Scope / MVP 范围

Implemented in the first round / 首轮已实现：

- Normal boxes / 普通抽屉
- Mapping boxes / 映射抽屉
- Desktop drawer windows / 桌面抽屉窗口
- Quick panel / 快捷面板
- SQLite persistence / SQLite 持久化
- Recycle-bin based delete flow / 基于回收站的删除流程

Not implemented yet / 尚未实现：

- Target boxes bound to existing folders with two-way sync / 绑定已有文件夹并支持双向同步的目标抽屉
- Magnetic access window beside open/save dialogs / 附着在打开/保存对话框旁的磁性访问窗口
- Installer / 安装程序
- Tray icon and auto-start settings / 托盘图标和开机自启设置
- Thumbnail cache and advanced file previews / 缩略图缓存和高级文件预览

## Tech Stack / 技术栈

- .NET 10
- WPF
- Win32 API wrappers / Win32 API 封装
- SQLite
- CommunityToolkit.Mvvm
- xUnit

The project intentionally avoids Electron, WebView shells, and heavy third-party UI frameworks. / 本项目有意避免使用 Electron、WebView 外壳和沉重的第三方 UI 框架。

## Repository Layout / 仓库结构

```text
WitchDrawer.sln
src/
  WitchDrawer.App/       WPF UI, windows, view models, drag/drop, hotkey wiring / WPF UI、窗口、视图模型、拖放、快捷键绑定
  WitchDrawer.Core/      models, SQLite persistence, file import/delete rules / 模型、SQLite 持久化、文件导入/删除规则
  WitchDrawer.Native/    Shell open, recycle bin, global hotkeys / Shell 打开、回收站、全局快捷键
tests/
  WitchDrawer.Core.Tests/
docs/
  ARCHITECTURE.md
  PROJECT_PLAN.md
```

## Requirements / 环境要求

- Windows 10/11
- .NET SDK `10.0.300` or compatible .NET 10 SDK / 或兼容的 .NET 10 SDK

The SDK version is locked by `global.json`. / SDK 版本由 `global.json` 锁定。

## Build / 构建

```powershell
dotnet build WitchDrawer.sln
```

If `dotnet` is not on `PATH` but the local SDK is installed under the current user / 如果 `dotnet` 不在 `PATH` 中，但本地 SDK 已安装在当前用户目录下：

```powershell
C:\Users\Administrator\.dotnet\dotnet.exe build WitchDrawer.sln
```

The debug executable is generated at / Debug 可执行文件生成于：

```text
src/WitchDrawer.App/bin/Debug/net10.0-windows/WitchDrawer.App.exe
```

## Test / 测试

```powershell
dotnet test WitchDrawer.sln
```

Current focused tests cover / 当前重点测试覆盖：

- default box creation / 默认抽屉创建
- normal-box file move / 普通抽屉文件移动
- mapping-box reference import / 映射抽屉引用导入
- duplicate file-name suffixing / 重复文件名后缀处理
- item delete through trash abstraction / 通过回收站抽象删除项目
- normal-box delete through trash abstraction / 通过回收站抽象删除普通抽屉
- mapping-box delete without touching source files / 删除映射抽屉时不触碰源文件

## Runtime Data / 运行时数据

WitchDrawer stores app data under / WitchDrawer 将应用数据存储在：

```text
%LocalAppData%\WitchDrawer
```

Important paths / 重要路径：

```text
%LocalAppData%\WitchDrawer\witchdrawer.db
%LocalAppData%\WitchDrawer\Boxes\{BoxId}
%LocalAppData%\WitchDrawer\logs
```

Normal boxes move files into `Boxes\{BoxId}`. Mapping boxes only store references in SQLite. / 普通抽屉将文件移入 `Boxes\{BoxId}`。映射抽屉仅在 SQLite 中存储引用。

## File Safety Rules / 文件安全规则

- Normal boxes validate the target path before moving files. / 普通抽屉在移动文件前会验证目标路径。
- Name conflicts are resolved as `name (1).ext`, `name (2).ext`, etc. / 名称冲突通过添加 ` (1)`、` (2)` 等后缀解决。
- Mapping boxes never move, copy, or shortcut source files. / 映射抽屉永不移动、复制或创建源文件的快捷方式。
- Delete operations use the recycle bin abstraction by default. / 删除操作默认使用回收站抽象。
- UI code should not directly mutate user files; file changes should flow through `WitchDrawer.Core`. / UI 代码不应直接修改用户文件；文件变更应通过 `WitchDrawer.Core` 执行。

## Performance Rules / 性能规则

- No file scanning, file moving, SQLite writes, icon extraction, or thumbnail work on the UI thread. / 禁止在 UI 线程上进行文件扫描、文件移动、SQLite 写入、图标提取或缩略图生成。
- File lists must stay virtualized. / 文件列表必须保持虚拟化。
- Animations should use `Opacity` and `Transform`. / 动画应使用 `Opacity` 和 `Transform`。
- Avoid real-time blur, oversized shadows, and large visual trees. / 避免实时模糊、过大阴影和大型可视化树。
- Target idle CPU is near `0%`; target idle memory is under `150 MB` where practical. / 目标空闲 CPU 接近 `0%`；目标空闲内存在可行范围内低于 `150 MB`。

## Development Notes / 开发说明

- Read `AGENTS.md` before modifying the project. / 修改项目前请阅读 `AGENTS.md`。
- Keep WPF as the primary UI. / 保持 WPF 作为主要 UI。
- Add focused tests when changing file movement, deletion, persistence, or search behavior. / 更改文件移动、删除、持久化或搜索行为时，请添加针对性测试。
- Run both build and test before handing off changes / 交付变更前请同时运行构建和测试：

```powershell
dotnet build WitchDrawer.sln
dotnet test WitchDrawer.sln
```

## Known Limitations / 已知限制

- Desktop drawers are regular non-topmost WPF windows. They are meant to sit on the desktop and be covered by normal apps, but they are not currently embedded into Explorer's desktop icon layer. / 桌面抽屉是普通的非置顶 WPF 窗口，设计为放置在桌面上并被普通应用覆盖，但目前尚未嵌入到资源管理器的桌面图标层。
- The glass theme is implemented with lightweight translucent WPF surfaces; it avoids heavy runtime blur for performance. / 毛玻璃主题使用轻量半透明 WPF 表面实现；为保持性能，避免了沉重的运行时模糊效果。
- Quick panel is intentionally topmost because it is a temporary hotkey-driven access panel. / 快捷面板有意设为置顶，因为它是临时的快捷键驱动访问面板。
