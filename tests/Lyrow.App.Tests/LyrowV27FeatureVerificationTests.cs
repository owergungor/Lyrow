using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV27FeatureVerificationTests
{
    private static string GetSolutionRoot()
    {
        string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
    }

    [Fact]
    public void Versioning_IsStrictlyTwoPart_V27()
    {
        Assert.Equal("2.7", GitHubUpdateService.AppVersion);
        Assert.True(GitHubUpdateService.IsValidTwoPartVersionTag("v2.7"));
        Assert.False(GitHubUpdateService.IsValidTwoPartVersionTag("v2.7.0"));
        Assert.False(GitHubUpdateService.IsValidTwoPartVersionTag("2.7.0"));
        Assert.False(GitHubUpdateService.IsValidTwoPartVersionTag("v2.7.1"));
        Assert.True(GitHubUpdateService.IsNewerVersion("2.6", "2.7"));
        Assert.False(GitHubUpdateService.IsNewerVersion("2.7", "2.7"));
    }

    [Fact]
    public void Versioning_AllConfigFilesEnforce27()
    {
        string root = GetSolutionRoot();
        string props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        props.Should().Contain("<Version>2.7</Version>");
        props.Should().Contain("<PackageVersion>2.7</PackageVersion>");
        props.Should().Contain("<InformationalVersion>2.7</InformationalVersion>");
        props.Should().NotContain("<Version>2.7.0</Version>");
        props.Should().NotContain("<PackageVersion>2.7.0</PackageVersion>");
        props.Should().NotContain("<InformationalVersion>2.7.0</InformationalVersion>");

        string appCsproj = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "Lyrow.App.csproj"));
        appCsproj.Should().Contain("<Version>2.7</Version>");
        appCsproj.Should().Contain("<PackageVersion>2.7</PackageVersion>");
        appCsproj.Should().Contain("<InformationalVersion>2.7</InformationalVersion>");
        appCsproj.Should().NotContain("<Version>2.7.0</Version>");
        appCsproj.Should().NotContain("<PackageVersion>2.7.0</PackageVersion>");
        appCsproj.Should().NotContain("<InformationalVersion>2.7.0</InformationalVersion>");

        string setupIss = File.ReadAllText(Path.Combine(root, "installer", "setup.iss"));
        setupIss.Should().Contain("#define MyAppVersion \"2.7\"");
        setupIss.Should().NotContain("#define MyAppVersion \"2.7.0\"");

        string manifest = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "app.manifest"));
        manifest.Should().Contain("version=\"2.7.0.0\"");

        string mainXaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml"));
        mainXaml.Should().Contain("Current Version: v2.7");
    }

    [Fact]
    public void HighResIconAssets_ExistWithExpectedSizes()
    {
        string root = GetSolutionRoot();
        string icoPath = Path.Combine(root, "src", "Lyrow.App", "Assets", "lyrow.ico");
        string pngPath = Path.Combine(root, "src", "Lyrow.App", "Assets", "lyrow.png");

        Assert.True(File.Exists(icoPath), "lyrow.ico must exist");
        Assert.True(File.Exists(pngPath), "lyrow.png must exist");

        var icoInfo = new FileInfo(icoPath);
        var pngInfo = new FileInfo(pngPath);

        // ICO should be high-res multi-layer (~240 KB)
        Assert.True(icoInfo.Length > 200_000, $"lyrow.ico size {icoInfo.Length} bytes is lower than expected 200KB");
        // PNG should be high-res lossless (~1 MB)
        Assert.True(pngInfo.Length > 800_000, $"lyrow.png size {pngInfo.Length} bytes is lower than expected 800KB");
    }

    [Fact]
    public void ZeroLegacyNames_InAllSourceAndConfig()
    {
        string root = GetSolutionRoot();
        string[] searchDirs = { "src", "tests", "installer" };
        string pattern1 = @"\b" + "l" + "u" + "m" + "n" + @"\b";
        string pattern2 = @"\b" + "v" + "e" + "r" + "c" + "i" + @"\b";

        foreach (var dir in searchDirs)
        {
            var dirPath = Path.Combine(root, dir);
            if (!Directory.Exists(dirPath)) continue;

            var files = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (file.Contains("FeatureVerificationTests"))
                    continue;

                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".cs" or ".xaml" or ".iss" or ".props" or ".csproj" or ".json" or ".xml"))
                    continue;

                string content = File.ReadAllText(file);
                Regex.IsMatch(content, pattern1, RegexOptions.IgnoreCase).Should().BeFalse($"File {file} should not contain legacy whole-word");
                Regex.IsMatch(content, pattern2, RegexOptions.IgnoreCase).Should().BeFalse($"File {file} should not contain legacy whole-word");
            }
        }
    }
}
