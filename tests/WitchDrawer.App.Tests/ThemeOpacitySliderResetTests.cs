using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Tests;

[Collection("AppThemeManager")]
public sealed class ThemeOpacitySliderResetTests
{
    private static readonly string[] Fields =
    [
        nameof(SettingsViewModel.ThemeTransparencyPercent),
        nameof(SettingsViewModel.BoxBorderTransparencyPercent),
        nameof(SettingsViewModel.IconFrameTransparencyPercent)
    ];

    [Theory]
    [InlineData(AppTheme.Moe)]
    [InlineData(AppTheme.Glass)]
    [InlineData(AppTheme.Crystal)]
    public Task PresetReset_UpdatesValuesAndRenderedTracks(AppTheme theme) => WithThemeAsync(theme, async fixture =>
    {
        fixture.SetSliderValues(42, 95, 65);
        await fixture.LayoutAsync();
        fixture.Model.ResetThemePresetCommand.Execute(null);
        await fixture.LayoutAsync();

        fixture.AssertSynchronized(fixture.Defaults);
    });

    [Theory]
    [InlineData(AppTheme.Moe)]
    [InlineData(AppTheme.Glass)]
    [InlineData(AppTheme.Crystal)]
    public Task FractionalInputAndRepeatedPresetReset_KeepControlsSynchronized(AppTheme theme) =>
        WithThemeAsync(theme, async fixture =>
        {
            for (var index = 0; index < Fields.Length; index++)
            {
                fixture.Sliders[index].SetCurrentValue(Slider.ValueProperty, fixture.Defaults[index] + .25);
                Assert.Equal(fixture.Defaults[index], Read(fixture.Model, Fields[index]));
                Assert.Equal(fixture.Defaults[index], fixture.Sliders[index].Value);
            }
            await fixture.LayoutAsync();
            fixture.AssertSynchronized(fixture.Defaults);
            fixture.Model.ResetThemePresetCommand.Execute(null);
            await fixture.LayoutAsync();
            fixture.AssertSynchronized(fixture.Defaults);
            fixture.Model.ResetThemePresetCommand.Execute(null);
            await fixture.LayoutAsync();
            fixture.AssertSynchronized(fixture.Defaults);
        });

    [Theory]
    [InlineData(AppTheme.Moe, 0)]
    [InlineData(AppTheme.Moe, 1)]
    [InlineData(AppTheme.Moe, 2)]
    [InlineData(AppTheme.Glass, 0)]
    [InlineData(AppTheme.Glass, 1)]
    [InlineData(AppTheme.Glass, 2)]
    [InlineData(AppTheme.Crystal, 0)]
    [InlineData(AppTheme.Crystal, 1)]
    [InlineData(AppTheme.Crystal, 2)]
    public Task SingleReset_UpdatesItsTrackAndPreservesOtherTransparencyValues(AppTheme theme, int fieldIndex) =>
        WithThemeAsync(theme, async fixture =>
        {
            fixture.SetSliderValues(42, 95, 65);
            await fixture.LayoutAsync();
            var expected = Fields.Select(field => Read(fixture.Model, field)).ToArray();
            expected[fieldIndex] = fieldIndex == 0
                ? fixture.Defaults[0]
                : Math.Round((1 - AppThemeManager.GetDesktopBoxColor(theme,
                    fieldIndex == 1 ? "GlassStrokeBrush" : "GlassInnerBrush",
                    AppThemeManager.GetBoxOpacity(theme)).A / 255d) * 100);

            fixture.Model.ResetThemeFieldCommand.Execute(Fields[fieldIndex]);
            await fixture.LayoutAsync();
            fixture.AssertSynchronized(expected);

            fixture.Sliders[fieldIndex].SetCurrentValue(Slider.ValueProperty, expected[fieldIndex] + .25);
            Assert.Equal(expected[fieldIndex], Read(fixture.Model, Fields[fieldIndex]));
            Assert.Equal(expected[fieldIndex], fixture.Sliders[fieldIndex].Value);
            fixture.Model.ResetThemeFieldCommand.Execute(Fields[fieldIndex]);
            await fixture.LayoutAsync();
            fixture.AssertSynchronized(expected);
        });

    private static Task WithThemeAsync(AppTheme theme, Func<Fixture, Task> test) => RunOnStaAsync(async () =>
    {
        var previous = AppThemeManager.CurrentTheme;
        AppThemeManager.ResetBoxOpacitiesForTests();
        Fixture? fixture = null;
        try
        {
            AppThemeManager.Apply(theme);
            fixture = new Fixture();
            await fixture.LayoutAsync();
            await test(fixture);
        }
        finally
        {
            if (fixture is not null) await fixture.Model.FlushPendingThemeSettingsAsync();
            AppThemeManager.ResetBoxOpacitiesForTests();
            AppThemeManager.Apply(previous);
        }
    });

