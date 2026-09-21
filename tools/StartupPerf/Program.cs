// 启动性能对照工具（不纳入 WitchDrawer.sln）。
// 用法: dotnet run -c Release --project tools/StartupPerf -- [boxCount] [itemsPerBox]
// 在准备好的数据目录上测量：
//   1) DrawerService.InitializeAsync（含恢复日志扫描、路径修复、默认盒子检查）
//   2) 旧式逐项设置读取（模拟每个盒子 11 项 + 全局 12 项）
//   3) 新式快照读取（若当前构建提供 GetAllSettingsAsync，则通过反射调用）
//   4) 路径修复阶段的文件存在检查次数（通过环境变量开关的计数器，若可用）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var boxCount = args.Length > 0 && int.TryParse(args[0], out var b) ? b : 20;
var itemsPerBox = args.Length > 1 && int.TryParse(args[1], out var i) ? i : 30;

var root = Path.Combine(Path.GetTempPath(), "WitchDrawerStartupPerf");
if (Directory.Exists(root))
{
    Directory.Delete(root, recursive: true);
}

var paths = new AppPaths(root);
var repository = new DrawerRepository(paths.DatabasePath);
var service = new DrawerService(paths, repository);

// ---------- 准备数据 ----------
var seedWatch = Stopwatch.StartNew();
await service.InitializeAsync();
var sourceDir = Path.Combine(root, "seed-source");
Directory.CreateDirectory(sourceDir);

var boxes = new List<Box>();
for (var index = 0; index < boxCount; index++)
{
    var type = index % 5 == 4 ? BoxType.Mapping : BoxType.Normal;
    boxes.Add(await service.CreateBoxAsync($"盒子 {index}", type));
}

foreach (var box in boxes)
{
    for (var item = 0; item < itemsPerBox; item++)
    {
        var source = Path.Combine(sourceDir, $"file-{box.Id:N}-{item}.txt");
        await File.WriteAllTextAsync(source, "seed");
        await service.ImportPathAsync(box.Id, source);
    }
}

// 每个盒子 11 项盒级设置 + 若干全局设置。
string[] perBoxKeys =
[
    "BoxPreset_{0}", "BoxSizeMode:{1}", "BoxTitleVisible:{1}", "DrawerTitleVisible:{1}",
    "BoxFileNameVisible:{1}", "BoxHoverRollUpEnabled:{1}", "BoxSortMode:{1}",
    "DrawerSortMode:{1}", "DrawerCoverSize:{1}", "BoxRollUp:{1}", "BoxVisualStyle:{1}",
];
foreach (var box in boxes)
{
    foreach (var keyTemplate in perBoxKeys)
    {
        var key = string.Format(keyTemplate, box.Id, box.Id.ToString("N"));
        await service.SetSettingAsync(key, "True");
    }
}

string[] globalKeys =
[
    "Theme", "QuickPanelHotKey", "EditorFollowsBoxOpacity", "IconToolTipCompact",
    "AboutPageShown", "DesktopDoubleClickToggle", "AutoHide.Enabled",
    "AutoHide.HiddenTransparency", "AutoHide.RevealScope", "ThemeBoxOpacityVersion",
    "BoxBorderOpacity:Moe", "IconFrameOpacity:Moe",
];
foreach (var key in globalKeys)
{
    await service.SetSettingAsync(key, "True");
}
seedWatch.Stop();
Console.WriteLine($"[seed] {boxCount} 盒 × {itemsPerBox} 项 + 设置，耗时 {seedWatch.ElapsedMilliseconds} ms");

// ---------- 1) 初始化（再次启动场景） ----------
var initWatch = Stopwatch.StartNew();
await service.InitializeAsync();
initWatch.Stop();
Console.WriteLine($"[init] InitializeAsync（再次启动，{boxCount} 盒 {boxCount * itemsPerBox} 项）: {initWatch.ElapsedMilliseconds} ms");

// ---------- 2) 旧式逐项读取 ----------
var perKeyWatch = Stopwatch.StartNew();
var perKeyReads = 0;
foreach (var box in boxes)
{
    foreach (var keyTemplate in perBoxKeys)
    {
        var key = string.Format(keyTemplate, box.Id, box.Id.ToString("N"));
        _ = await service.GetSettingAsync(key);
        perKeyReads++;
    }
}
foreach (var key in globalKeys)
{
    _ = await service.GetSettingAsync(key);
    perKeyReads++;
}
perKeyWatch.Stop();
Console.WriteLine($"[settings] 逐项读取 {perKeyReads} 次: {perKeyWatch.ElapsedMilliseconds} ms");

// ---------- 3) 新式快照读取（存在时） ----------
var snapshotMethod = typeof(DrawerService).GetMethod(
    "GetAllSettingsAsync",
    BindingFlags.Public | BindingFlags.Instance);
if (snapshotMethod is null)
{
    Console.WriteLine("[settings] 快照读取: 当前构建未提供 GetAllSettingsAsync（基线）");
}
else
{
    var snapshotWatch = Stopwatch.StartNew();
    var task = (Task)snapshotMethod.Invoke(service, [CancellationToken.None])!;
    await task;
    var result = task.GetType().GetProperty("Result")!.GetValue(task);
    var count = (int)(result!.GetType().GetProperty("Count")!.GetValue(result)!);
    snapshotWatch.Stop();
    Console.WriteLine($"[settings] 快照读取 1 次（{count} 项）: {snapshotWatch.ElapsedMilliseconds} ms");
}

// ---------- 4) 首启场景（全新目录） ----------
var coldRoot = Path.Combine(Path.GetTempPath(), "WitchDrawerStartupPerfCold");
if (Directory.Exists(coldRoot))
{
    Directory.Delete(coldRoot, recursive: true);
}
var coldPaths = new AppPaths(coldRoot);
var coldService = new DrawerService(coldPaths, new DrawerRepository(coldPaths.DatabasePath));
var coldWatch = Stopwatch.StartNew();
await coldService.InitializeAsync();
coldWatch.Stop();
Console.WriteLine($"[init] InitializeAsync（首次启动，空目录）: {coldWatch.ElapsedMilliseconds} ms");

Directory.Delete(coldRoot, recursive: true);
Directory.Delete(root, recursive: true);
