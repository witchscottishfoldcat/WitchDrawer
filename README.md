<p align="center">
  <img src="https://raw.githubusercontent.com/witchscottishfoldcat/WitchDrawer/main/src/WitchDrawer.App/Assets/app.png" alt="WitchDrawer Logo" width="128" height="128" />
</p>

<h1 align="center">WitchDrawer</h1>

<p align="center">
  <img src="https://img.shields.io/badge/version-1.0.0-blue" alt="Version" />
  <img src="https://img.shields.io/badge/license-CC%20BY--NC--SA%204.0-green" alt="License" />
  <img src="https://img.shields.io/badge/.NET-10.0-purple" alt=".NET" />
  <img src="https://img.shields.io/badge/platform-Windows-blue" alt="Platform" />
</p>

WitchDrawer 是一款基于原生 WPF 构建的轻量级 Windows 桌面文件收纳工具。专为桌面美化和日常文件收纳设计：将常用文件拖入桌面小收纳盒，快速打开，让临时工作资料井然有序。

English: WitchDrawer is a lightweight Windows desktop file drawer built with native WPF. It is designed for desktop beautification and daily file staging.

## 功能特性

- **普通收纳盒** — 将拖入的文件或文件夹移入 WitchDrawer 的应用数据存储目录
- **映射收纳盒** — 仅存储绝对路径引用，源文件保留在原位
- **像素收纳盒** — 像素风格的收纳盒，为桌面增添趣味
- **桌面浮动窗口** — 每个收纳盒显示为精美的浮动桌面窗口，支持自由拖放定位
- **窗口位置记忆** — 自动记住每个收纳盒在桌面上的位置
- **系统图标** — 拖入的文件显示系统原生图标
- **拖出支持** — 可以将项目从收纳盒中拖出作为文件放置
- **跨盒拖放** — 支持在收纳盒之间拖放移动图标
- **快捷面板** — 按 `Ctrl+Alt+W` 跨所有收纳盒搜索并打开项目
- **三套主题** — 清透雅致 / 玻璃光泽 / 水晶棱镜
- **图标大小** — 超大 / 大 / 中 / 小 四档可调
- **开机自启动** — 可在设置中开启/关闭
- **检查更新** — 自动检测 GitHub Releases 新版本
- **回收站删除** — 删除操作默认使用回收站，保护用户数据
- **系统托盘** — 最小化到系统托盘，不占用任务栏
- **单实例运行** — 防止重复启动

## 技术栈

| 技术 | 说明 |
|------|------|
| .NET 10 | 运行时 |
| WPF | 原生 Windows UI |
| Win32 API | Shell 打开、回收站、全局快捷键、窗口层级 |
| SQLite | 本地持久化（WAL 模式） |
| CommunityToolkit.Mvvm | MVVM 框架 |
| xUnit | 单元测试 |

本项目有意避免使用 Electron、WebView 外壳和沉重的第三方 UI 框架。

## 仓库结构

```text
WitchDrawer.sln
src/
  WitchDrawer.App/       WPF UI、窗口、视图模型、拖放、快捷键绑定
  WitchDrawer.Core/      模型、SQLite 持久化、文件导入/删除规则、更新检查
  WitchDrawer.Native/    Shell 打开、回收站、全局快捷键、系统托盘
tests/
  WitchDrawer.Core.Tests/
```

## 环境要求

- Windows 10/11
- .NET SDK `10.0.300` 或兼容的 .NET 10 SDK

## 构建

```powershell
dotnet build WitchDrawer.sln
```

Debug 可执行文件位于：

```text
src/WitchDrawer.App/bin/Debug/net10.0-windows/WitchDrawer.App.exe
```

## 测试

```powershell
dotnet test WitchDrawer.sln
```

测试覆盖：默认收纳盒创建、普通/映射/像素盒导入、重复文件名后缀、跨盒移动、回收站删除等。

## 运行时数据

```text
%LocalAppData%\WitchDrawer\
  witchdrawer.db          SQLite 数据库
  Boxes\{BoxId}\          普通收纳盒的文件存储
  logs\                   运行日志
```

## 开源协议

本项目采用 **CC BY-NC-SA 4.0** 协议开源。

- **BY（署名）**：二次修改必须注明原作者 Thewitchcat
- **NC（非商用）**：禁止商业使用
- **SA（相同方式共享）**：衍生作品必须以相同协议开源

## 作者

- **Thewitchcat**
- 邮箱：witchscottishfoldcat@gmail.com
- 网站：[www.witchcat.cn](https://www.witchcat.cn)
- GitHub：[witchscottishfoldcat/WitchDrawer](https://github.com/witchscottishfoldcat/WitchDrawer)
