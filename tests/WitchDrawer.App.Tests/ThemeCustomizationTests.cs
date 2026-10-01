using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core;
using WitchDrawer.Core.Storage;
using WitchDrawer.Native.Files;

namespace WitchDrawer.App.Tests;

[Collection("AppThemeManager")]
public sealed class ThemeCustomizationTests
{
    [Theory]
    [InlineData(AppTheme.Moe, "#FFFFFFFF", "#FFF5F5F7", "#FFEBEBF0", "#FFE7F1FF")]
    [InlineData(AppTheme.Glass, "#D12C2C2E", "#1FFFFFFF", "#2E3A3A3C", "#330A84FF")]
    [InlineData(AppTheme.Crystal, "#66FFFFFF", "#3DFFFFFF", "#52FFFFFF", "#2E0071E3")]
    public void OriginalPresetsAndReset_ReproduceExistingColorsAndModeRadii(
        AppTheme theme, string surface, string frame, string hover, string selected)
    {
        var previous = AppThemeManager.CurrentTheme;
        AppThemeManager.ResetBoxOpacitiesForTests();
        try
        {
            AppThemeManager.Apply(theme);
            var before = new ResourceDictionary();
            AppThemeManager.ApplyDesktopBoxResources(before);
            Assert.Equal(Parse(surface), ((SolidColorBrush)before["GlassSurfaceBrush"]).Color);
            Assert.Equal(Parse(frame), ((SolidColorBrush)before["DesktopIconFrameBrush"]).Color);
            Assert.Equal(Parse(hover), ((SolidColorBrush)before["ItemHoverBrush"]).Color);
            Assert.Equal(Parse(selected), ((SolidColorBrush)before["ItemSelectedBrush"]).Color);
            foreach (var (key, radius) in new (string, double)[] { ("BoxWindowRadius", 20), ("BoxPixelWindowRadius", 14),
                ("BoxCollapsedWindowRadius", 24), ("BoxSurfaceRadius", 18), ("BoxPixelSurfaceRadius", 12),
                ("BoxCollapsedSurfaceRadius", 22), ("BoxPopupRadius", 24), ("BoxPopupSurfaceRadius", 23),
                ("BoxPopupInnerRadius", 22), ("PixelItemRadius", 6), ("PixelIconRadius", 4), ("MappingItemRadius", 4) })
                Assert.Equal(new CornerRadius(radius), typeof(DesktopBoxCornerSettings).GetProperty(key)!.GetValue(AppThemeManager.DesktopCorners));
            AppThemeManager.SetCustomization(theme, new ThemeCustomization { BoxCornerRadius = 0, IconCornerScale = 0,
                BoxBackground = "#123456", IconBackground = "#AF52DE", ItemHover = "#FFFF00", ItemSelected = "#00FF00",
                ItemHoverOpacity = 0, Accent = "#FF9500" });
            var customized = new ResourceDictionary();
            AppThemeManager.ApplyDesktopBoxResources(customized);
            Assert.Equal(new CornerRadius(0), AppThemeManager.DesktopCorners.BoxSurfaceRadius);
            Assert.Equal(new CornerRadius(0), AppThemeManager.DesktopCorners.PixelIconRadius);
            AppThemeManager.SetCustomization(theme, ThemeCustomization.Empty);
            AppThemeManager.ApplyDesktopBoxResources(customized);
            Assert.Equal(before.Count, customized.Count);
            foreach (var key in before.Keys)
            {
                if (before[key] is SolidColorBrush original)
                {
                    var restored = Assert.IsType<SolidColorBrush>(customized[key]);
                    Assert.Equal(original.Color, restored.Color);
                    Assert.True(restored.IsFrozen);
                }
                else Assert.Equal(before[key], customized[key]);
            }
        }
        finally { AppThemeManager.ResetBoxOpacitiesForTests(); AppThemeManager.Apply(previous); }
    }

