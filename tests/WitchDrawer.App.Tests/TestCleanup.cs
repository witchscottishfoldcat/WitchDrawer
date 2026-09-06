namespace WitchDrawer.App.Tests;

using System.IO;

public static class TestCleanup
{
    /// <summary>
    /// 删除临时根目录。SQLite 连接关闭与文件释放存在毫秒级窗口，
    /// 用 GC 强制终结 + 重试确保在并行/快速执行的测试进程中确定性地删除根目录。
    /// </summary>
    public static void DeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 19)
            {
                if (attempt == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

                Thread.Sleep(150);
            }
        }
    }
}
