using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class ThemeCustomizationSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{\"Version\":2,\"Accent\":\"#123456\"}")]
    public void MissingMalformedOrFuturePayload_InheritsPreset(string? value)
        => Assert.Equal(ThemeCustomization.Empty, ThemeCustomizationSettings.Deserialize(value));

    [Fact]
    public void Normalize_ValidatesIndependentFieldsAndDoesNotStoreDefaults()
    {
        var value = new ThemeCustomization
        {
            BoxCornerRadius = 90, IconCornerScale = -3, ItemHoverOpacity = double.NaN,
            Accent = " af52de ", BoxBackground = "#AAFFFFFF", IconBackground = "invalid"
        }.Normalize();
        Assert.Equal(32, value.BoxCornerRadius);
        Assert.Equal(0, value.IconCornerScale);
        Assert.Null(value.ItemHoverOpacity);
        Assert.Null(value.BoxBackground);
        Assert.Null(value.IconBackground);
        Assert.Equal("#AF52DE", value.Accent);
        Assert.Null(ThemeCustomizationSettings.Serialize(ThemeCustomization.Empty));
        var json = ThemeCustomizationSettings.Serialize(new ThemeCustomization { Accent = "#123456" });
        Assert.DoesNotContain("BoxCornerRadius", json);
        Assert.Equal(new ThemeCustomization { Accent = "#123456" }, ThemeCustomizationSettings.Deserialize(json));
    }

    [Fact]
    public async Task SQLite_ReloadsThemesIndependentlyAndResetDeletesOnlyOneOverride()
    {
        var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Theme", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "settings.db");
            var repository = new DrawerRepository(path);
            await repository.InitializeAsync();
            var settings = new SettingsService(repository);
            var moe = new ThemeCustomization { BoxCornerRadius = 0, IconCornerScale = 1.25, Accent = "#AF52DE", ItemHoverOpacity = 0.35789 };
            var glass = new ThemeCustomization { BoxBackground = "#123456" };
            await settings.SetSettingAsync("ThemeCustomization.Moe", ThemeCustomizationSettings.Serialize(moe)!);
            await settings.SetSettingAsync("ThemeCustomization.Glass", ThemeCustomizationSettings.Serialize(glass)!);
            var reloaded = new SettingsService(new DrawerRepository(path));
            Assert.Equal(moe, ThemeCustomizationSettings.Deserialize(await reloaded.GetSettingAsync("ThemeCustomization.Moe")));
            Assert.Equal(glass, ThemeCustomizationSettings.Deserialize(await reloaded.GetSettingAsync("ThemeCustomization.Glass")));
            Assert.Equal(ThemeCustomization.Empty, ThemeCustomizationSettings.Deserialize(await reloaded.GetSettingAsync("ThemeCustomization.Crystal")));
            await reloaded.DeleteSettingAsync("ThemeCustomization.Moe");
            Assert.Equal(ThemeCustomization.Empty, ThemeCustomizationSettings.Deserialize(await settings.GetSettingAsync("ThemeCustomization.Moe")));
            Assert.Equal(glass, ThemeCustomizationSettings.Deserialize(await settings.GetSettingAsync("ThemeCustomization.Glass")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
