using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Lyrow.App.Tests;

public sealed class LyrowStage37DistributionVerificationTests
{
    private static string FindRepoRoot()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Lyrow.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\.."));
    }

    [Fact]
    public void SetupIss_ShouldContainExpectedConfiguration()
    {
        string repoRoot = FindRepoRoot();
        string issPath = Path.Combine(repoRoot, "installer", "setup.iss");
        File.Exists(issPath).Should().BeTrue();

        string content = File.ReadAllText(issPath);
        content.Should().Contain("PrivilegesRequired=lowest", "Installer must install at user level without admin requirement");
        content.Should().Contain("OutputBaseFilename=Lyrow-Setup-x64", "Installer executable must be named Lyrow-Setup-x64");
        content.Should().Contain("{autoprograms}", "Installer must register Start Menu program shortcut");
        content.Should().Contain("Excludes: \"*.pdb,logs,logs\\*,settings.json,portable,.portable,*.tmp\"", "Installer must not copy developer logs, settings, or portable markers");
    }

    [Fact]
    public void BuildPackagesScript_ShouldExistAndBeConfigured()
    {
        string repoRoot = FindRepoRoot();
        string ps1Path = Path.Combine(repoRoot, "installer", "build_packages.ps1");
        File.Exists(ps1Path).Should().BeTrue();

        string content = File.ReadAllText(ps1Path);
        content.Should().Contain("Lyrow-Setup-x64.exe");
        content.Should().Contain("Lyrow-v$Version-win-x64-portable.zip");
        content.Should().Contain("sha256sums.txt");
    }

    [Fact]
    public void PortableZip_WhenPresentInDist_ShouldBeCleanAndEnclosed()
    {
        string repoRoot = FindRepoRoot();
        string distDir = Path.Combine(repoRoot, "dist");
        if (!Directory.Exists(distDir)) return;

        var portableZips = Directory.GetFiles(distDir, "Lyrow-v*-win-x64-portable.zip");
        if (portableZips.Length == 0) return;

        string zipPath = portableZips.OrderByDescending(f => f).First();
        using var archive = ZipFile.OpenRead(zipPath);

        archive.Entries.Count.Should().BeGreaterThan(500);

        string zipBaseName = Path.GetFileNameWithoutExtension(zipPath);
        // Extracts "Lyrow-v3.X" from "Lyrow-v3.X-win-x64-portable"
        string expectedTopFolder = zipBaseName.Substring(0, zipBaseName.IndexOf("-win-x64", StringComparison.OrdinalIgnoreCase));

        // All entries must reside inside the top level enclosed folder
        foreach (var entry in archive.Entries)
        {
            string norm = entry.FullName.Replace('\\', '/');
            norm.StartsWith($"{expectedTopFolder}/").Should().BeTrue($"Entry '{entry.FullName}' must be inside '{expectedTopFolder}/'");
        }

        // Must contain Lyrow.App.exe and portable marker
        archive.Entries.Any(e => e.FullName.Replace('\\', '/') == $"{expectedTopFolder}/Lyrow.App.exe").Should().BeTrue();
        archive.Entries.Any(e => e.FullName.Replace('\\', '/') == $"{expectedTopFolder}/portable").Should().BeTrue();

        // Must NOT contain PDBs, logs/app.log, or settings.json
        archive.Entries.Any(e => e.FullName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
        archive.Entries.Any(e => e.FullName.EndsWith("app.log", StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
        archive.Entries.Any(e => e.FullName.EndsWith("settings.json", StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
    }
}