    private static double Read(SettingsViewModel model, string field) => field switch
    {
        nameof(SettingsViewModel.ThemeTransparencyPercent) => model.ThemeTransparencyPercent,
        nameof(SettingsViewModel.BoxBorderTransparencyPercent) => model.BoxBorderTransparencyPercent,
        nameof(SettingsViewModel.IconFrameTransparencyPercent) => model.IconFrameTransparencyPercent,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private sealed class Fixture
    {
        private readonly StackPanel _panel = new() { Width = 600 };
        public SettingsViewModel Model { get; }
        public Slider[] Sliders { get; }
        public double[] Defaults { get; }

        public Fixture()
        {
            var store = new MemorySettings();
            Model = new SettingsViewModel(store, NullAppLogger.Instance, new DesktopAdapter(),
                new AutoHideSettingsStore(store), new UiOperationState(NullAppLogger.Instance));
            Defaults = Fields.Select(field => Read(Model, field)).ToArray();
            _panel.DataContext = Model;
            _panel.Resources = LoadSliderStyles();
            Sliders = Fields.Select((field, index) =>
            {
                var slider = new Slider
                {
                    Width = 580,
                    Minimum = 0,
                    Maximum = index == 0 ? 90 : 100,
                    Style = (Style)_panel.Resources["ThemeOpacitySliderStyle"]
                };
                slider.SetBinding(Slider.ValueProperty, new Binding(field)
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });
                _panel.Children.Add(slider);
                return slider;
            }).ToArray();
        }

        public void SetSliderValues(params double[] values)
        {
            for (var index = 0; index < Sliders.Length; index++)
                Sliders[index].SetCurrentValue(Slider.ValueProperty, values[index]);
        }

        public async Task LayoutAsync()
        {
            _panel.Measure(new Size(600, 100));
            _panel.Arrange(new Rect(0, 0, 600, 100));
            _panel.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            _panel.UpdateLayout();
        }

        public void AssertSynchronized(IReadOnlyList<double> expected)
        {
            for (var index = 0; index < Fields.Length; index++)
            {
                Assert.Equal(expected[index], Read(Model, Fields[index]));
                Assert.Equal(expected[index], Sliders[index].Value);
                var track = Assert.IsType<Track>(Sliders[index].Template.FindName("PART_Track", Sliders[index]));
                Assert.Equal(expected[index], track.Value);
                Assert.True(BindingOperations.IsDataBound(track, Track.ValueProperty));
                Assert.Equal(Sliders[index].Minimum, track.Minimum);
                Assert.Equal(Sliders[index].Maximum, track.Maximum);
                var expectedLeft = (expected[index] - track.Minimum) / (track.Maximum - track.Minimum)
                    * (track.ActualWidth - track.Thumb.ActualWidth);
                var actualLeft = track.Thumb.TranslatePoint(new Point(), track).X;
                Assert.InRange(Math.Abs(actualLeft - expectedLeft), 0, .01);
            }
        }
    }

    private static ResourceDictionary LoadSliderStyles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WitchDrawer.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var document = XDocument.Load(Path.Combine(directory.FullName, "src", "WitchDrawer.App", "Views", "Styles",
            "MainWindow", "ThemeCardStyles.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var keys = new HashSet<string> { "ThemeOpacityTrackButtonStyle", "ThemeOpacityThumbStyle", "ThemeOpacitySliderStyle" };
        var root = document.Root!;
        foreach (var element in root.Elements().Where(element => !keys.Contains((string?)element.Attribute(xaml + "Key") ?? "")).ToArray())
            element.Remove();
        var resources = Assert.IsType<ResourceDictionary>(XamlReader.Parse(root.ToString()));
        resources["AccentBrush"] = Brushes.Blue;
        resources["AccentHoverBrush"] = Brushes.RoyalBlue;
        resources["GlassSurfaceBrush"] = Brushes.White;
        resources["BorderBrushSoft"] = Brushes.LightGray;
        return resources;
    }

    private static Task RunOnStaAsync(Func<Task> test)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await test(); finished.SetResult(); }
                catch (Exception exception) { finished.SetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return finished.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class MemorySettings : ISettingsStore
    {
        private readonly ConcurrentDictionary<string, string> _values = new();
        public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));
        public Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(_values));
        public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = value;
            return Task.CompletedTask;
        }
        public Task<bool> DeleteSettingAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryRemove(key, out _));
    }

    private sealed class DesktopAdapter : IDesktopIntegration
    {
        public Task<bool> IsStartupEnabledAsync() => Task.FromResult(false);
        public Task SetStartupEnabledAsync(bool enabled) => Task.CompletedTask;
        public Task<bool> AreDesktopIconsHiddenAsync() => Task.FromResult(false);
        public Task<bool> ToggleDesktopIconsAsync() => Task.FromResult(false);
    }
}
