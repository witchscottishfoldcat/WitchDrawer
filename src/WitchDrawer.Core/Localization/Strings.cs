using System.Globalization;
using System.Resources;

namespace WitchDrawer.Core.Localization;

/// <summary>Shared UI and service messages. English is the neutral resource fallback.</summary>
public static class Strings
{
    private static readonly ResourceManager Resources = new(
        "WitchDrawer.Core.Localization.Strings", typeof(Strings).Assembly);
    private static CultureInfo _culture = CultureInfo.GetCultureInfo(
        AppLanguage.Resolve(null, CultureInfo.CurrentUICulture));

    public static CultureInfo Culture => Volatile.Read(ref _culture);
    public static event EventHandler? LanguageChanged;

    public static void SetLanguage(string language)
    {
        if (!AppLanguage.IsSupported(language))
            throw new ArgumentException("Unsupported application language.", nameof(language));
        var culture = CultureInfo.GetCultureInfo(language);
        var previous = Interlocked.Exchange(ref _culture, culture);
        if (previous.Name != culture.Name) LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string key) => Get(key, Culture);

    public static string Get(string key, CultureInfo culture) =>
        Resources.GetString(key, culture) ?? throw new MissingManifestResourceException($"Missing language resource: {key}");

    public static string Format(string key, params object?[] arguments) =>
        string.Format(Culture, Get(key), arguments);
}
