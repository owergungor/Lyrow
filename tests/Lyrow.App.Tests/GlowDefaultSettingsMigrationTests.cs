using System.IO;
using System.Text.Json;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

public class GlowDefaultSettingsMigrationTests
{
    [Fact]
    public void DeserializingEmptyJson_AppliesV26Defaults()
    {
        string emptyJson = "{}";
        var settings = JsonSerializer.Deserialize<LyrowSettings>(emptyJson);

        Assert.NotNull(settings);
        Assert.Equal(80, settings.GlowIntensity);
        Assert.Equal(48, settings.GlowThickness);
        Assert.Equal(2.5f, settings.Glow.Sensitivity);
    }

    [Fact]
    public void DeserializingExistingCustomValues_PreservesUserPreferences()
    {
        string customJson = """
        {
            "GlowIntensity": 95,
            "GlowThickness": 120,
            "Glow": {
                "Sensitivity": 3.2
            }
        }
        """;

        var settings = JsonSerializer.Deserialize<LyrowSettings>(customJson);

        Assert.NotNull(settings);
        // Preserves user-chosen values
        Assert.Equal(95, settings.GlowIntensity);
        Assert.Equal(120, settings.GlowThickness);
        Assert.Equal(3.2f, settings.Glow.Sensitivity);
    }
}
