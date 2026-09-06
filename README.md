<p align="center">
  <img src="https://raw.githubusercontent.com/witchscottishfoldcat/WitchDrawer/main/src/WitchDrawer.App/Assets/app.png" alt="WitchDrawer Logo" width="128" height="128" />
</p>

<h1 align="center">WitchDrawer</h1>

<p align="center">
  <img src="https://img.shields.io/badge/version-1.3.10-blue" alt="Version" />
  <img src="https://img.shields.io/badge/license-CC%20BY--NC--SA%204.0-green" alt="License" />
  <img src="https://img.shields.io/badge/.NET-10.0-purple" alt=".NET" />
  <img src="https://img.shields.io/badge/platform-Windows-blue" alt="Platform" />
</p>

WitchDrawer 是一款基于原生 WPF 构建的轻量级 Windows 桌面文件收纳工具。专为桌面美化和日常文件收纳设计：将常用文件拖入桌面小收纳盒，快速打开，让临时工作资料井然有序。

English: WitchDrawer is a lightweight Windows desktop file drawer built with native WPF. It is designed for desktop beautification and daily file staging.

## 效果展示

[![WitchDrawer 桌面收纳效果展示](docs/images/witchdrawer-desktop-showcase.png)](https://www.bilibili.com/video/BV1zx3c6eEX8/)

▶ [在哔哩哔哩观看 WitchDrawer 视频演示](https://www.bilibili.com/video/BV1zx3c6eEX8/)

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
- **原位还原删除** — 删除收纳项或收纳盒时，普通/像素盒文件恢复到原来的位置；原位置不可用则回退到桌面，重名自动加后缀；映射盒只删除引用
- **系统托盘** — 最小化到系统托盘，不占用任务栏
- **单实例运行** — 防止重复启动

## 技术栈

| 技术 | 说明 |
|------|------|
| .NET 10 | 运行时 |
| WPF | 原生 Windows UI |
| Rust | 生产内核：SQLite、文件操作、搜索、待办与更新逻辑 |
| Win32 API | Shell 打开、全局快捷键、窗口层级 |
| SQLite | 本地持久化（WAL 模式） |
| CommunityToolkit.Mvvm | MVVM 框架 |
| xUnit / cargo test | 单元测试 |

本项目有意避免使用 Electron、WebView 外壳和沉重的第三方 UI 框架。

## 性能对比：Rust 内核 vs 原版 .NET 内核

> 实测方法：同一台机器（Windows 10 / .NET 10 / x64 Release），两版均以 `--silent` 启动，12 秒稳定后采样，各 3 次取中位数。原版为上游 `main`（纯 C# 内核），对比版为 Rust 内核（WPF 壳不变，仅替换 Core 实现）。

| 指标 | 原版 .NET 内核 | Rust 内核 | 差异 |
|---|---:|---:|---:|
| 空闲工作集 | 184.8 MB | 180.0 MB | **-2.6%**（省 4.8 MB） |
| 空闲私有内存 | 118.1 MB | 121.5 MB | +2.9%（多 3.4 MB） |
| 冷启动完成 | 1823.55 ms | 1724.85 ms | **-5.4%**（7/28 基准） |

### 结论

- **启动更快**：Rust 内核冷启动比原版快约 5.4%。
- **工作集略降**：整体物理占用小幅减少 2.6%。
- **私有内存持平略升**：WPF 框架自身占用约 88.7 MB 私有内存（架构固定成本），Rust 内核的 SQLite 原生分配使私有内存微升 2.9%，未实现私有内存下降。内存优化空间主要在 WPF 壳层，而非内核。

## 仓库结构

```text
WitchDrawer.sln
src/
  WitchDrawer.App/         WPF UI、窗口、视图模型、拖放、快捷键绑定
  WitchDrawer.Core/        模型、服务契约、Rust FFI/异步适配、文件与数据内核入口
  WitchDrawer.Native/      Shell 打开、全局快捷键、系统托盘
rust/
  witchdrawer-core/        Core 的生产 Rust 实现（SQLite、业务规则、文件安全）
tests/
  WitchDrawer.Core.Tests/
  WitchDrawer.App.Tests/
  WitchDrawer.Core.Native.Tests/
benchmarks/
  BenchDotnet/             .NET 内存 benchmark
  WitchDrawer.LegacyCore/  仅用于迁移对比的原 C# 内核
installer/
  WitchDrawer.iss          Inno Setup 安装脚本
build.ps1                  一键构建脚本（cargo + dotnet）
```

## 环境要求

- Windows 10/11
- .NET SDK `10.0.300` 或兼容的 .NET 10 SDK
- Rust toolchain stable（生产应用构建必需）

## 构建

一键构建（推荐）：

```powershell
.\build.ps1 -Release
```

手动分步构建：

```powershell
# 1. 构建 Rust DLL
cd rust\witchdrawer-core
cargo build --release
cd ..\..

# 2. 构建 .NET 解决方案
dotnet build WitchDrawer.sln -c Release -p:SkipRustBuild=true
```

Debug 可执行文件位于：

```text
src/WitchDrawer.App/bin/Debug/net10.0-windows/WitchDrawer.App.exe
```

## 测试

```powershell
# .NET 测试
dotnet test WitchDrawer.sln

# Rust 测试
cd rust\witchdrawer-core && cargo test --lib
```

测试覆盖：默认收纳盒创建、普通/映射/像素盒导入、重复文件名后缀、跨盒移动、原位还原删除、更新 URL 校验，以及真实 .NET → P/Invoke → Rust 的 UTF-8、导入和恢复流程。

## Rust 内核状态与成本

生产应用只依赖 `WitchDrawer.Core` 和 `WitchDrawer.Native`。Core 的异步服务通过内部 P/Invoke 调用 Rust；SQLite、搜索、待办、更新和所有文件变更均由 Rust 执行。原 C# 实现仅保留在 benchmark 项目，不进入 App 输出。

`witchdrawer_core.dll` release 约 6.0 MiB。`rusqlite` 的 bundled SQLite 避免系统 DLL 版本差异；Serde/JSON、UUID、Chrono、Hex 和 Tracing 只做数据/FFI 辅助，不创建后台线程；Tokio、Reqwest/rustls、SHA-256 与 ZIP 只在检查或应用更新时按需工作。Core 将阻塞原生调用放到工作线程并串行化数据库/文件变更，空闲时不轮询、不保留异步运行时。迁移后的真实启动与内存结果记录在性能报告中；当前完整启动更快，但稳定空闲内存略有增加。

## 基准

```powershell
dotnet run -c Release --project benchmarks\BenchDotnet\BenchDotnet.csproj
cd rust\witchdrawer-core
cargo run --release --example rust-bench
```

同负载 C# Core / Rust Core 对比方法与最近一次结果见
[`benchmarks/RESULTS.md`](benchmarks/RESULTS.md)。

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