    [Theory]
    [InlineData(AppTheme.Moe)]
    [InlineData(AppTheme.Glass)]
    [InlineData(AppTheme.Crystal)]
    public void RgbOverrides_KeepSurfaceAlphaAndIndependentInteractionColors(AppTheme theme)
    {
        AppThemeManager.ResetBoxOpacitiesForTests();
        try
        {
            AppThemeManager.SetBoxOpacity(theme, 0.68765);
            var surface = AppThemeManager.GetDesktopSurfaceColor(theme);
            var frame = AppThemeManager.GetDesktopIconFrameColor(theme);
            AppThemeManager.SetCustomization(theme, new ThemeCustomization { BoxBackground = "#123456", IconBackground = "#AF52DE", ItemHover = "#FF9500", ItemHoverOpacity = 0.25 });
            Assert.Equal(Color.FromArgb(surface.A, 0x12, 0x34, 0x56), AppThemeManager.GetDesktopSurfaceColor(theme));
            Assert.Equal(Color.FromArgb(frame.A, 0xAF, 0x52, 0xDE), AppThemeManager.GetDesktopIconFrameColor(theme));
            Assert.Equal(Color.FromArgb(64, 255, 149, 0), AppThemeManager.GetItemHoverColor(theme, true));
            Assert.Equal(Color.FromArgb(64, 255, 149, 0), AppThemeManager.GetItemHoverColor(theme, false));
            Assert.Equal(AppThemeManager.GetDesktopBoxColor(theme, "AccentSoftBrush", 0.68765), AppThemeManager.GetItemSelectedColor(theme, true));
            Assert.Equal(0.68765, AppThemeManager.GetBoxOpacity(theme));
        }
        finally { AppThemeManager.ResetBoxOpacitiesForTests(); }
    }

    [Fact]
    public void IconCornerScale_SurvivesSizeChangesAndRestoresEveryOriginalSize()
    {
        var baseline = new DesktopBoxLayoutSettings();
        var custom = new DesktopBoxLayoutSettings();
        custom.ApplyCornerScale(1.5);
        foreach (var preset in new[] { "3x3", "4x4", "5x5", "6x6" })
        {
            baseline.ApplyPresetWithoutCallback(preset);
            custom.ApplyPresetWithoutCallback(preset);
            Assert.Equal(baseline.IconCornerRadius.TopLeft * 1.5, custom.IconCornerRadius.TopLeft);
            Assert.Equal(baseline.ItemCornerRadius.TopLeft * 1.5, custom.ItemCornerRadius.TopLeft);
            Assert.Equal(baseline.ItemSlotWidth, custom.ItemSlotWidth);
        }
        custom.ApplyCornerScale(1);
        Assert.Equal(baseline.IconCornerRadius, custom.IconCornerRadius);
    }

    [Fact]
    public async Task SnapshotLoad_PreservesLegacyPrecisionAndIndependentThemesWithoutWritingOverrides()
    {
        AppThemeManager.ResetBoxOpacitiesForTests();
        var store = new MemorySettings();
        try
        {
            var values = new Dictionary<string, string> { [SettingsViewModel.ThemeBoxOpacityMigrationVersionSettingKey] = "2" };
            foreach (var theme in Enum.GetValues<AppTheme>()) values[SettingsViewModel.GetThemeBoxOpacitySettingKey(theme)] = "0.68765";
            values["ThemeCustomization.Glass"] = ThemeCustomizationSettings.Serialize(new ThemeCustomization { BoxCornerRadius = 0, Accent = "#AF52DE" })!;
            var model = Create(store);
            await model.LoadAsync(new StartupSettingsSnapshot(values));
            Assert.Equal(0, store.ReadCount);
            Assert.Empty(store.Values);
            Assert.Equal(ThemeCustomization.Empty, AppThemeManager.GetCustomization(AppTheme.Moe));
            Assert.Equal(0, AppThemeManager.GetCustomization(AppTheme.Glass).BoxCornerRadius);
            foreach (var theme in Enum.GetValues<AppTheme>()) Assert.Equal(0.68765, AppThemeManager.GetBoxOpacity(theme));
        }
        finally { AppThemeManager.ResetBoxOpacitiesForTests(); }
    }

