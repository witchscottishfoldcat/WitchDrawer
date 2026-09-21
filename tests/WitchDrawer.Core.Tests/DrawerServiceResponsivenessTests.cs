using Microsoft.Data.Sqlite;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class DrawerServiceResponsivenessTests
{
    [Theory]
    [InlineData("setting")]
    [InlineData("delete-setting")]
    [InlineData("create")]
    [InlineData("rename")]
    [InlineData("reorder")]
    [InlineData("delete-item")]
    [InlineData("delete-box")]
    public async Task DatabaseWriteContention_DoesNotBlockCallingThread(string operation)
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerResponsiveness", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var service = new DrawerService(paths, new DrawerRepository(paths.DatabasePath));
            await service.InitializeAsync();
            await service.SetSettingAsync("position", "old");
            var boxes = await service.GetBoxesAsync();
            var mapping = boxes.Single(box => box.Type == BoxType.Mapping);
            var source = Path.Combine(root, "reference.txt");
            await File.WriteAllTextAsync(source, "reference stays in place");
            var item = await service.ImportPathAsync(mapping.Id, source);
            Func<Task> invoke = operation switch
            {
                "setting" => () => service.SetSettingAsync("position", "new"),
                "delete-setting" => () => service.DeleteSettingAsync("position"),
                "create" => () => service.CreateBoxAsync("new", BoxType.Normal),
                "rename" => () => service.RenameBoxAsync(mapping.Id, "renamed"),
                "reorder" => () => service.ReorderBoxesAsync(boxes.Reverse().Select(box => box.Id).ToArray()),
                "delete-item" => () => service.DeleteItemAsync(item.Id),
                "delete-box" => () => service.DeleteBoxAsync(mapping.Id),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };

            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = paths.DatabasePath,
                Pooling = false
            }.ToString());
            connection.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            var caller = new Thread(() =>
            {
                try { returned.SetResult(invoke()); }
                catch (Exception exception) { returned.SetException(exception); }
            }) { IsBackground = true };
            caller.Start();

            var releasedCaller = false;
            var writePending = false;
            try
            {
                releasedCaller = await Task.WhenAny(returned.Task, Task.Delay(1000)) == returned.Task;
                if (releasedCaller)
                {
                    writePending = !(await returned.Task).IsCompleted;
                }
            }
            finally
            {
                transaction.Rollback();
            }

            await (await returned.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(releasedCaller, $"{operation} blocked its caller while SQLite held a write lock.");
            Assert.True(writePending, "The write must remain pending until the competing transaction releases its lock.");
            Assert.True(File.Exists(source));
            if (operation == "setting")
                Assert.Equal("new", await service.GetSettingAsync("position"));
            if (operation == "delete-setting")
                Assert.Null(await service.GetSettingAsync("position"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SettingsWrites_PreserveInvocationOrderAndRecoverAfterCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawerResponsiveness", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var service = new DrawerService(paths, new DrawerRepository(paths.DatabasePath));
            await service.InitializeAsync();
            var writes = Enumerable.Range(0, 50)
                .Select(index => service.SetSettingAsync("position", index.ToString())).ToArray();
            await Task.WhenAll(writes);
            Assert.Equal("49", await service.GetSettingAsync("position"));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.SetSettingAsync("position", "cancelled", new CancellationToken(true)));
            var first = service.SetSettingAsync("position", "before-delete");
            var deletion = service.DeleteSettingAsync("position");
            var last = service.SetSettingAsync("position", "after-delete");
            await Task.WhenAll(first, deletion, last);
            Assert.True(await deletion);
            Assert.Equal("after-delete", await service.GetSettingAsync("position"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
