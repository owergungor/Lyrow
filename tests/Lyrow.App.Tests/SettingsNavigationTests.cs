using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Lyrow.App.Tests;

public class SettingsNavigationTests
{
    private static string GetSolutionRoot()
    {
        string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
    }

    [Fact]
    public void NavigationMenu_HasExactOrder_HomePlaybackGlowLyrics()
    {
        string root = GetSolutionRoot();
        string xamlPath = Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml");
        string xaml = File.ReadAllText(xamlPath);

        // Extract menu item tags in order
        var matches = Regex.Matches(xaml, @"<NavigationViewItem\s+x:Name=""([^""]+)""[^>]*Tag=""([^""]+)""");
        Assert.True(matches.Count >= 4, "Should have at least 4 main navigation items");

        var tags = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 4; i++)
        {
            tags.Add(matches[i].Groups[2].Value);
        }

        // Required v2.7 order: Home (0), Playback (1), Glow (2), Lyrics (3)
        Assert.Equal("Home", tags[0]);
        Assert.Equal("Playback", tags[1]);
        Assert.Equal("Glow", tags[2]);
        Assert.Equal("Lyrics", tags[3]);
    }

    [Fact]
    public void SettingsPage_ContainsOnlyGeneralUpdatesAndShortcutsCategories()
    {
        string root = GetSolutionRoot();
        string xamlPath = Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml");
        string xaml = File.ReadAllText(xamlPath);

        // Settings page should contain SectionGeneral, SectionUpdates, SectionShortcuts
        xaml.Should().Contain("x:Name=\"SectionGeneral\"");
        xaml.Should().Contain("x:Name=\"SectionUpdates\"");
        xaml.Should().Contain("x:Name=\"SectionShortcuts\"");

        // Settings page must NOT contain duplicate SectionPlayback, SectionLyrics, or SectionGlow
        xaml.Should().NotContain("x:Name=\"SectionPlayback\"");
        xaml.Should().NotContain("x:Name=\"SectionLyrics\"");
        xaml.Should().NotContain("x:Name=\"SectionGlow\"");
    }
}
