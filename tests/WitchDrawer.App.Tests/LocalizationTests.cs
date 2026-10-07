using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Localization;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Localization;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Tests;

[Collection("AppThemeManager")]
public sealed class LocalizationTests : IDisposable
{
    private readonly string _previousLanguage = Strings.Culture.Name;
    public void Dispose() => LocalizationProvider.Instance.Apply(_previousLanguage);

    [Fact]
    public async Task SwitchingLanguage_UpdatesExistingPresentationAndSavesChoice()
    {
        LocalizationProvider.Instance.Apply(AppLanguage.SimplifiedChinese);
        var store = new MemorySettings();
        var operations = new UiOperationState(NullAppLogger.Instance);
        var viewModel = CreateSettings(store, operations);
        var item = new DrawerItemViewModel(new DrawerItem(Guid.NewGuid(), Guid.NewGuid(),
            "中文文件.txt", ItemKind.File, null, null, 0, DateTimeOffset.Now, DateTimeOffset.Now));
        var color = viewModel.ThemeColors[0];
        var existingChoice = BoxDisplaySettingsView.SizeModeChoices[0];
        var notifications = new List<string?>();
        item.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        await viewModel.ChangeLanguageCommand.ExecuteAsync(AppLanguage.English);

        Assert.Equal(AppLanguage.English, await store.GetSettingAsync(AppLanguage.SettingKey));
        Assert.True(viewModel.IsEnglish);
        Assert.Equal("File", item.KindLabel);
        Assert.Equal("中文文件.txt", item.Model.DisplayName);
        Assert.Equal("Box background", color.Label);
        Assert.Equal("Auto size", existingChoice.Label);
        Assert.Contains(string.Empty, notifications);
        Assert.Equal("Language updated", operations.StatusText);
        Assert.DoesNotContain("清透", viewModel.ThemeLabel);

        await viewModel.ChangeLanguageCommand.ExecuteAsync(AppLanguage.SimplifiedChinese);
        Assert.Equal("文件", item.KindLabel);
        Assert.Equal("盒子背景", color.Label);
        Assert.True(viewModel.IsSimplifiedChinese);
    }

    [Fact]
    public async Task StartupSnapshot_RestoresSavedLanguage()
    {
        LocalizationProvider.Instance.Apply(AppLanguage.SimplifiedChinese);
        var store = new MemorySettings();
        var viewModel = CreateSettings(store, new UiOperationState(NullAppLogger.Instance));
        await viewModel.LoadAsync(new StartupSettingsSnapshot(new Dictionary<string, string>
        {
            [AppLanguage.SettingKey] = AppLanguage.English
        }));
        Assert.True(viewModel.IsEnglish);
        Assert.Equal(0, store.ReadCount);
    }

    [Fact]
    public async Task FailedLanguageWrite_KeepsCurrentLanguage()
    {
        LocalizationProvider.Instance.Apply(AppLanguage.SimplifiedChinese);
        var store = new MemorySettings { RejectLanguageWrites = true };
        var operations = new UiOperationState(NullAppLogger.Instance);
        var viewModel = CreateSettings(store, operations);
        await viewModel.ChangeLanguageCommand.ExecuteAsync(AppLanguage.English);
        Assert.True(viewModel.IsSimplifiedChinese);
        Assert.Null(await store.GetSettingAsync(AppLanguage.SettingKey));
        Assert.Equal("语言设置保存失败", operations.StatusText);
    }

    [Fact]
    public Task TranslationBindings_RefreshTextFormatsAndNullFallbacks() => OnStaAsync(() =>
    {
        LocalizationProvider.Instance.Apply(AppLanguage.SimplifiedChinese);
        var panel = (StackPanel)XamlReader.Parse("""
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                        xmlns:loc="clr-namespace:WitchDrawer.App.Localization;assembly=WitchDrawer.App">
                <TextBlock Text="{loc:Text Settings}" />
                <TextBlock Text="{loc:Text Items, Value={Binding Count}}" />
                <TextBlock Text="{loc:Text SelectABox, Value={Binding Name}, Fallback=True}" />
            </StackPanel>
            """);
        panel.DataContext = new { Count = 3, Name = (string?)null };
        DrainBindings();
        var texts = panel.Children.Cast<TextBlock>().ToArray();
        Assert.Equal(new[] { "设置", "3 项", "选择一个收纳盒" }, texts.Select(text => text.Text));
        LocalizationProvider.Instance.Apply(AppLanguage.English);
        DrainBindings();
        Assert.Equal(new[] { "Settings", "3 items", "Select a box" }, texts.Select(text => text.Text));
        // A binding supplying a real box name must preserve user text.
        panel.DataContext = new { Count = 5, Name = "自定义盒子" };
        DrainBindings();
        Assert.Equal("自定义盒子", texts[2].Text);
    });

    [Fact]
    public void EveryXamlTranslation_HasBothLanguageResources()
    {
        var appSource = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/WitchDrawer.App"));
        foreach (var file in Directory.EnumerateFiles(appSource, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\{loc:Text (\w+)"))
            {
                Assert.NotEmpty(Strings.Get(match.Groups[1].Value, CultureInfo.GetCultureInfo("en")));
                Assert.NotEmpty(Strings.Get(match.Groups[1].Value, CultureInfo.GetCultureInfo("zh-CN")));
            }
        }
    }

    private static SettingsViewModel CreateSettings(MemorySettings store, UiOperationState operations) =>
        new(store, NullAppLogger.Instance, new DesktopAdapter(), new AutoHideSettingsStore(store), operations);

    private static void DrainBindings() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

    private static Task OnStaAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private sealed class DesktopAdapter : IDesktopIntegration
    {
        public Task<bool> IsStartupEnabledAsync() => Task.FromResult(false);
        public Task SetStartupEnabledAsync(bool enabled) => Task.CompletedTask;
        public Task<bool> AreDesktopIconsHiddenAsync() => Task.FromResult(false);
        public Task<bool> ToggleDesktopIconsAsync() => Task.FromResult(false);
    }

    private sealed class MemorySettings : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = [];
        public bool RejectLanguageWrites { get; init; }
        public int ReadCount { get; private set; }
        public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
        { ReadCount++; return Task.FromResult(_values.GetValueOrDefault(key)); }
        public Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(_values));
        public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            if (key == AppLanguage.SettingKey && RejectLanguageWrites) throw new IOException("Write failed.");
            _values[key] = value;
            return Task.CompletedTask;
        }
        public Task<bool> DeleteSettingAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.Remove(key));
    }
}
