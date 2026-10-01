using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task IndependentSettingsStore_PersistsReadsAndDeletesAcrossInstances()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Settings", Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new DrawerRepository(Path.Combine(root, "settings.db"));
            await repository.InitializeAsync();
            ISettingsStore settings = new SettingsService(repository);
            await settings.SetSettingAsync("透明度", "0.65");
            await settings.SetSettingAsync("主题", "Crystal");
            ISettingsStore reloaded = new SettingsService(new DrawerRepository(Path.Combine(root, "settings.db")));
            Assert.Equal("0.65", await reloaded.GetSettingAsync("透明度"));
            Assert.Equal(2, (await reloaded.GetAllSettingsAsync()).Count);
            Assert.True(await reloaded.DeleteSettingAsync("透明度"));
            Assert.False(await reloaded.DeleteSettingAsync("透明度"));
            Assert.Null(await settings.GetSettingAsync("透明度"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task OrderedWritesAndCancellation_KeepTheLatestSuccessfulValue()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Settings", Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new DrawerRepository(Path.Combine(root, "settings.db"));
            await repository.InitializeAsync();
            var settings = new SettingsService(repository);
            var writes = Enumerable.Range(0, 25).Select(value => settings.SetSettingAsync("value", value.ToString())).ToArray();
            await Task.WhenAll(writes);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => settings.SetSettingAsync("value", "canceled", canceled.Token));
            Assert.Equal("24", await settings.GetSettingAsync("value"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SettingsStore_SharesTheRepositoryMigrationGuard()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Settings", Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new DrawerRepository(Path.Combine(root, "source.db"));
            await repository.InitializeAsync();
            var settings = new SettingsService(repository);
            await settings.SetSettingAsync("key", "original");
            await repository.CopyConsistentDatabaseAsync(Path.Combine(root, "copy.db"),
                () => Task.CompletedTask, () => Task.CompletedTask,
                async () =>
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(() => settings.SetSettingAsync("key", "late"));
                    await Assert.ThrowsAsync<InvalidOperationException>(() => settings.GetSettingAsync("key"));
                });
            Assert.Equal("original", await settings.GetSettingAsync("key"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
