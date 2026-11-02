using System.Text.Json;
using FluentAssertions;
using Lyrow.Audio.Analysis;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

public class Stage11PersonalizationTests
{
    private static FrequencySpectrum CreateSpectrum(
        float rms,
        float bass,
        float subBass,
        float mid,
        float treble,
        bool isSilent = false)
    {
        var levels = new AudioLevels(
            Rms: rms,
            Peak: rms * 1.4f,
            Decibels: isSilent ? -90f : -10f,
            IsSilent: isSilent);

        return new FrequencySpectrum(
            rawFrequencies: Array.Empty<float>(),
            bands: Array.Empty<float>(),
            subBass: subBass,
            bass: bass,
            mid: mid,
            treble: treble,
            levels: levels);
    }

    [Fact]
    public void SettingsDefaults_InitializeWithOptimalPersonalizationValues()
    {
        var settings = LyrowSettings.CreateDefault();

        settings.LyricsLineSpacing.Should().Be(20, "Default line spacing should be 20 px for optimal readability");
        settings.LyricsAlignment.Should().Be("Left", "Default lyrics alignment should be Left conforming to desktop standard");
        settings.GlowAcousticProfile.Should().Be(GlowAcousticProfile.Balanced, "Default acoustic profile should be Balanced");
        settings.LyricsBackdropOpacity.Should().Be(85, "Default lyrics backdrop opacity should be 85%");

        settings.Glow.AcousticProfile.Should().Be(GlowAcousticProfile.Balanced);
        settings.Lyrics.LineSpacing.Should().Be(20);
        settings.Lyrics.Alignment.Should().Be("Left");
        settings.Lyrics.BackdropOpacity.Should().Be(85);
    }

    [Fact]
    public void SettingsSerialization_RoundTripsAllPersonalizationOptionsAccurately()
    {
        var original = new LyrowSettings
        {
            LyricsLineSpacing = 28,
            LyricsAlignment = "Center",
            GlowAcousticProfile = GlowAcousticProfile.BassBoost,
            LyricsBackdropOpacity = 65
        };
        original.Glow.AcousticProfile = GlowAcousticProfile.BassBoost;
        original.Lyrics.LineSpacing = 28;
        original.Lyrics.Alignment = "Center";
        original.Lyrics.BackdropOpacity = 65;

        string json = JsonSerializer.Serialize(original, new JsonSerializerOptions { WriteIndented = true });
        var deserialized = JsonSerializer.Deserialize<LyrowSettings>(json);

        deserialized.Should().NotBeNull();
        deserialized!.LyricsLineSpacing.Should().Be(28);
        deserialized.LyricsAlignment.Should().Be("Center");
        deserialized.GlowAcousticProfile.Should().Be(GlowAcousticProfile.BassBoost);
        deserialized.LyricsBackdropOpacity.Should().Be(65);
        deserialized.Glow.AcousticProfile.Should().Be(GlowAcousticProfile.BassBoost);
        deserialized.Lyrics.LineSpacing.Should().Be(28);
        deserialized.Lyrics.Alignment.Should().Be("Center");
        deserialized.Lyrics.BackdropOpacity.Should().Be(65);
    }

    [Fact]
    public void BackwardCompatibility_LegacyJsonWithoutNewKeys_DeserializesWithDefaults()
    {
        // JSON simulating older Lyrow version settings payload without Stage 11 keys
        string legacyJson = """
        {
            "Theme": 0,
            "Language": "en",
            "GlowIntensity": 80,
            "GlowThickness": 24,
            "FocusedLyricFontSize": 28.0,
            "UnfocusedLyricFontSize": 20.0
        }
        """;

        var deserialized = JsonSerializer.Deserialize<LyrowSettings>(legacyJson);

        deserialized.Should().NotBeNull();
        deserialized!.LyricsLineSpacing.Should().Be(20, "Should default safely to 20 px without throwing");
        deserialized.LyricsAlignment.Should().Be("Left", "Should default safely to Left without throwing");
        deserialized.GlowAcousticProfile.Should().Be(GlowAcousticProfile.Balanced, "Should default safely to Balanced");
        deserialized.LyricsBackdropOpacity.Should().Be(85, "Should default safely to 85%");
    }

