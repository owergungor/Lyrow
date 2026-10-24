using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

public class GlowDefaultSettingsTests
{
    [Fact]
    public void DefaultSettings_HaveCorrectV26GlowStandards()
    {
        var settings = LyrowSettings.CreateDefault();

        // Brightness = 80%
        Assert.Equal(80, settings.GlowIntensity);

        // Thickness = 48px
        Assert.Equal(48, settings.GlowThickness);

        // Reactivity = 250% (Sensitivity = 2.5f)
        Assert.Equal(2.5f, settings.Glow.Sensitivity);
    }

    [Fact]
    public void GlowModuleSettings_DefaultSensitivity_IsTwoHundredFiftyPercent()
    {
        var glow = new GlowModuleSettings();
        Assert.Equal(2.5f, glow.Sensitivity);
    }
}
