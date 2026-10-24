using System.IO;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Lyrow.App.Tests;

public class HomeNavigationTests
{
    [Fact]
    public void HomeCards_AreDefinedWithDistinctNamesForNavigation()
    {
        string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string root = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        string xamlPath = Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml");
        string xaml = File.ReadAllText(xamlPath);

        xaml.Should().Contain("x:Name=\"CardCurrentTrack\"");
        xaml.Should().Contain("x:Name=\"CardVolume\"");
        xaml.Should().Contain("x:Name=\"CardGlow\"");
        xaml.Should().Contain("x:Name=\"CardLyrics\"");
    }
}
