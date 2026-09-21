using System.Globalization;
using System.Threading;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.ViewModels;

// 主题切换与各层透明度的同步、恢复与持久化。
public sealed partial class MainViewModel
{
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
        SynchronizeThemeTransparency();
        QueueThemeOpacitySave(GetThemeBoxOpacitySettingKey(theme), FormatOpacity(opacity));
        QueueThemeOpacitySave(BoxBorderOpacitySettingKeyPrefix + theme, null);
        QueueThemeOpacitySave(IconFrameOpacitySettingKeyPrefix + theme, null);
    }

    private async Task ApplyThemeAsync(AppTheme theme)
    {
        try
        {
            AppThemeManager.Apply(theme);
            SetCurrentTheme(theme);
            await _drawerService.SetSettingAsync(ThemeSettingKey, theme.ToString());
            StatusText = $"已切换到 {ThemeLabel} 风格";
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
            AppTheme.Glass => "暗黑曜石",
            AppTheme.Crystal => "全透水晶",
            _ => "清透雅致"
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
                AppThemeManager.SetBoxOpacity(theme, ParseSavedOpacity(savedOpacity));
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
            await _drawerService.SetSettingAsync(
                GetThemeBoxOpacitySettingKey(theme),
                FormatOpacity(opacity));
        }

        await _drawerService.SetSettingAsync(
            ThemeBoxOpacityMigrationVersionSettingKey,
            ThemeBoxOpacityMigrationVersion);
        SynchronizeThemeTransparency();
    }

    private async Task ToggleEditorOpacityFollowAsync()
    {
        try
        {
            var enabled = !EditorFollowsBoxOpacity;
            await _drawerService.SetSettingAsync(
                EditorFollowsBoxOpacitySettingKey,
                enabled.ToString());
            EditorFollowsBoxOpacity = enabled;
            StatusText = enabled
                ? "编辑页已跟随桌面盒子透明度"
                : "编辑页已保持标准透明度";
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

    private void SynchronizeThemeTransparency()
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
        }
        finally
        {
            _isSynchronizingThemeTransparency = false;
        }
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
        }

        _ = PersistThemeOpacityAfterDelayAsync(settingKey, value, delay);
    }

    private async Task PersistThemeOpacityAfterDelayAsync(
        string settingKey,
        string? value,
        CancellationTokenSource delay)
    {
        try
        {
            await Task.Delay(250, delay.Token).ConfigureAwait(false);
            await _themeOpacityWriteGate.WaitAsync(delay.Token).ConfigureAwait(false);
            try
            {
                delay.Token.ThrowIfCancellationRequested();
                if (value is null)
                {
                    await _drawerService.DeleteSettingAsync(settingKey, delay.Token).ConfigureAwait(false);
                }
                else
                {
                    await _drawerService.SetSettingAsync(settingKey, value, delay.Token).ConfigureAwait(false);
                }
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

    private static double ParseSavedOpacity(string? value)
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
            : AppThemeManager.DefaultBoxOpacity;
    }
}
