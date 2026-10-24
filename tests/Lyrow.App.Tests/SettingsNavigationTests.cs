using System.IO;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Lyrow.App.Tests;

public class SettingsNavigationTests
{
    [Fact]
    public void SettingsPage_ContainsOnlyGeneralUpdatesAndShortcutsCategories()
    {
        string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string root = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
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