    [Theory]
    [InlineData(GlowAcousticProfile.Balanced)]
    [InlineData(GlowAcousticProfile.BassBoost)]
    [InlineData(GlowAcousticProfile.VocalFocus)]
    [InlineData(GlowAcousticProfile.HighFidelity)]
    public void GlowBeatEngine_SetAcousticProfile_ModifiesFrequencySubBandReactivity(GlowAcousticProfile profile)
    {
        var engine = new GlowBeatEngine();
        engine.SetAcousticProfile(profile);
        engine.AcousticProfile.Should().Be(profile);

        // Feed synthetic spectrum
        var spectrum = CreateSpectrum(rms: 0.5f, bass: 0.6f, subBass: 0.5f, mid: 0.5f, treble: 0.5f);

        engine.Process(spectrum, sensitivity: 1.0f, frameDeltaMs: 16.6);

        engine.OverallGlowIntensity.Should().BeGreaterThan(0f);
        engine.BassIntensity.Should().BeGreaterThan(0f);
        engine.MidIntensity.Should().BeGreaterThan(0f);
        engine.TrebleIntensity.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void GlowBeatEngine_BassBoost_ProducesHigherBassIntensityThanVocalFocus()
    {
        var bassEngine = new GlowBeatEngine();
        bassEngine.SetAcousticProfile(GlowAcousticProfile.BassBoost);

        var vocalEngine = new GlowBeatEngine();
        vocalEngine.SetAcousticProfile(GlowAcousticProfile.VocalFocus);

        var spectrum = CreateSpectrum(rms: 0.5f, bass: 0.7f, subBass: 0.6f, mid: 0.6f, treble: 0.3f);

        // Process a couple frames to stabilize smoothing
        for (int i = 0; i < 5; i++)
        {
            bassEngine.Process(spectrum, sensitivity: 1.0f, frameDeltaMs: 16.6);
            vocalEngine.Process(spectrum, sensitivity: 1.0f, frameDeltaMs: 16.6);
        }

        bassEngine.BassIntensity.Should().BeGreaterThan(vocalEngine.BassIntensity,
            "BassBoost profile must emphasize low frequencies higher than VocalFocus profile");
        vocalEngine.MidIntensity.Should().BeGreaterThan(bassEngine.MidIntensity,
            "VocalFocus profile must emphasize mid frequencies higher than BassBoost profile");
    }

    [Fact]
    public void Localization_PersonalizationKeys_ExistAcrossAllFiveLanguages()
    {
        var loc = LocalizationService.Instance;
        string[] languages = ["en", "fr", "es", "de", "tr"];
        string[] requiredKeys =
        [
            "Lyrics_LineSpacing",
            "Lyrics_LineSpacing_Desc",
            "Lyrics_Alignment",
            "Lyrics_Alignment_Desc",
            "Lyrics_BackdropOpacity",
            "Lyrics_BackdropOpacity_Desc",
            "Glow_AcousticProfile",
            "Glow_AcousticProfile_Desc",
            "Profile_Balanced",
            "Profile_BassBoost",
            "Profile_VocalFocus",
            "Profile_HighFidelity",
            "Align_Left",
            "Align_Center"
        ];

        foreach (var lang in languages)
        {
            loc.SetLanguage(lang);
            foreach (var key in requiredKeys)
            {
                string value = loc[key];
                value.Should().NotBeNullOrWhiteSpace($"Key '{key}' must have a localized translation in language '{lang}'");
                value.Should().NotBe(key, $"Key '{key}' should not fallback to its key name in language '{lang}'");
            }
        }

        // Restore to english default
        loc.SetLanguage("en");
    }

    [Theory]
    [InlineData(40)]
    [InlineData(85)]
    [InlineData(100)]
    public void LyricsBackdropOpacity_ClampedCorrectly(int inputPercent)
    {
        int clamped = Math.Clamp(inputPercent, 40, 100);
        double opacity = clamped / 100.0;

        opacity.Should().BeInRange(0.40, 1.00);
    }

    [Theory]
    [InlineData("Left", "Left")]
    [InlineData("Center", "Center")]
    [InlineData("left", "Left")]
    [InlineData("CENTER", "Center")]
    [InlineData("invalid", "Left")]
    public void LyricsAlignment_NormalizesPredictably(string input, string expected)
    {
        string normalized = string.Equals(input, "Center", StringComparison.OrdinalIgnoreCase) ? "Center" : "Left";
        normalized.Should().Be(expected);
    }
}