    [Fact]
    public async Task RapidEditsAndReset_FlushOnlyLatestValuesAndKeepOtherTheme()
    {
        var previous = AppThemeManager.CurrentTheme;
        AppThemeManager.ResetBoxOpacitiesForTests();
        var store = new MemorySettings();
        var model = Create(store);
        try
        {
            await model.ApplyGlassThemeCommand.ExecuteAsync(null);
            model.BoxCornerRadius = 7;
            model.ThemeColors.Single(c => c.Key == "Accent").Hex = "#AF52DE";
            await model.ApplyMoeThemeCommand.ExecuteAsync(null);
            for (var value = 0; value <= 32; value++) model.BoxCornerRadius = value;
            model.ThemeColors.Single(c => c.Key == "BoxBackground").Hex = "#123456";
            model.ThemeTransparencyPercent = 60;
            model.ResetThemePresetCommand.Execute(null);
            await model.FlushPendingThemeSettingsAsync();
            await Task.Delay(350);
            Assert.Null(await store.GetSettingAsync("ThemeCustomization.Moe"));
            Assert.Equal("1.00", await store.GetSettingAsync("ThemeBoxOpacity.Moe"));
            Assert.Equal(ThemeCustomization.Empty, AppThemeManager.GetCustomization(AppTheme.Moe));
            var glass = ThemeCustomizationSettings.Deserialize(await store.GetSettingAsync("ThemeCustomization.Glass"));
            Assert.Equal(7, glass.BoxCornerRadius);
            Assert.Equal("#AF52DE", glass.Accent);
            await model.ApplyGlassThemeCommand.ExecuteAsync(null);
            Assert.Equal(7, model.BoxCornerRadius);
            Assert.Equal("#AF52DE", model.ThemeColors.Single(c => c.Key == "Accent").Hex);
        }
        finally { await model.FlushPendingThemeSettingsAsync(); AppThemeManager.ResetBoxOpacitiesForTests(); AppThemeManager.Apply(previous); }
    }

