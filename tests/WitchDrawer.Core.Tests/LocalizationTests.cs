using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using WitchDrawer.Core.Localization;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData(null, "zh-CN", "zh-CN")]
    [InlineData(null, "zh-TW", "zh-CN")]
    [InlineData(null, "en-US", "en")]
    [InlineData(null, "de-DE", "en")]
    [InlineData("invalid", "zh-CN", "zh-CN")]
    [InlineData("en", "zh-CN", "en")]
    [InlineData("zh-CN", "en-US", "zh-CN")]
    public void LanguageChoice_PrefersSavedLanguageAndFallsBackToSystem(
        string? saved, string system, string expected) =>
        Assert.Equal(expected, AppLanguage.Resolve(saved, CultureInfo.GetCultureInfo(system)));

    [Fact]
    public void Translations_HaveMatchingKeysAndFormattingArguments()
    {
        var resources = new ResourceManager("WitchDrawer.Core.Localization.Strings", typeof(Strings).Assembly);
        var english = Read(resources, "en");
        var chinese = Read(resources, "zh-CN");
        Assert.True(english.Count > 500);
        Assert.Equal(english.Keys.Order(), chinese.Keys.Order());
        foreach (var (key, value) in english)
        {
            Assert.False(string.IsNullOrWhiteSpace(value), key);
            Assert.False(string.IsNullOrWhiteSpace(chinese[key]), key);
            var format = CompositeFormat.Parse(value);
            var translatedFormat = CompositeFormat.Parse(chinese[key]);
            Assert.Equal(format.MinimumArgumentCount, translatedFormat.MinimumArgumentCount);
            var arguments = Enumerable.Repeat<object>(42, format.MinimumArgumentCount).ToArray();
            _ = string.Format(CultureInfo.GetCultureInfo("en"), format, arguments);
            _ = string.Format(CultureInfo.GetCultureInfo("zh-CN"), translatedFormat, arguments);
        }
    }

    [Fact]
    public void NeutralResources_FallBackToEnglish()
    {
        Assert.Equal("Settings", Strings.Get("Settings", CultureInfo.GetCultureInfo("fr-FR")));
        Assert.Equal("设置", Strings.Get("Settings", CultureInfo.GetCultureInfo("zh-CN")));
    }

    [Theory]
    [InlineData(AppLanguage.English)]
    [InlineData(AppLanguage.SimplifiedChinese)]
    public async Task LanguageSetting_SurvivesReopeningSqlite(string language)
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Localization", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "settings.db");
            var repository = new DrawerRepository(path);
            await repository.InitializeAsync();
            var settings = new SettingsService(repository);
            await settings.SetSettingAsync(AppLanguage.SettingKey, language);
            var reopened = new SettingsService(new DrawerRepository(path));
            Assert.Equal(language, await reopened.GetSettingAsync(AppLanguage.SettingKey));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static Dictionary<string, string> Read(ResourceManager manager, string culture) =>
        manager.GetResourceSet(CultureInfo.GetCultureInfo(culture), true, true)!
            .Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
}
