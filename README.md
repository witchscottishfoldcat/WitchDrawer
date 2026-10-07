<p align="center">
  <img src="src/WitchDrawer.App/Assets/app.png" alt="WitchDrawer" width="128" height="128" />
</p>

<h1 align="center">WitchDrawer</h1>

<p align="center">
  <img src="https://img.shields.io/badge/version-1.4.3-blue" alt="版本 1.4.3" />
  <img src="https://img.shields.io/badge/platform-Windows%20x64-blue" alt="Windows x64" />
  <img src="https://img.shields.io/badge/.NET-10.0-purple" alt=".NET 10" />
</p>

<p align="center">
  简体中文 · <a href="README.en.md">English</a> · <a href="https://github.com/witchscottishfoldcat/WitchDrawer/releases/latest">下载</a> · <a href="docs/releases/v1.4.3.md">更新记录</a>
</p>

WitchDrawer 是基于原生 WPF 的轻量级 Windows 桌面文件收纳工具。把常用文件放进桌面收纳盒，通过拖放和快捷搜索整理、打开文件。

[![WitchDrawer 桌面效果展示](docs/images/witchdrawer-desktop-showcase.png)](https://www.bilibili.com/video/BV1zx3c6eEX8/)

[观看视频演示](https://www.bilibili.com/video/BV1zx3c6eEX8/)

## 下载与运行

推荐使用 **Windows 11 x64**；Windows 10 部分功能可能不兼容。

在 [GitHub Releases](https://github.com/witchscottishfoldcat/WitchDrawer/releases/latest) 下载：

- **安装版**：运行 `WitchDrawer-Setup-vX.Y.Z-x64.exe`，按向导安装。
- **便携版**：完整解压 `WitchDrawer-vX.Y.Z-win-x64.zip`，运行 `WitchDrawer.App.exe`。

两种版本均自包含，无需另装 .NET 运行时，并分别提供独立的 `.sha256` 校验文件。

## 主要功能

| 收纳盒 | 用途 |
| --- | --- |
| 普通收纳盒 | 管理实际存入应用数据目录的文件，支持普通与像素样式 |
| 映射收纳盒 | 保存文件或文件夹的绝对路径引用，保留源文件位置 |
| 抽屉收纳盒 | 与普通盒相同的文件存储方式，提供可展开的文件面板 |
| 待办收纳盒 | 添加、编辑、完成和归档事项，支持完成率统计 |

- **快捷面板**：跨盒搜索文件项，支持按名称、路径和盒名筛选。
- **文件操作**：拖入、拖出、跨盒拖放；右键复制、粘贴、重命名、复制路径及定位快捷方式目标。
- **外观设置**：三套主题，可调透明度、颜色、圆角与图标大小，支持自动隐藏。
- **桌面管理**：记忆盒子位置；普通/映射盒可卷起；支持隐藏桌面图标及双击桌面空白处切换。
- **日常运行**：系统托盘、开机自启动、更新检查与诊断日志导出。

## 开始使用

1. 启动应用，在主页创建收纳盒。项目目录或需要保留原位置的文件，优先使用**映射收纳盒**。
2. 将文件或文件夹拖入盒子，双击图标打开。
3. 按 `Ctrl+Alt+W` 打开快捷面板，搜索并打开文件；快捷键可在设置中修改。

桌面拖放请以普通权限启动；以管理员身份运行时，Windows 会限制从普通权限的资源管理器或桌面拖入文件。

### 文件操作规则

| 操作 | 普通盒（含像素样式）与抽屉盒 | 映射盒 |
| --- | --- | --- |
| 从资源管理器拖入 | **移动**文件或文件夹到数据目录 | 仅添加路径引用 |
| 粘贴文件 | 创建副本，保留源文件 | 仅添加路径引用 |
| 重命名 | 修改存储文件的名称，还原时使用新名称 | 仅修改引用的显示名称 |
| 删除文件项或盒子 | 将存储文件还原到原目录；原目录不存在时回退到桌面 | 仅移除引用 |

存储或还原文件时，重名自动添加 ` (1)`、` (2)` 等后缀。映射引用不会跟踪源文件在外部的移动；源路径失效时无法打开。

待办单项删除可在 **10 秒内**撤销，仅在应用运行期间有效；删除整个待办盒会同时删除归档历史，无法撤销。完成率统计当前盒内全部未归档事项，不按日期筛选。

### 常用快捷键

以下文件快捷键在桌面文件盒中使用；复制、重命名和复制路径需要先选中文件项。

| 快捷键 | 操作 |
| --- | --- |
| `Ctrl+C` / `Ctrl+V` | 复制 / 粘贴文件 |
| `Ctrl+Shift+C` | 复制当前文件路径 |
| `F2` | 重命名 |
| `Delete` | 移除文件项，按上表执行还原或移除引用 |

## 数据位置

默认数据目录为 `%LocalAppData%\WitchDrawer\`，便携版也使用此目录：

```text
witchdrawer.db     SQLite 数据库
Boxes\{BoxId}\     普通盒（含像素样式）与抽屉盒的文件
logs\             运行日志
```

可在设置的“数据存储位置”中迁移目录，重启后生效，原目录保留为备份。环境变量 `WITCHDRAWER_DATA_DIR` 可覆盖数据目录，优先于设置。

## 开发与构建

需要 Windows 和 .NET SDK `10.0.300` 或兼容的 .NET 10 SDK（见 [global.json](global.json)）。在仓库根目录执行：

```powershell
dotnet build WitchDrawer.sln
dotnet test WitchDrawer.sln
.\dev.ps1
```

`dev.ps1` 使用 Debug 配置启动应用；`build.ps1` 使用 Release 配置构建解决方案。

- `src/WitchDrawer.App`：WPF 窗口、MVVM、拖放与快捷键接线。
- `src/WitchDrawer.Core`：模型、SQLite 持久化、搜索与文件安全操作。
- `src/WitchDrawer.Native`：Win32 Shell、全局快捷键与桌面集成。
- `tests/`：App、Core、Native 测试。

打包需要 Inno Setup 6。版本以 [Directory.Build.props](Directory.Build.props) 为准：

```powershell
dotnet build WitchDrawer.sln --configuration Release
dotnet test WitchDrawer.sln --configuration Release
.\tools\Publish-WitchDrawer.ps1 -Version 1.4.3
```

脚本在 `publish/` 生成 Windows x64 自包含安装包、完整便携 ZIP 及各自的 SHA-256 文件；缺少 Inno Setup 编译器时会报错。发布前验证 ZIP 完整解压后可启动、校验文件与 `Get-FileHash` 一致，并上传全部四个文件。

## 反馈与支持

遇到问题请提交 [Issue](https://github.com/witchscottishfoldcat/WitchDrawer/issues)，附应用版本、Windows 版本及复现步骤；可从“关于”页导出诊断日志，日志包不包含数据库或用户文件内容。

作者：**Thewitchcat** · [网站](https://www.witchcat.cn) · [邮箱](mailto:witchscottishfoldcat@gmail.com) · [赞助](https://www.witchcat.cn/zh/support)（备注 ID 可加入鸣谢名单）

## 许可证

- **代码、构建脚本与配置**：[PolyForm Noncommercial 1.0.0](LICENSE)。
- **文档与媒体素材**（含 `docs/` 和 `src/WitchDrawer.App/Assets/`）：[CC BY-NC-SA 4.0](LICENSE-DOCS)。

两者均为非商业许可，具体权利与限制以许可证全文为准。