    [Fact]
    public async Task InvalidInputAndSingleReset_DoNotChangeOtherFields()
    {
        AppThemeManager.ResetBoxOpacitiesForTests();
        var model = Create(new MemorySettings());
        try
        {
            model.BoxCornerRadius = 5;
            model.IconCornerPercent = 125;
            var color = model.ThemeColors.Single(c => c.Key == "ItemHover");
            color.Hex = "#123456";
            color.Hex = "#BAD";
            Assert.NotEmpty(color.Error);
            Assert.Equal("#123456", AppThemeManager.GetCustomization(model.CurrentTheme).ItemHover);
            model.ResetThemeFieldCommand.Execute("BoxCornerRadius");
            Assert.Null(AppThemeManager.GetCustomization(model.CurrentTheme).BoxCornerRadius);
            Assert.Equal(125, model.IconCornerPercent);
            color.ResetCommand.Execute(null);
            Assert.Null(AppThemeManager.GetCustomization(model.CurrentTheme).ItemHover);
            Assert.Empty(color.Error);
        }
        finally { await model.FlushPendingThemeSettingsAsync(); AppThemeManager.ResetBoxOpacitiesForTests(); }
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    [Theory]
    [InlineData(AppTheme.Moe)]
    [InlineData(AppTheme.Glass)]
    [InlineData(AppTheme.Crystal)]
    public void AppearanceRefresh_RetainsUnchangedResourceInstances(AppTheme theme)
    {
        var previous = AppThemeManager.CurrentTheme;
        AppThemeManager.ResetBoxOpacitiesForTests();
        try
        {
            AppThemeManager.Apply(theme);
            var resources = new ResourceDictionary();
            AppThemeManager.ApplyDesktopBoxResources(resources);
            AssertRetainedResources(() => AppThemeManager.ApplyDesktopBoxResources(resources), _ => false);
            AssertRetainedResources(() =>
            {
                AppThemeManager.SetCustomization(theme, new ThemeCustomization { BoxCornerRadius = 5 });
                AppThemeManager.ApplyDesktopBoxResources(resources);
            }, _ => false);
            AssertRetainedResources(() =>
            {
                AppThemeManager.SetCustomization(theme, AppThemeManager.GetCustomization(theme) with { IconCornerScale = 1.5 });
                AppThemeManager.ApplyDesktopBoxResources(resources);
            }, _ => false);
            AssertRetainedResources(() =>
            {
                AppThemeManager.SetCustomization(theme, AppThemeManager.GetCustomization(theme) with { ItemHoverOpacity = .5 });
                AppThemeManager.ApplyDesktopBoxResources(resources);
            }, key => key == "ItemHoverBrush");
            AssertRetainedResources(() => AppThemeManager.ApplyDesktopBoxResources(resources), _ => false);

            void AssertRetainedResources(Action update, Func<string, bool> changes)
            {
                var snapshot = resources.Keys.Cast<string>().ToDictionary(key => key, key => resources[key]);
                update();
                Assert.Equal(snapshot.Count, resources.Count);
                foreach (var (key, value) in snapshot)
                {
                    if (changes(key)) Assert.NotSame(value, resources[key]);
                    else Assert.Same(value, resources[key]);
                }
            }
        }
        finally { AppThemeManager.ResetBoxOpacitiesForTests(); AppThemeManager.Apply(previous); }
    }

    [Fact]
    public void SharedCorners_KeepModeDefaultsAndOnlyNotifyTheChangedGeometry()
    {
        var corners = new DesktopBoxCornerSettings();
        var properties = typeof(DesktopBoxCornerSettings).GetProperties()
            .Where(property => property.PropertyType == typeof(CornerRadius)).ToArray();
        var baseline = properties.ToDictionary(property => property.Name, property => property.GetValue(corners));
        var changes = new List<string?>();
        corners.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        corners.Apply(new ThemeCustomization { BoxCornerRadius = 1 });
        Assert.Contains(nameof(corners.BoxSurfaceRadius), changes);
        Assert.DoesNotContain(changes, name => name is not null && !name.StartsWith("Box", StringComparison.Ordinal));
        Assert.Equal(new CornerRadius(1), corners.BoxWindowRadius);
        Assert.Equal(new CornerRadius(1), corners.BoxPixelSurfaceRadius);
        Assert.Equal(new CornerRadius(0), corners.BoxPopupSurfaceRadius);
        Assert.Equal(new CornerRadius(0), corners.BoxPopupInnerRadius);

        changes.Clear();
        var customization = new ThemeCustomization { BoxCornerRadius = 1, IconCornerScale = 1.5 };
        corners.Apply(customization);
        Assert.DoesNotContain(changes, name => name is not null && name.StartsWith("Box", StringComparison.Ordinal));
        Assert.Equal(new CornerRadius(9), corners.PixelItemRadius);
        Assert.Equal(new CornerRadius(6), corners.PixelIconRadius);
        Assert.Equal(new CornerRadius(6), corners.MappingItemRadius);
        changes.Clear();
        corners.Apply(customization with { BoxBackground = "#123456", ItemHoverOpacity = .5 });
        Assert.Empty(changes);

        corners.Apply(ThemeCustomization.Empty);
        foreach (var property in properties) Assert.Equal(baseline[property.Name], property.GetValue(corners));
    }

    [Fact]
    public void SwitchingThemes_RestoresSharedCornersAndKeepsTheBindingSourceStable()
    {
        var previous = AppThemeManager.CurrentTheme;
        AppThemeManager.ResetBoxOpacitiesForTests();
        try
        {
            AppThemeManager.SetCustomization(AppTheme.Moe, new ThemeCustomization { BoxCornerRadius = 5, IconCornerScale = .5 });
            AppThemeManager.SetCustomization(AppTheme.Glass, new ThemeCustomization { BoxCornerRadius = 25, IconCornerScale = 1.5 });
            var source = AppThemeManager.DesktopCorners;
            AppThemeManager.Apply(AppTheme.Moe);
            Assert.Equal(new CornerRadius(5), source.BoxSurfaceRadius);
            Assert.Equal(new CornerRadius(2), source.PixelIconRadius);
            AppThemeManager.Apply(AppTheme.Glass);
            Assert.Same(source, AppThemeManager.DesktopCorners);
            Assert.Equal(new CornerRadius(25), source.BoxSurfaceRadius);
            Assert.Equal(new CornerRadius(6), source.PixelIconRadius);
            AppThemeManager.Apply(AppTheme.Crystal);
            Assert.Equal(new CornerRadius(18), source.BoxSurfaceRadius);
            Assert.Equal(new CornerRadius(4), source.PixelIconRadius);
        }
        finally { AppThemeManager.ResetBoxOpacitiesForTests(); AppThemeManager.Apply(previous); }
    }

    [Fact]
    public void ReusedResources_KeepThemeSwitchesAndEditorModeEquivalentToFreshResources()
    {
        var previous = AppThemeManager.CurrentTheme;
        AppThemeManager.ResetBoxOpacitiesForTests();
        try
        {
            var resources = new ResourceDictionary();
            foreach (var theme in new[] { AppTheme.Crystal, AppTheme.Glass, AppTheme.Moe, AppTheme.Crystal })
            {
                AppThemeManager.Apply(theme);
                foreach (var customization in new[] { new ThemeCustomization { BoxBackground = "#123456", Accent = "#AF52DE", BoxCornerRadius = 0 }, ThemeCustomization.Empty })
                {
                    AppThemeManager.SetCustomization(theme, customization);
                    AppThemeManager.ApplyDesktopBoxResources(resources);
                    AssertEquivalentToFresh(AppThemeManager.ApplyDesktopBoxResources);
                    AppThemeManager.ApplyEditorOpacityResources(resources);
                    AssertEquivalentToFresh(AppThemeManager.ApplyEditorOpacityResources);
                }
            }

            void AssertEquivalentToFresh(Action<ResourceDictionary> apply)
            {
                var fresh = new ResourceDictionary();
                apply(fresh);
                Assert.Equal(fresh.Count, resources.Count);
                foreach (var key in fresh.Keys)
                {
                    if (fresh[key] is SolidColorBrush brush)
                        Assert.Equal(brush.Color, Assert.IsType<SolidColorBrush>(resources[key]).Color);
                    else Assert.Equal(fresh[key], resources[key]);
                }
            }
        }
        finally { AppThemeManager.ResetBoxOpacitiesForTests(); AppThemeManager.Apply(previous); }
    }

    [Theory]
    [InlineData(AppTheme.Moe)]
    [InlineData(AppTheme.Glass)]
    [InlineData(AppTheme.Crystal)]
    public async Task SliderEdits_DoNotNotifyOrReplaceUnrelatedPreviews(AppTheme theme)
    {
        var previous = AppThemeManager.CurrentTheme;
        AppThemeManager.ResetBoxOpacitiesForTests();
        AppThemeManager.Apply(theme);
        var model = Create(new MemorySettings());
        try
        {
            var previews = new Dictionary<string, Brush>
            {
                [nameof(model.PreviewSurfaceBrush)] = model.PreviewSurfaceBrush,
                [nameof(model.PreviewFrameBrush)] = model.PreviewFrameBrush,
                [nameof(model.PreviewBorderBrush)] = model.PreviewBorderBrush,
                [nameof(model.PreviewHoverBrush)] = model.PreviewHoverBrush,
                [nameof(model.PreviewSelectedBrush)] = model.PreviewSelectedBrush,
                [nameof(model.PreviewAccentBrush)] = model.PreviewAccentBrush
            };
            var swatches = model.ThemeColors.Select(option => option.PreviewBrush).ToArray();
            var changes = new List<string?>();
            var swatchChanges = new List<string?>();
            model.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
            foreach (var option in model.ThemeColors) option.PropertyChanged += (_, e) => swatchChanges.Add(e.PropertyName);
            model.BoxCornerRadius = 5;
            model.IconCornerPercent = 125;
            Assert.Contains(nameof(model.PreviewBoxRadius), changes);
            Assert.Contains(nameof(model.PreviewIconRadius), changes);
            Assert.DoesNotContain(changes, name => name is not null && previews.ContainsKey(name));
            foreach (var (name, brush) in previews)
                Assert.Same(brush, typeof(SettingsViewModel).GetProperty(name)!.GetValue(model));

            changes.Clear();
            model.ItemHoverTransparencyPercent = 50;
            Assert.Contains(nameof(model.PreviewHoverBrush), changes);
            Assert.DoesNotContain(changes, name => name is not null && previews.ContainsKey(name) && name != nameof(model.PreviewHoverBrush));
            Assert.DoesNotContain(nameof(model.PreviewBoxRadius), changes);
            Assert.DoesNotContain(nameof(model.PreviewIconRadius), changes);
            Assert.Empty(swatchChanges);
            for (var i = 0; i < swatches.Length; i++) Assert.Same(swatches[i], model.ThemeColors[i].PreviewBrush);
        }
        finally { await model.FlushPendingThemeSettingsAsync(); AppThemeManager.ResetBoxOpacitiesForTests(); AppThemeManager.Apply(previous); }
    }

    [Fact]
    public async Task CommittingUneditedInputs_KeepsPresetInheritanceAndDoesNotWrite()
    {
        AppThemeManager.ResetBoxOpacitiesForTests();
        var store = new MemorySettings();
        var model = Create(store);
        try
        {
            model.BoxCornerRadius = model.BoxCornerRadius;
            model.IconCornerPercent = model.IconCornerPercent;
            model.ItemHoverTransparencyPercent = model.ItemHoverTransparencyPercent;
            foreach (var color in model.ThemeColors) color.Hex = color.Hex;
            await model.FlushPendingThemeSettingsAsync();
            Assert.Equal(ThemeCustomization.Empty, AppThemeManager.GetCustomization(model.CurrentTheme));
            Assert.Empty(store.Values);
        }
        finally { await model.FlushPendingThemeSettingsAsync(); AppThemeManager.ResetBoxOpacitiesForTests(); }
    }

    [Fact]
    public async Task FailedDelayedSave_IsRetriedByShutdownFlush()
    {
        AppThemeManager.ResetBoxOpacitiesForTests();
        var store = new MemorySettings { FailWrites = true };
        var model = Create(store);
        try
        {
            model.BoxCornerRadius = 5;
            await store.FailedWrite.Task.WaitAsync(TimeSpan.FromSeconds(3));
            // The writer's finally removes its delay; the unsaved value must remain pending.
            store.FailWrites = false;
            await model.FlushPendingThemeSettingsAsync();
            var saved = ThemeCustomizationSettings.Deserialize(await store.GetSettingAsync("ThemeCustomization." + model.CurrentTheme));
            Assert.Equal(5, saved.BoxCornerRadius);
        }
        finally { store.FailWrites = false; await model.FlushPendingThemeSettingsAsync(); AppThemeManager.ResetBoxOpacitiesForTests(); }
    }

    [Fact]
    public void CornerEdits_DoNotRecalculateItemLayoutOrIconSizes()
    {
        var paths = new AppPaths(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WitchDrawer.Theme", Guid.NewGuid().ToString("N")));
        var repository = new DrawerRepository(paths.DatabasePath);
        var now = DateTimeOffset.UtcNow;
        var model = new DesktopBoxViewModel(new Box(Guid.NewGuid(), "Preview", BoxType.Normal, null, 0, now, now),
            new DrawerService(paths, repository), new TodoService(repository), new Launcher(),
            new ShellChangeNotifierService(), NullAppLogger.Instance, BoxVisualStyle.Modern);
        var changes = new List<string?>();
        model.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        model.LayoutSettings.ApplyCornerScale(1.5);
        Assert.Empty(changes);
    }

    [Fact]
    public async Task MissingVersionTwoOpacityKeys_FallBackToEachOriginalPreset()
    {
        AppThemeManager.ResetBoxOpacitiesForTests();
        try
        {
            await Create(new MemorySettings()).LoadAsync(new StartupSettingsSnapshot(new Dictionary<string, string>
                { [SettingsViewModel.ThemeBoxOpacityMigrationVersionSettingKey] = "2" }));
            Assert.Equal(1, AppThemeManager.GetBoxOpacity(AppTheme.Moe));
            Assert.Equal(.82, AppThemeManager.GetBoxOpacity(AppTheme.Glass));
            Assert.Equal(.4, AppThemeManager.GetBoxOpacity(AppTheme.Crystal));
        }
        finally { AppThemeManager.ResetBoxOpacitiesForTests(); }
    }

    private sealed class Launcher : IFileLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private static SettingsViewModel Create(ISettingsStore store) => new(store, NullAppLogger.Instance,
        new DesktopAdapter(), new AutoHideSettingsStore(store), new UiOperationState(NullAppLogger.Instance));
    private sealed class DesktopAdapter : IDesktopIntegration
    {
        public Task<bool> IsStartupEnabledAsync() => Task.FromResult(false);
        public Task SetStartupEnabledAsync(bool enabled) => Task.CompletedTask;
        public Task<bool> AreDesktopIconsHiddenAsync() => Task.FromResult(false);
        public Task<bool> ToggleDesktopIconsAsync() => Task.FromResult(false);
    }
    private sealed class MemorySettings : ISettingsStore
    {
        public ConcurrentDictionary<string, string> Values { get; } = new();
        public bool FailWrites { get; set; }
        public TaskCompletionSource FailedWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReadCount { get; private set; }
        public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
        { ReadCount++; return Task.FromResult(Values.GetValueOrDefault(key)); }
        public Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Values));
        public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailWrites) { FailedWrite.TrySetResult(); throw new System.IO.IOException("Simulated settings write failure"); }
            Values[key] = value;
            return Task.CompletedTask;
        }
        public Task<bool> DeleteSettingAsync(string key, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Values.TryRemove(key, out _)); }
    }
}
