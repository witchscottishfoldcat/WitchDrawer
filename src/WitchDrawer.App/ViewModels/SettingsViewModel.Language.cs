using CommunityToolkit.Mvvm.Input;
using WitchDrawer.App.Localization;
using WitchDrawer.Core.Localization;

namespace WitchDrawer.App.ViewModels;

public sealed partial class SettingsViewModel
{
    public string Language => LocalizationProvider.Instance.Language;
    public bool IsEnglish => Language == AppLanguage.English;
    public bool IsSimplifiedChinese => Language == AppLanguage.SimplifiedChinese;
    public IAsyncRelayCommand<string> ChangeLanguageCommand { get; private set; } = null!;

    private void InitializeLanguage() => ChangeLanguageCommand = new AsyncRelayCommand<string>(ChangeLanguageAsync);

    private async Task ChangeLanguageAsync(string? language)
    {
        if (!AppLanguage.IsSupported(language)) return;
        try
        {
            // Save first: a failed write keeps both the current UI and the saved choice unchanged.
            await _settings.SetSettingAsync(AppLanguage.SettingKey, language!);
            LocalizationProvider.Instance.Apply(language!);
            StatusText = Strings.Get("LanguageChanged");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to change application language.");
            StatusText = Strings.Get("LanguageSaveFailed");
            OnPropertyChanged(nameof(IsEnglish));
            OnPropertyChanged(nameof(IsSimplifiedChinese));
        }
    }

    protected override void OnLanguageChanged() => UpdateThemeLabel();
}
