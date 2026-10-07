using WitchDrawer.App.Localization;
using WitchDrawer.Core.Localization;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Messages;
using WitchDrawer.Core;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed partial class SettingsViewModel : LocalizedObservableObject
{
    private const string ThemeSettingKey = "Theme";
    internal const string ThemeBoxOpacitySettingKeyPrefix = "ThemeBoxOpacity.";
    internal const string BoxBorderOpacitySettingKeyPrefix = "DesktopBoxBorderOpacity.";
    internal const string IconFrameOpacitySettingKeyPrefix = "DesktopIconFrameOpacity.";
    internal const string ThemeBoxOpacityMigrationVersionSettingKey = "ThemeBoxOpacityVersion";
    private const string ThemeBoxOpacityMigrationVersion = "2";
    internal const string EditorFollowsBoxOpacitySettingKey = "EditorFollowsBoxOpacity";
    internal const string DesktopDoubleClickSettingKey = "DesktopDoubleClickToggle";
    internal const string IconToolTipCompactSettingKey = "IconToolTipCompact";
    private string _themeLabel = Strings.Get("Light");
    private AppTheme _currentTheme;
    private double _themeTransparencyPercent = (1 - AppThemeManager.DefaultBoxOpacity) * 100;
    private double _boxBorderTransparencyPercent;
    private double _iconFrameTransparencyPercent;
    private readonly object _themeOpacitySaveLock = new();
    private readonly Dictionary<string, CancellationTokenSource> _themeOpacitySaveDelays = [];
    private readonly Dictionary<string, string?> _pendingThemeSettings = [];
    private readonly SemaphoreSlim _themeOpacityWriteGate = new(1, 1);
    private bool _isSynchronizingThemeTransparency;
    private bool _editorFollowsBoxOpacity;
    private bool _iconToolTipCompact;
    private readonly AutoHideSettingsStore _autoHideSettingsStore;
    private bool _autoHideEnabled;
    private int _autoHideHiddenTransparencyPercent = AutoHideSettings.DefaultHiddenTransparencyPercent;
    private AutoHideRevealScope _autoHideRevealScope = AutoHideRevealScope.HoveredBoxOnly;
    private bool _autoHideFadeWholeBox = true;
    private bool _autoHideFadeTitle = true;
    private bool _autoHideFadeBorder = true;
    private CancellationTokenSource? _autoHideSaveCts;
    private bool _launchOnStartup;
    private bool _areDesktopIconsHidden;
    private bool _isDesktopDoubleClickEnabled;
    private readonly ISettingsStore _settings;
    private readonly IAppLogger _logger;
    private readonly IDesktopIntegration _desktop;
    private readonly UiOperationState _operations;
    private string StatusText { set => _operations.StatusText = value; }

    public SettingsViewModel(ISettingsStore settings, IAppLogger logger,
        IDesktopIntegration desktop, AutoHideSettingsStore autoHideSettingsStore, UiOperationState operations)
    {
        _settings = settings;
        _logger = logger;
        _desktop = desktop;
        _autoHideSettingsStore = autoHideSettingsStore;
        _operations = operations;
        SetCurrentTheme(AppThemeManager.CurrentTheme);
        ApplyMoeThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Moe));
        ApplyGlassThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Glass));
        ApplyCrystalThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Crystal));
        ResetThemeTransparencyCommand = new RelayCommand(ResetThemeTransparency);
        InitializeCustomization();
        InitializeLanguage();
        ToggleLaunchOnStartupCommand = new AsyncRelayCommand(ToggleLaunchOnStartupAsync);
        ToggleDesktopIconsCommand = new AsyncRelayCommand(ToggleDesktopIconsAsync);
        ToggleDesktopDoubleClickCommand = new AsyncRelayCommand(ToggleDesktopDoubleClickAsync);
        ToggleEditorOpacityFollowCommand = new AsyncRelayCommand(ToggleEditorOpacityFollowAsync);
        ToggleIconToolTipCompactCommand = new AsyncRelayCommand(ToggleIconToolTipCompactAsync);
        ToggleAutoHideEnabledCommand = new AsyncRelayCommand(ToggleAutoHideEnabledAsync);
        ApplyAutoHideScopeHoveredOnlyCommand =
            new AsyncRelayCommand(() => ApplyAutoHideRevealScopeAsync(AutoHideRevealScope.HoveredBoxOnly));
        ApplyAutoHideScopeAllCommand =
            new AsyncRelayCommand(() => ApplyAutoHideRevealScopeAsync(AutoHideRevealScope.AllBoxes));
    }

    public async Task LoadAsync(StartupSettingsSnapshot? startupSnapshot = null)
    {
        LocalizationProvider.Instance.Apply(AppLanguage.Resolve(
            await ReadSettingAsync(AppLanguage.SettingKey, startupSnapshot), CultureInfo.CurrentUICulture));

        // 必须在首次启动标记写入前判断是否为旧安装，才能让新用户使用二段透明度，
        // 同时让升级用户保留旧主题原本的视觉效果。
        await RestoreThemeBoxOpacitiesAsync(startupSnapshot);
        await RestoreAppearanceOpacitiesAsync(startupSnapshot);
        await RestoreCustomizationsAsync(startupSnapshot);
        var editorOpacityFollowSetting =
            await ReadSettingAsync(EditorFollowsBoxOpacitySettingKey, startupSnapshot);
        EditorFollowsBoxOpacity = bool.TryParse(
            editorOpacityFollowSetting,
            out var editorFollowsBoxOpacity)
            && editorFollowsBoxOpacity;

        var iconToolTipCompactSetting =
            await ReadSettingAsync(IconToolTipCompactSettingKey, startupSnapshot);
        IconToolTipCompact = bool.TryParse(
            iconToolTipCompactSetting,
            out var iconToolTipCompact)
            && iconToolTipCompact;
        PublishIconToolTipMode();

        var autoHideSettings = await _autoHideSettingsStore.LoadAsync(startupSnapshot: startupSnapshot);
        AutoHideEnabled = autoHideSettings.IsEnabled;
        AutoHideHiddenTransparencyPercent = autoHideSettings.HiddenTransparencyPercent;
        AutoHideRevealScope = autoHideSettings.RevealScope;
        AutoHideFadeWholeBox = autoHideSettings.FadeWholeBox;
        AutoHideFadeTitle = autoHideSettings.FadeTitle;
        AutoHideFadeBorder = autoHideSettings.FadeBorder;
        PublishAutoHideSettings();

        LaunchOnStartup = await _desktop.IsStartupEnabledAsync();
        AreDesktopIconsHidden = await _desktop.AreDesktopIconsHiddenAsync();
        var desktopDoubleClickSetting =
            await ReadSettingAsync(DesktopDoubleClickSettingKey, startupSnapshot);
        IsDesktopDoubleClickEnabled =
            bool.TryParse(desktopDoubleClickSetting, out var desktopDoubleClickEnabled)
            && desktopDoubleClickEnabled;
    }

    public async Task RefreshDesktopStateAsync()
    {
        LaunchOnStartup = await _desktop.IsStartupEnabledAsync();
        AreDesktopIconsHidden = await _desktop.AreDesktopIconsHiddenAsync();
    }

    private Task<string?> ReadSettingAsync(string key, StartupSettingsSnapshot? snapshot)
        => snapshot is not null ? Task.FromResult(snapshot.Get(key)) : _settings.GetSettingAsync(key);

    public IAsyncRelayCommand ApplyMoeThemeCommand { get; }

    public IAsyncRelayCommand ApplyGlassThemeCommand { get; }

    public IAsyncRelayCommand ApplyCrystalThemeCommand { get; }

    public IRelayCommand ResetThemeTransparencyCommand { get; }

    public IAsyncRelayCommand ToggleLaunchOnStartupCommand { get; }

    public IAsyncRelayCommand ToggleDesktopIconsCommand { get; }

    public IAsyncRelayCommand ToggleDesktopDoubleClickCommand { get; }

    public IAsyncRelayCommand ToggleEditorOpacityFollowCommand { get; }

    public IAsyncRelayCommand ToggleIconToolTipCompactCommand { get; }

    public IAsyncRelayCommand ToggleAutoHideEnabledCommand { get; }

    public IAsyncRelayCommand ApplyAutoHideScopeHoveredOnlyCommand { get; }

    public IAsyncRelayCommand ApplyAutoHideScopeAllCommand { get; }

    public string ThemeLabel
    {
        get => _themeLabel;
        private set => SetProperty(ref _themeLabel, value);
    }

    public AppTheme CurrentTheme
    {
        get => _currentTheme;
        private set
        {
            if (SetProperty(ref _currentTheme, value))
            {
                OnPropertyChanged(nameof(IsMoeTheme));
                OnPropertyChanged(nameof(IsGlassTheme));
                OnPropertyChanged(nameof(IsCrystalTheme));
            }
        }
    }

    public bool IsMoeTheme => CurrentTheme == AppTheme.Moe;

    public bool IsGlassTheme => CurrentTheme == AppTheme.Glass;

    public bool IsCrystalTheme => CurrentTheme == AppTheme.Crystal;

    public double ThemeTransparencyPercent
    {
        get => _themeTransparencyPercent;
        set
        {
            if (!double.IsFinite(value))
            {
                return;
            }

            var normalized = Math.Clamp(
                Math.Round(value),
                0,
                (1 - AppThemeManager.MinimumBoxOpacity) * 100);
            if (SetProperty(ref _themeTransparencyPercent, normalized))
            {
                OnPropertyChanged(nameof(ThemeTransparencyLabel));
                var opacity = 1 - (normalized / 100);
                if (!_isSynchronizingThemeTransparency)
                {
                    AppThemeManager.SetBoxOpacity(CurrentTheme, opacity);
                    SynchronizeThemeTransparency();
                    QueueThemeOpacitySave(GetThemeBoxOpacitySettingKey(CurrentTheme), FormatOpacity(opacity));
                }
            }
        }
    }

    public string ThemeTransparencyLabel => $"{ThemeTransparencyPercent:0}%";

    public double BoxBorderTransparencyPercent
    {
        get => _boxBorderTransparencyPercent;
        set => SetAppearanceTransparency(ref _boxBorderTransparencyPercent, value,
            nameof(BoxBorderTransparencyPercent), BoxBorderOpacitySettingKeyPrefix,
            AppThemeManager.SetBoxBorderOpacity);
    }

    public double IconFrameTransparencyPercent
    {
        get => _iconFrameTransparencyPercent;
        set => SetAppearanceTransparency(ref _iconFrameTransparencyPercent, value,
            nameof(IconFrameTransparencyPercent), IconFrameOpacitySettingKeyPrefix,
            AppThemeManager.SetIconFrameOpacity);
    }

    public bool EditorFollowsBoxOpacity
    {
        get => _editorFollowsBoxOpacity;
        private set => SetProperty(ref _editorFollowsBoxOpacity, value);
    }

    /// <summary>
    /// 图标名称（悬停提示）显示模式。<see langword="false"/> = 完整显示（文件路径），
    /// <see langword="true"/> = 精简显示（文件名，快捷方式自动去掉 .lnk）。
    /// </summary>
    public bool IconToolTipCompact
    {
        get => _iconToolTipCompact;
        private set => SetProperty(ref _iconToolTipCompact, value);
    }

    public bool AutoHideEnabled
    {
        get => _autoHideEnabled;
        private set => SetProperty(ref _autoHideEnabled, value);
    }

    public int AutoHideHiddenTransparencyPercent
    {
        get => _autoHideHiddenTransparencyPercent;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref _autoHideHiddenTransparencyPercent, clamped))
            {
                return;
            }

            // 立即应用（拖动滑块时实时预览），仅持久化走防抖。
            PublishAutoHideSettings();
            QueueAutoHideSave();
        }
    }

    public AutoHideRevealScope AutoHideRevealScope
    {
        get => _autoHideRevealScope;
        private set
        {
            if (!SetProperty(ref _autoHideRevealScope, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsAutoHideScopeHoveredOnly));
            OnPropertyChanged(nameof(IsAutoHideScopeAll));
        }
    }

    public bool IsAutoHideScopeHoveredOnly => AutoHideRevealScope == AutoHideRevealScope.HoveredBoxOnly;

    public bool IsAutoHideScopeAll => AutoHideRevealScope == AutoHideRevealScope.AllBoxes;

    /// <summary>
    /// 是否在自动隐藏时连同收纳盒外壳（背景/边框/阴影）一起透明。与内容透明相互独立。
    /// </summary>
    public bool AutoHideFadeWholeBox
    {
        get => _autoHideFadeWholeBox;
        set
        {
            if (SetProperty(ref _autoHideFadeWholeBox, value))
            {
                PublishAutoHideSettings();
                QueueAutoHideSave();
            }
        }
    }

    /// <summary>
    /// 是否在自动隐藏时连同收纳盒标题一起透明。与内容透明相互独立。
    /// </summary>
    public bool AutoHideFadeTitle
    {
        get => _autoHideFadeTitle;
        set
        {
            if (SetProperty(ref _autoHideFadeTitle, value))
            {
                PublishAutoHideSettings();
                QueueAutoHideSave();
            }
        }
    }

    /// <summary>
    /// 是否在自动隐藏时连同收纳盒边框（描边）一起透明。与内容透明相互独立。
    /// </summary>
    public bool AutoHideFadeBorder
    {
        get => _autoHideFadeBorder;
        set
        {
            if (SetProperty(ref _autoHideFadeBorder, value))
            {
                PublishAutoHideSettings();
                QueueAutoHideSave();
            }
        }
    }

    public bool LaunchOnStartup
    {
        get => _launchOnStartup;
        private set => SetProperty(ref _launchOnStartup, value);
    }

    public bool AreDesktopIconsHidden
    {
        get => _areDesktopIconsHidden;
        private set => SetProperty(ref _areDesktopIconsHidden, value);
    }

    public bool IsDesktopDoubleClickEnabled
    {
        get => _isDesktopDoubleClickEnabled;
        private set => SetProperty(ref _isDesktopDoubleClickEnabled, value);
    }

    private void SetAppearanceTransparency(ref double field, double value, string propertyName,
        string settingPrefix, Action<AppTheme, double?> apply)
    {
        if (!double.IsFinite(value))
        {
            return;
        }

        var normalized = Math.Clamp(Math.Round(value), 0, 100);
        if (SetProperty(ref field, normalized, propertyName) && !_isSynchronizingThemeTransparency)
        {
            var opacity = 1 - normalized / 100;
            apply(CurrentTheme, opacity);
            QueueThemeOpacitySave(settingPrefix + CurrentTheme, FormatOpacity(opacity));
        }
    }

    private void ResetThemeTransparency()
    {
        var theme = CurrentTheme;
        var opacity = AppThemeManager.GetDefaultBoxOpacity(theme);
        AppThemeManager.SetBoxOpacity(theme, opacity);
        AppThemeManager.SetBoxBorderOpacity(theme, null);
        AppThemeManager.SetIconFrameOpacity(theme, null);
        SynchronizeThemeTransparency(refreshControls: true);
        QueueThemeOpacitySave(GetThemeBoxOpacitySettingKey(theme), FormatOpacity(opacity));
        QueueThemeOpacitySave(BoxBorderOpacitySettingKeyPrefix + theme, null);
        QueueThemeOpacitySave(IconFrameOpacitySettingKeyPrefix + theme, null);
    }

    private bool TryResetThemeTransparencyField(string? field)
    {
        var theme = CurrentTheme;
        switch (field)
        {
            case nameof(ThemeTransparencyPercent):
                var opacity = AppThemeManager.GetDefaultBoxOpacity(theme);
                AppThemeManager.SetBoxOpacity(theme, opacity);
                QueueThemeOpacitySave(GetThemeBoxOpacitySettingKey(theme), FormatOpacity(opacity));
                break;
            case nameof(BoxBorderTransparencyPercent):
                AppThemeManager.SetBoxBorderOpacity(theme, null);
                QueueThemeOpacitySave(BoxBorderOpacitySettingKeyPrefix + theme, null);
                break;
            case nameof(IconFrameTransparencyPercent):
                AppThemeManager.SetIconFrameOpacity(theme, null);
                QueueThemeOpacitySave(IconFrameOpacitySettingKeyPrefix + theme, null);
                break;
            default:
                return false;
        }

        SynchronizeThemeTransparency(refreshControls: true);
        return true;
    }

    private async Task ApplyThemeAsync(AppTheme theme)
    {
        try
        {
            AppThemeManager.Apply(theme);
            SetCurrentTheme(theme);
            await _settings.SetSettingAsync(ThemeSettingKey, theme.ToString());
            StatusText = Strings.Format("SwitchedToTheTheme", ThemeLabel);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to apply theme.");
            StatusText = exception.Message;
        }
    }

    private void SetCurrentTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        SynchronizeThemeTransparency();
        UpdateThemeLabel();
    }

    private void UpdateThemeLabel()
    {
        ThemeLabel = CurrentTheme switch
        {
            AppTheme.Glass => Strings.Get("Obsidian"),
            AppTheme.Crystal => Strings.Get("Crystal"),
            _ => Strings.Get("Light")
        };
    }

    private async Task RestoreThemeBoxOpacitiesAsync(StartupSettingsSnapshot? startupSnapshot = null)
    {
        var migrationVersion =
            await ReadSettingAsync(ThemeBoxOpacityMigrationVersionSettingKey, startupSnapshot);
        if (string.Equals(
                migrationVersion,
                ThemeBoxOpacityMigrationVersion,
                StringComparison.Ordinal))
        {
            foreach (var theme in Enum.GetValues<AppTheme>())
            {
                var savedOpacity = await ReadSettingAsync(GetThemeBoxOpacitySettingKey(theme), startupSnapshot);
                AppThemeManager.SetBoxOpacity(theme, ParseSavedOpacity(savedOpacity, AppThemeManager.GetDefaultBoxOpacity(theme)));
            }

            SynchronizeThemeTransparency();
            return;
        }

        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            var opacity = AppThemeManager.GetDefaultBoxOpacity(theme);
            if (string.Equals(migrationVersion, "1", StringComparison.Ordinal))
            {
                var savedOpacity = ParseSavedOpacity(
                    await ReadSettingAsync(GetThemeBoxOpacitySettingKey(theme), startupSnapshot));
                if (!IsVersionOneGeneratedDefault(savedOpacity))
                {
                    opacity = savedOpacity;
                }
            }

            AppThemeManager.SetBoxOpacity(theme, opacity);
            await _settings.SetSettingAsync(
                GetThemeBoxOpacitySettingKey(theme),
                FormatOpacity(opacity));
        }

        await _settings.SetSettingAsync(
            ThemeBoxOpacityMigrationVersionSettingKey,
            ThemeBoxOpacityMigrationVersion);
        SynchronizeThemeTransparency();
    }

    private async Task ToggleEditorOpacityFollowAsync()
    {
        try
        {
            var enabled = !EditorFollowsBoxOpacity;
            await _settings.SetSettingAsync(
                EditorFollowsBoxOpacitySettingKey,
                enabled.ToString());
            EditorFollowsBoxOpacity = enabled;
            StatusText = enabled
                ? Strings.Get("EditorNowFollowsDesktopBoxOpacity")
                : Strings.Get("EditorNowUsesStandardOpacity");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save editor opacity follow setting.");
            OnPropertyChanged(nameof(EditorFollowsBoxOpacity));
            StatusText = exception.Message;
        }
    }

    private async Task RestoreAppearanceOpacitiesAsync(StartupSettingsSnapshot? startupSnapshot = null)
    {
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            AppThemeManager.SetBoxBorderOpacity(theme, ParseAppearanceOpacity(
                await ReadSettingAsync(BoxBorderOpacitySettingKeyPrefix + theme, startupSnapshot)));
            AppThemeManager.SetIconFrameOpacity(theme, ParseAppearanceOpacity(
                await ReadSettingAsync(IconFrameOpacitySettingKeyPrefix + theme, startupSnapshot)));
        }

        SynchronizeThemeTransparency();
    }

    private static bool IsVersionOneGeneratedDefault(double opacity)
    {
        return Math.Abs(opacity - AppThemeManager.DefaultBoxOpacity) < 0.0001
            || Math.Abs(opacity - AppThemeManager.MaximumBoxOpacity) < 0.0001;
    }

    private void SynchronizeThemeTransparency(bool refreshControls = false)
    {
        _isSynchronizingThemeTransparency = true;
        try
        {
            ThemeTransparencyPercent =
                Math.Round((1 - AppThemeManager.GetBoxOpacity(CurrentTheme)) * 100);
            BoxBorderTransparencyPercent =
                Math.Round((1 - AppThemeManager.GetBoxBorderOpacity(CurrentTheme)) * 100);
            IconFrameTransparencyPercent =
                Math.Round((1 - AppThemeManager.GetIconFrameOpacity(CurrentTheme)) * 100);
            if (refreshControls)
            {
                // Explicit resets also refresh controls when the source values
                // already equal the defaults.
                OnPropertyChanged(nameof(ThemeTransparencyPercent));
                OnPropertyChanged(nameof(BoxBorderTransparencyPercent));
                OnPropertyChanged(nameof(IconFrameTransparencyPercent));
            }
        }
        finally
        {
            _isSynchronizingThemeTransparency = false;
        }
        RefreshCustomization();
    }

    private void QueueThemeOpacitySave(string settingKey, string? value)
    {
        CancellationTokenSource delay;
        lock (_themeOpacitySaveLock)
        {
            if (_themeOpacitySaveDelays.TryGetValue(settingKey, out var previousDelay))
            {
                previousDelay.Cancel();
            }

            delay = new CancellationTokenSource();
            _themeOpacitySaveDelays[settingKey] = delay;
            _pendingThemeSettings[settingKey] = value;
        }

        _ = PersistThemeOpacityAfterDelayAsync(settingKey, value, delay);
    }

    private async Task PersistThemeOpacityAfterDelayAsync(
        string settingKey,
        string? value,
        CancellationTokenSource delay)
    {
        var saved = false;
        try
        {
            await Task.Delay(250, delay.Token).ConfigureAwait(false);
            await _themeOpacityWriteGate.WaitAsync(delay.Token).ConfigureAwait(false);
            try
            {
                delay.Token.ThrowIfCancellationRequested();
                if (value is null)
                {
                    await _settings.DeleteSettingAsync(settingKey, delay.Token).ConfigureAwait(false);
                }
                else
                {
                    await _settings.SetSettingAsync(settingKey, value, delay.Token).ConfigureAwait(false);
                }
                saved = true;
            }
            finally
            {
                _themeOpacityWriteGate.Release();
            }
        }
        catch (OperationCanceledException) when (delay.IsCancellationRequested)
        {
            // 连续拖动时只保存停止后的最终值。
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Failed to persist {settingKey}.");
        }
        finally
        {
            lock (_themeOpacitySaveLock)
            {
                if (_themeOpacitySaveDelays.TryGetValue(settingKey, out var currentDelay)
                    && ReferenceEquals(currentDelay, delay))
                {
                    _themeOpacitySaveDelays.Remove(settingKey);
                    if (saved) _pendingThemeSettings.Remove(settingKey);
                }
            }

            delay.Dispose();
        }
    }

    internal static string GetThemeBoxOpacitySettingKey(AppTheme theme)
    {
        return ThemeBoxOpacitySettingKeyPrefix + theme;
    }

    private static string FormatOpacity(double opacity)
    {
        return opacity.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static double ParseSavedOpacity(string? value, double fallback = AppThemeManager.DefaultBoxOpacity)
    {
        return double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var opacity)
            ? Math.Clamp(
                opacity,
                AppThemeManager.MinimumBoxOpacity,
                AppThemeManager.MaximumBoxOpacity)
            : fallback;
    }
    private async Task ToggleLaunchOnStartupAsync()
    {
        try
        {
            var newState = !LaunchOnStartup;
            await _desktop.SetStartupEnabledAsync(newState);
            LaunchOnStartup = newState;
            StatusText = newState ? Strings.Get("LaunchAtStartupEnabled") : Strings.Get("LaunchAtStartupDisabled");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to toggle startup registry key.");
            StatusText = exception.Message;
        }
    }

    private async Task ToggleDesktopIconsAsync()
    {
        try
        {
            // 原生层以窗口实际可见性为基准切换并返回真实结果，
            // 不依赖本缓存的旧值（外部从桌面菜单改过状态时缓存会失步）。
            var hidden = await _desktop.ToggleDesktopIconsAsync();
            AreDesktopIconsHidden = hidden;
            StatusText = hidden ? Strings.Get("WindowsDesktopIconsHidden") : Strings.Get("WindowsDesktopIconsShown");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to toggle Windows desktop icons.");
            StatusText = exception.Message;
        }
    }

    private async Task ToggleDesktopDoubleClickAsync()
    {
        try
        {
            var enabled = !IsDesktopDoubleClickEnabled;
            await _settings.SetSettingAsync(
                DesktopDoubleClickSettingKey,
                enabled.ToString());
            IsDesktopDoubleClickEnabled = enabled;
            StatusText = enabled ? Strings.Get("DesktopDoubleClickToggleEnabled") : Strings.Get("DesktopDoubleClickToggleDisabled");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save desktop double-click setting.");
            OnPropertyChanged(nameof(IsDesktopDoubleClickEnabled));
            StatusText = exception.Message;
        }
    }

    private async Task ToggleIconToolTipCompactAsync()
    {
        try
        {
            var compact = !IconToolTipCompact;
            await _settings.SetSettingAsync(
                IconToolTipCompactSettingKey,
                compact.ToString());
            IconToolTipCompact = compact;
            PublishIconToolTipMode();
            StatusText = compact
                ? Strings.Get("IconTooltipsNowShowFileNames")
                : Strings.Get("IconTooltipsNowShowFullPaths");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save icon tooltip compact setting.");
            OnPropertyChanged(nameof(IconToolTipCompact));
            StatusText = exception.Message;
        }
    }

    private void PublishIconToolTipMode()
    {
        WeakReferenceMessenger.Default.Send(
            new IconToolTipModeChangedMessage(IconToolTipCompact));
    }

    private async Task ToggleAutoHideEnabledAsync()
    {
        try
        {
            var enabled = !AutoHideEnabled;
            AutoHideEnabled = enabled;
            PublishAutoHideSettings();
            await SaveAutoHideSettingsAsync();
            StatusText = enabled ? Strings.Get("AutoHideEnabled") : Strings.Get("AutoHideDisabled");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save auto hide enable setting.");
            OnPropertyChanged(nameof(AutoHideEnabled));
            StatusText = exception.Message;
        }
    }

    private async Task ApplyAutoHideRevealScopeAsync(AutoHideRevealScope scope)
    {
        try
        {
            AutoHideRevealScope = scope;
            PublishAutoHideSettings();
            await SaveAutoHideSettingsAsync();
            StatusText = scope == AutoHideRevealScope.AllBoxes
                ? Strings.Get("HoverOverAnyBoxToRevealAllBoxes")
                : Strings.Get("HoverOverABoxToRevealOnlyItsContents");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save auto hide reveal scope setting.");
            OnPropertyChanged(nameof(AutoHideRevealScope));
            StatusText = exception.Message;
        }
    }

    private Task SaveAutoHideSettingsAsync()
    {
        return _autoHideSettingsStore.SaveAsync(new AutoHideSettings(
            AutoHideEnabled,
            AutoHideHiddenTransparencyPercent,
            AutoHideRevealScope,
            AutoHideFadeWholeBox,
            AutoHideFadeTitle,
            AutoHideFadeBorder));
    }

    private void PublishAutoHideSettings()
    {
        WeakReferenceMessenger.Default.Send(new AutoHideSettingsChangedMessage(
            AutoHideEnabled,
            AutoHideHiddenTransparencyPercent,
            AutoHideRevealScope,
            AutoHideFadeWholeBox,
            AutoHideFadeTitle,
            AutoHideFadeBorder));
    }

    private void QueueAutoHideSave()
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _autoHideSaveCts, next);
        // 只取消不立即 Dispose：旧任务可能仍挂在该 token 的 Task.Delay 上，
        // 此时 Dispose 会让其回调注册抛出 ObjectDisposedException（被误记为保存失败）。
        // 已取消且无注册的 CancellationTokenSource 由 GC 回收即可。
        previous?.Cancel();

        _ = PersistAutoHideAfterDelayAsync(next.Token);
    }

    private async Task PersistAutoHideAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // 应用已在属性 setter 中即时完成，这里只负责持久化。
            await SaveAutoHideSettingsAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save auto hide settings.");
        }
    }

    private static double? ParseAppearanceOpacity(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity)
        && double.IsFinite(opacity) && opacity >= 0 && opacity <= 1 ? opacity : null;
}
