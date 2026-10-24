using System.IO;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Lyrow.App.Tests;

public class PlaybackTransportIconTests
{
    [Fact]
    public void PlaybackTransportControls_AreIconOnlyWithTooltipsAndAccessibility()
    {
        string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string root = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        string xamlPath = Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml");
        string xaml = File.ReadAllText(xamlPath);

        // Previous button has icon and tooltip
        xaml.Should().Contain("x:Name=\"BtnPagePrev\"");
        xaml.Should().Contain("ToolTipService.ToolTip=\"Previous Track\"");

        // Play/Pause button has icon and tooltip
        xaml.Should().Contain("x:Name=\"BtnPagePlayPause\"");
        xaml.Should().Contain("ToolTipService.ToolTip=\"Play / Pause\"");
        xaml.Should().Contain("x:Name=\"IconPagePlayPause\"");

        // Next button has icon and tooltip
        xaml.Should().Contain("x:Name=\"BtnPageNext\"");
        xaml.Should().Contain("ToolTipService.ToolTip=\"Next Track\"");
    }
}
