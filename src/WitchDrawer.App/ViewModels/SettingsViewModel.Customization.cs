using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed partial class SettingsViewModel
{
    private double _displayedBoxCornerRadius;
    private double _displayedIconCornerPercent;
    private double _displayedHoverTransparency;
    private string? _displayedCustomizationLabel;
    private string? _displayedBoxCornerLabel;
    private string? _displayedIconCornerLabel;
    private string? _displayedHoverLabel;
    private CornerRadius _previewBoxRadius;
    private CornerRadius _previewIconRadius;
    private Brush _previewSurfaceBrush = Brushes.Transparent;
    private Brush _previewFrameBrush = Brushes.Transparent;
    private Brush _previewBorderBrush = Brushes.Transparent;
    private Brush _previewHoverBrush = Brushes.Transparent;
    private Brush _previewSelectedBrush = Brushes.Transparent;
    private Brush _previewAccentBrush = Brushes.Transparent;
    public IReadOnlyList<ThemeColorOptionViewModel> ThemeColors { get; private set; } = [];
    public IRelayCommand ResetThemePresetCommand { get; private set; } = null!;
    public IRelayCommand<string> ResetThemeFieldCommand { get; private set; } = null!;

    private ThemeCustomization Customization => AppThemeManager.GetCustomization(CurrentTheme);
    public string CustomizationLabel => Customization.IsEmpty ? "颜色与圆角跟随预设" : "已自定义颜色或样式";
    public double BoxCornerRadius
    {
        get => Customization.BoxCornerRadius ?? 18;
        set
        {
            if (!double.IsFinite(value)) return;
            var normalized = Math.Round(Math.Clamp(value, 0, 32));
            if (normalized != BoxCornerRadius) UpdateCustomization(settings => settings with { BoxCornerRadius = normalized });
        }
    }
    public double IconCornerPercent
    {
        get => (Customization.IconCornerScale ?? 1) * 100;
        set
        {
            if (!double.IsFinite(value)) return;
            var normalized = Math.Round(Math.Clamp(value, 0, 200));
            if (normalized != IconCornerPercent) UpdateCustomization(settings => settings with { IconCornerScale = normalized / 100 });
        }
    }
    public double ItemHoverTransparencyPercent
    {
        get => Math.Round((1 - AppThemeManager.GetItemHoverColor(CurrentTheme, desktop: true).A / 255d) * 100);
        set
        {
            if (!double.IsFinite(value)) return;
            var normalized = Math.Round(Math.Clamp(value, 0, 100));
            if (normalized != ItemHoverTransparencyPercent) UpdateCustomization(settings => settings with { ItemHoverOpacity = 1 - normalized / 100 });
        }
    }
    public string BoxCornerLabel => Customization.BoxCornerRadius is null ? "跟随各模式预设" : $"{BoxCornerRadius:0} px";
    public string IconCornerLabel => Customization.IconCornerScale is null ? "跟随图标尺寸" : $"{IconCornerPercent:0}%";
    public string HoverTransparencyLabel => Customization.ItemHoverOpacity is null ? "跟随主题" : $"{ItemHoverTransparencyPercent:0}%";
    public CornerRadius PreviewBoxRadius => _previewBoxRadius;
    public CornerRadius PreviewIconRadius => _previewIconRadius;
    public Brush PreviewSurfaceBrush => _previewSurfaceBrush;
    public Brush PreviewFrameBrush => _previewFrameBrush;
    public Brush PreviewBorderBrush => _previewBorderBrush;
    public Brush PreviewHoverBrush => _previewHoverBrush;
    public Brush PreviewSelectedBrush => _previewSelectedBrush;
    public Brush PreviewAccentBrush => _previewAccentBrush;

    private void InitializeCustomization()
    {
        ResetThemePresetCommand = new RelayCommand(() =>
        {
            UpdateCustomization(_ => ThemeCustomization.Empty);
            ResetThemeTransparency();
            StatusText = $"已恢复 {ThemeLabel} 原始预设";
        });
        ResetThemeFieldCommand = new RelayCommand<string>(field =>
        {
            if (TryResetThemeTransparencyField(field)) return;
            UpdateCustomization(settings => field switch
            {
                "BoxCornerRadius" => settings with { BoxCornerRadius = null },
                "IconCornerScale" => settings with { IconCornerScale = null },
                "ItemHoverOpacity" => settings with { ItemHoverOpacity = null },
                _ => settings
            });
        });
        ThemeColors = new[]
        {
            new ThemeColorOptionViewModel("BoxBackground", "盒子背景", color => UpdateCustomization(s => s with { BoxBackground = color })),
            new ThemeColorOptionViewModel("IconBackground", "图标背景框", color => UpdateCustomization(s => s with { IconBackground = color })),
            new ThemeColorOptionViewModel("ItemHover", "文件项悬停", color => UpdateCustomization(s => s with { ItemHover = color })),
            new ThemeColorOptionViewModel("ItemSelected", "文件项选中", color => UpdateCustomization(s => s with { ItemSelected = color })),
            new ThemeColorOptionViewModel("Accent", "强调色", color => UpdateCustomization(s => s with { Accent = color }))
        };
        RefreshCustomization();
    }

    private void UpdateCustomization(Func<ThemeCustomization, ThemeCustomization> change)
    {
        var theme = CurrentTheme;
        var next = change(Customization).Normalize();
        AppThemeManager.SetCustomization(theme, next);
        RefreshCustomization();
        QueueThemeOpacitySave(ThemeCustomizationSettings.KeyPrefix + theme, ThemeCustomizationSettings.Serialize(next));
    }

    private async Task RestoreCustomizationsAsync(StartupSettingsSnapshot? snapshot)
    {
        foreach (var theme in Enum.GetValues<AppTheme>())
            AppThemeManager.SetCustomization(theme, ThemeCustomizationSettings.Deserialize(
                await ReadSettingAsync(ThemeCustomizationSettings.KeyPrefix + theme, snapshot)));
        RefreshCustomization();
    }

    private void RefreshCustomization()
    {
        var accent = AppThemeManager.ResolveAccentColor(CurrentTheme, "AccentBrush",
            AppThemeManager.GetDesktopBoxColor(CurrentTheme, "AccentBrush", AppThemeManager.GetBoxOpacity(CurrentTheme)));
        SetPreviewBrush(ref _previewSurfaceBrush, AppThemeManager.GetDesktopSurfaceColor(CurrentTheme), nameof(PreviewSurfaceBrush));
        SetPreviewBrush(ref _previewFrameBrush, AppThemeManager.GetDesktopIconFrameColor(CurrentTheme), nameof(PreviewFrameBrush));
        SetPreviewBrush(ref _previewBorderBrush, AppThemeManager.GetDesktopBoxBorderColor(CurrentTheme), nameof(PreviewBorderBrush));
        SetPreviewBrush(ref _previewHoverBrush, AppThemeManager.GetItemHoverColor(CurrentTheme, true), nameof(PreviewHoverBrush));
        SetPreviewBrush(ref _previewSelectedBrush, AppThemeManager.GetItemSelectedColor(CurrentTheme, true), nameof(PreviewSelectedBrush));
        SetPreviewBrush(ref _previewAccentBrush, accent, nameof(PreviewAccentBrush));
        foreach (var option in ThemeColors)
        {
            var (color, custom) = option.Key switch
            {
                "BoxBackground" => (AppThemeManager.GetDesktopSurfaceColor(CurrentTheme), Customization.BoxBackground),
                "IconBackground" => (AppThemeManager.GetDesktopIconFrameColor(CurrentTheme), Customization.IconBackground),
                "ItemHover" => (AppThemeManager.GetItemHoverColor(CurrentTheme, true), Customization.ItemHover),
                "ItemSelected" => (AppThemeManager.GetItemSelectedColor(CurrentTheme, true), Customization.ItemSelected),
                _ => (accent, Customization.Accent)
            };
            option.Refresh(color, custom is not null);
        }
        SetProperty(ref _displayedCustomizationLabel, CustomizationLabel, nameof(CustomizationLabel));
        SetProperty(ref _displayedBoxCornerRadius, BoxCornerRadius, nameof(BoxCornerRadius));
        SetProperty(ref _displayedIconCornerPercent, IconCornerPercent, nameof(IconCornerPercent));
        SetProperty(ref _displayedHoverTransparency, ItemHoverTransparencyPercent, nameof(ItemHoverTransparencyPercent));
        SetProperty(ref _displayedBoxCornerLabel, BoxCornerLabel, nameof(BoxCornerLabel));
        SetProperty(ref _displayedIconCornerLabel, IconCornerLabel, nameof(IconCornerLabel));
        SetProperty(ref _displayedHoverLabel, HoverTransparencyLabel, nameof(HoverTransparencyLabel));
        SetProperty(ref _previewBoxRadius, new CornerRadius(BoxCornerRadius), nameof(PreviewBoxRadius));
        SetProperty(ref _previewIconRadius, new CornerRadius(6 * IconCornerPercent / 100), nameof(PreviewIconRadius));
    }

    private void SetPreviewBrush(ref Brush field, Color color, string property)
    {
        if (field is SolidColorBrush existing && existing.Color == color) return;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        SetProperty(ref field, brush, property);
    }

    public async Task FlushPendingThemeSettingsAsync()
    {
        KeyValuePair<string, string?>[] pending;
        lock (_themeOpacitySaveLock)
        {
            pending = _pendingThemeSettings.ToArray();
            foreach (var delay in _themeOpacitySaveDelays.Values.ToArray()) delay.Cancel();
            _themeOpacitySaveDelays.Clear();
            _pendingThemeSettings.Clear();
        }
        await _themeOpacityWriteGate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var (key, value) in pending)
            {
                if (value is null) await _settings.DeleteSettingAsync(key).ConfigureAwait(false);
                else await _settings.SetSettingAsync(key, value).ConfigureAwait(false);
            }
        }
        finally { _themeOpacityWriteGate.Release(); }
    }
}
