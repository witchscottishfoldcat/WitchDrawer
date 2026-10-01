using System.Text.Json;
using System.Text.Json.Serialization;
using WitchDrawer.Core.Models;

namespace WitchDrawer.Core.Services;

/// <summary>Versioned settings payload, persisted by the existing background ISettingsStore.</summary>
public static class ThemeCustomizationSettings
{
    public const string KeyPrefix = "ThemeCustomization.";
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string? Serialize(ThemeCustomization customization)
    {
        var normalized = customization.Normalize();
        return normalized.IsEmpty ? null : JsonSerializer.Serialize(normalized, Options);
    }

    public static ThemeCustomization Deserialize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ThemeCustomization.Empty;
        try
        {
            return (JsonSerializer.Deserialize<ThemeCustomization>(value, Options)
                ?? ThemeCustomization.Empty).Normalize();
        }
        catch (JsonException)
        {
            return ThemeCustomization.Empty;
        }
    }
}
