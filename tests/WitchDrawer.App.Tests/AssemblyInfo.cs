// 关闭批内并行：RustDrawerService 的原生 SQLite 句柄释放依赖 GC 时机，
// 并行执行时会出现跨测试文件句柄竞争（"witchdrawer.db being used by another process"）。
// 串行执行可消除该非确定性。
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
