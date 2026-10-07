using System.Globalization;

namespace WitchDrawer.Core.Localization;

public static class AppLanguage
{
    public const string SettingKey = "Language";
    public const string English = "en";
    public const string SimplifiedChinese = "zh-CN";

    public static bool IsSupported(string? language) => language is English or SimplifiedChinese;

    /// <summary>Use an explicit choice when present; otherwise follow the system UI language.</summary>
    public static string Resolve(string? savedLanguage, CultureInfo systemCulture)
    {
        ArgumentNullException.ThrowIfNull(systemCulture);
        if (IsSupported(savedLanguage)) return savedLanguage!;
        return systemCulture.TwoLetterISOLanguageName == "zh" ? SimplifiedChinese : English;
    }
}
