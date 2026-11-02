using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Comprehensive verification tests for Stage 17: Release Operations, Distribution Automation & User Readiness.
/// Validates checksum generation, checksum verification, tamper detection, missing file detection,
/// installer metadata, and user documentation integrity.
/// </summary>
public class Stage17ReleaseOperationsTests : IDisposable
{
    private readonly string _testTempDir;
    private readonly string _repoRoot;
    private readonly string _generateScriptPath;
    private readonly string _verifyScriptPath;

    public Stage17ReleaseOperationsTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), $"Lyrow_Stage17_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testTempDir);

        string candidateRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        if (File.Exists(Path.Combine(candidateRoot, "Lyrow.sln")))
        {
            _repoRoot = candidateRoot;
        }
        else
        {
            _repoRoot = Directory.GetCurrentDirectory();
        }

        _generateScriptPath = Path.Combine(_repoRoot, "installer", "generate_checksums.ps1");
        _verifyScriptPath = Path.Combine(_repoRoot, "installer", "verify_checksums.ps1");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }
        catch
        {
            // Ignore temp cleanup errors
        }
    }

    private (int exitCode, string stdout, string stderr) RunPowerShellScript(string scriptPath, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _repoRoot
        };

        using var process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30000);

        return (process.ExitCode, stdout, stderr);
    }

    [Fact]
    public void GenerateChecksums_ScriptFile_Exists()
    {
        File.Exists(_generateScriptPath).Should().BeTrue("installer/generate_checksums.ps1 must exist");
    }

    [Fact]
    public void VerifyChecksums_ScriptFile_Exists()
    {
        File.Exists(_verifyScriptPath).Should().BeTrue("installer/verify_checksums.ps1 must exist");
    }

    [Fact]
    public void GenerateChecksums_ValidFiles_GeneratesSha256SumsFile()
    {
        string file1 = Path.Combine(_testTempDir, "Lyrow-Setup.exe");
        string file2 = Path.Combine(_testTempDir, "Lyrow-Portable.zip");
        File.WriteAllText(file1, "Setup Binary Mock Content", Encoding.UTF8);
        File.WriteAllText(file2, "Portable ZIP Mock Content", Encoding.UTF8);

        string outputFile = Path.Combine(_testTempDir, "sha256sums.txt");
        var (exitCode, stdout, stderr) = RunPowerShellScript(_generateScriptPath, $"-DistPath \"{_testTempDir}\" -OutputFile \"{outputFile}\"");

        exitCode.Should().Be(0, $"Generate script must succeed. Stderr: {stderr}");
        File.Exists(outputFile).Should().BeTrue("Output checksum file must be created");

        string content = File.ReadAllText(outputFile);
        content.Should().Contain("Lyrow-Setup.exe");
        content.Should().Contain("Lyrow-Portable.zip");
    }

    [Fact]
    public void GenerateChecksums_NonExistentDirectory_ExitsWithErrorCode()
    {
        string nonExistent = Path.Combine(_testTempDir, "NonExistentSubDir_" + Guid.NewGuid().ToString("N"));
        string outputFile = Path.Combine(_testTempDir, "sha256sums.txt");

        var (exitCode, _, _) = RunPowerShellScript(_generateScriptPath, $"-DistPath \"{nonExistent}\" -OutputFile \"{outputFile}\"");

        exitCode.Should().Be(1, "Generate script must exit with code 1 when directory does not exist");
    }

    [Fact]
    public void GenerateChecksums_EmptyDirectory_ExitsWithErrorCode()
    {
        string emptyDir = Path.Combine(_testTempDir, "EmptyDir");
        Directory.CreateDirectory(emptyDir);
        string outputFile = Path.Combine(emptyDir, "sha256sums.txt");

        var (exitCode, _, _) = RunPowerShellScript(_generateScriptPath, $"-DistPath \"{emptyDir}\" -OutputFile \"{outputFile}\"");

        exitCode.Should().Be(1, "Generate script must exit with code 1 when directory has no distribution files");
    }

    [Fact]
    public void VerifyChecksums_ValidChecksums_ReturnsExitCodeZero()
    {
        string file1 = Path.Combine(_testTempDir, "Lyrow-App.exe");
        File.WriteAllText(file1, "Valid executable mock content", Encoding.UTF8);

        string checksumFile = Path.Combine(_testTempDir, "sha256sums.txt");
        var (genExit, _, _) = RunPowerShellScript(_generateScriptPath, $"-DistPath \"{_testTempDir}\" -OutputFile \"{checksumFile}\"");
        genExit.Should().Be(0);

        var (verifyExit, stdout, stderr) = RunPowerShellScript(_verifyScriptPath, $"-ChecksumFile \"{checksumFile}\" -BaseDir \"{_testTempDir}\"");

        verifyExit.Should().Be(0, $"Verify script must succeed for unchanged files. Stderr: {stderr}");
        stdout.Should().Contain("Lyrow-App.exe : OK");
        stdout.Should().Contain("All 1 file(s) successfully verified");
    }

    [Fact]
    public void VerifyChecksums_TamperedFile_ReturnsExitCodeOne()
    {
        string file1 = Path.Combine(_testTempDir, "Lyrow-App.exe");
        File.WriteAllText(file1, "Original Content", Encoding.UTF8);

        string checksumFile = Path.Combine(_testTempDir, "sha256sums.txt");
        RunPowerShellScript(_generateScriptPath, $"-DistPath \"{_testTempDir}\" -OutputFile \"{checksumFile}\"");

        // Tamper file after checksum generation
        File.WriteAllText(file1, "Tampered Malicious Content", Encoding.UTF8);

        var (verifyExit, stdout, stderr) = RunPowerShellScript(_verifyScriptPath, $"-ChecksumFile \"{checksumFile}\" -BaseDir \"{_testTempDir}\"");

        verifyExit.Should().Be(1, "Verify script must return 1 when checksum mismatch occurs");
        stdout.Should().Contain("FAILED");
        stdout.Should().Contain("Checksum mismatch");
    }

    [Fact]
    public void VerifyChecksums_MissingFile_ReturnsExitCodeOne()
    {
        string file1 = Path.Combine(_testTempDir, "Lyrow-App.exe");
        File.WriteAllText(file1, "Original Content", Encoding.UTF8);

        string checksumFile = Path.Combine(_testTempDir, "sha256sums.txt");
        RunPowerShellScript(_generateScriptPath, $"-DistPath \"{_testTempDir}\" -OutputFile \"{checksumFile}\"");

        // Delete file after checksum generation
        File.Delete(file1);

        var (verifyExit, stdout, _) = RunPowerShellScript(_verifyScriptPath, $"-ChecksumFile \"{checksumFile}\" -BaseDir \"{_testTempDir}\"");

        verifyExit.Should().Be(1, "Verify script must return 1 when a target file is missing");
        stdout.Should().Contain("MISSING");
    }

    [Fact]
    public void VerifyChecksums_MissingChecksumFile_ReturnsExitCodeOne()
    {
        string nonExistentChk = Path.Combine(_testTempDir, "non_existent_sha256sums.txt");

        var (verifyExit, _, _) = RunPowerShellScript(_verifyScriptPath, $"-ChecksumFile \"{nonExistentChk}\" -BaseDir \"{_testTempDir}\"");

        verifyExit.Should().Be(1, "Verify script must return 1 when checksum file itself is missing");
    }

    [Fact]
    public void SetupIss_ContainsExpectedPublisherAndMutex()
    {
        string setupIssPath = Path.Combine(_repoRoot, "installer", "setup.iss");
        File.Exists(setupIssPath).Should().BeTrue("setup.iss must exist");

        string content = File.ReadAllText(setupIssPath);
        content.Should().Contain("#define MyAppName \"Lyrow\"");
        content.Should().Contain("#define MyAppPublisher \"Lyrow Team\"");
        content.Should().Contain("#define AppMutex \"Lyrow_SingleInstance_f84b12c9-94b1-4c4f-9e63-47a61d15c89f\"");
        content.Should().Contain("AppMutex={#AppMutex}");
        content.Should().Contain("PrivilegesRequired=lowest");
        content.Should().Contain("KeepSettingsPrompt");
    }

    [Theory]
    [InlineData("artifacts/stage17-release-operations-runbook.md", "Release Operations Runbook")]
    [InlineData("artifacts/stage17-user-installation-guide.md", "User Installation Guide")]
    [InlineData("artifacts/stage17-troubleshooting-guide.md", "Troubleshooting Guide")]
    [InlineData("artifacts/stage17-privacy-and-security.md", "Privacy & Security Declaration")]
    public void Documentation_AllStage17Guides_ExistAndContainRequiredHeadings(string relativePath, string expectedTitle)
    {
        string fullPath = Path.Combine(_repoRoot, relativePath);
        File.Exists(fullPath).Should().BeTrue($"Document '{relativePath}' must exist");

        string content = File.ReadAllText(fullPath);
        content.Should().Contain(expectedTitle);
    }
}
