using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Lyrow.Core.Services;

/// <summary>
/// Resolves the user's registered default Windows web browser executable (Chrome, Edge, Firefox, Brave, Opera, Vivaldi, etc.)
/// and launches web URLs directly via the browser binary with <c>UseShellExecute = false</c>.
/// This prevents Windows <c>AppUriHandler</c> registrations (such as Apple Music Desktop intercepting <c>https://music.apple.com/</c>)
/// from hijacking web redirects.
/// </summary>
public static class DefaultWebBrowserLauncher
{
    public const string AppleMusicWebUrl = "https://music.apple.com/";

    /// <summary>
    /// Extracts a clean executable path from a Windows registry <c>shell\open\command</c> value
    /// such as <c>"C:\Program Files\Google\Chrome\Application\chrome.exe" --single-argument %1</c>.
    /// </summary>
    public static string? ExtractExecutablePathFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        string trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            int closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                string candidate = trimmed[1..closingQuote].Trim();
                return candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? candidate : null;
            }
        }

        int exeIdx = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx > 0)
        {
            string candidate = trimmed[..(exeIdx + 4)].Trim();
            return candidate;
        }

        return null;
    }

    /// <summary>
    /// Returns true if the executable path or ProgId belongs to a native media app or shell wrapper
    /// that must never be used as a web browser (e.g., AppleMusic.exe, iTunes.exe, explorer.exe, LaunchWinApp).
    /// </summary>
    public static bool IsDisallowedBrowserTarget(string? pathOrProgId)
    {
        if (string.IsNullOrWhiteSpace(pathOrProgId)) return true;

        string s = pathOrProgId.Trim();
        return s.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)
            || s.Contains("iTunes", StringComparison.OrdinalIgnoreCase)
            || s.Contains("AppleInc", StringComparison.OrdinalIgnoreCase)
            || s.Contains("LaunchWinApp", StringComparison.OrdinalIgnoreCase)
            || s.Contains("AppX", StringComparison.OrdinalIgnoreCase) && s.Contains("Apple", StringComparison.OrdinalIgnoreCase)
            || s.EndsWith(@"\explorer.exe", StringComparison.OrdinalIgnoreCase)
            || s.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
            || s.EndsWith(@"\rundll32.exe", StringComparison.OrdinalIgnoreCase)
            || s.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the absolute file path to the user's default web browser executable on Windows.
    /// Supports dependency-injected registry/file readers for deterministic unit testing.
    /// </summary>
    public static string? ResolveDefaultBrowserExecutablePath(
        Func<string, string, string?>? registryValueReader = null,
        Func<string, bool>? fileExistsValidator = null)
    {
        var readReg = registryValueReader ?? ReadRegistryValueWindows;
        var fileExists = fileExistsValidator ?? File.Exists;

        // 1. Check HKCU https and http UserChoice ProgId
        string[] schemes = ["https", "http"];
        foreach (string scheme in schemes)
        {
            string? progId = readReg(
                $@"HKEY_CURRENT_USER\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{scheme}\UserChoice",
                "ProgId");

            if (!string.IsNullOrWhiteSpace(progId) && !IsDisallowedBrowserTarget(progId))
            {
                string? exeFromProgId = ResolveExecutableFromProgId(progId, readReg, fileExists);
                if (!string.IsNullOrWhiteSpace(exeFromProgId))
                {
                    return exeFromProgId;
                }
            }
        }

        // 2. Check HKCU / HKLM StartMenuInternet default client
        string[] startMenuRoots =
        [
            @"HKEY_CURRENT_USER\Software\Clients\StartMenuInternet",
            @"HKEY_LOCAL_MACHINE\Software\Clients\StartMenuInternet"
        ];
        foreach (string root in startMenuRoots)
        {
            string? clientName = readReg(root, "");
            if (!string.IsNullOrWhiteSpace(clientName))
            {
                string? cmd = readReg($@"{root}\{clientName}\shell\open\command", "");
                string? exe = ExtractExecutablePathFromCommand(cmd);
                if (!string.IsNullOrWhiteSpace(exe) && !IsDisallowedBrowserTarget(exe) && fileExists(exe))
                {
                    return exe;
                }
            }
        }

        // 3. Check well-known browser ProgIds in HKCR
        string[] knownProgIds =
        [
            "ChromeHTML",
            "MSEdgeHTM",
            "BraveHTML",
            "FirefoxURL",
            "OperaStable",
            "VivaldiHTM"
        ];
        foreach (string knownProgId in knownProgIds)
        {
            string? exe = ResolveExecutableFromProgId(knownProgId, readReg, fileExists);
            if (!string.IsNullOrWhiteSpace(exe))
            {
                return exe;
            }
        }

        // 4. Fallback to well-known installed browser locations on Windows
        foreach (string candidatePath in GetKnownBrowserExecutablePaths())
        {
            if (!IsDisallowedBrowserTarget(candidatePath) && fileExists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private static string? ResolveExecutableFromProgId(
        string progId,
        Func<string, string, string?> readReg,
        Func<string, bool> fileExists)
    {
        string[] commandKeys =
        [
            $@"HKEY_CURRENT_USER\Software\Classes\{progId}\shell\open\command",
            $@"HKEY_CLASSES_ROOT\{progId}\shell\open\command",
            $@"HKEY_LOCAL_MACHINE\Software\Classes\{progId}\shell\open\command"
        ];

        foreach (string key in commandKeys)
        {
            string? cmd = readReg(key, "");
            string? exe = ExtractExecutablePathFromCommand(cmd);
            if (!string.IsNullOrWhiteSpace(exe) && !IsDisallowedBrowserTarget(exe) && fileExists(exe))
            {
                return exe;
            }
        }

        // If ProgId indicates a known browser family, check its App Paths entry
        string? appExeName = progId switch
        {
            _ when progId.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase) => "chrome.exe",
            _ when progId.StartsWith("MSEdge", StringComparison.OrdinalIgnoreCase) => "msedge.exe",
            _ when progId.StartsWith("Firefox", StringComparison.OrdinalIgnoreCase) => "firefox.exe",
            _ when progId.StartsWith("Brave", StringComparison.OrdinalIgnoreCase) => "brave.exe",
            _ when progId.StartsWith("Opera", StringComparison.OrdinalIgnoreCase) => "opera.exe",
            _ when progId.StartsWith("Vivaldi", StringComparison.OrdinalIgnoreCase) => "vivaldi.exe",
            _ => null
        };

        if (appExeName != null)
        {
            string[] appPathKeys =
            [
                $@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\App Paths\{appExeName}",
                $@"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\App Paths\{appExeName}"
            ];
            foreach (string key in appPathKeys)
            {
                string? exe = ExtractExecutablePathFromCommand(readReg(key, ""));
                if (!string.IsNullOrWhiteSpace(exe) && !IsDisallowedBrowserTarget(exe) && fileExists(exe))
                {
                    return exe;
                }
            }
        }

        return null;
    }

    public static IReadOnlyList<string> GetKnownBrowserExecutablePaths()
    {
        var paths = new List<string>();
        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        void AddCandidate(string root, string relative)
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                paths.Add(Path.Combine(root, relative));
            }
        }

        // Chrome
        AddCandidate(progFiles, @"Google\Chrome\Application\chrome.exe");
        AddCandidate(progFilesX86, @"Google\Chrome\Application\chrome.exe");
        AddCandidate(localAppData, @"Google\Chrome\Application\chrome.exe");

        // Microsoft Edge
        AddCandidate(progFilesX86, @"Microsoft\Edge\Application\msedge.exe");
        AddCandidate(progFiles, @"Microsoft\Edge\Application\msedge.exe");
        AddCandidate(localAppData, @"Microsoft\Edge\Application\msedge.exe");

        // Mozilla Firefox
        AddCandidate(progFiles, @"Mozilla Firefox\firefox.exe");
        AddCandidate(progFilesX86, @"Mozilla Firefox\firefox.exe");

        // Brave
        AddCandidate(progFiles, @"BraveSoftware\Brave-Browser\Application\brave.exe");
        AddCandidate(progFilesX86, @"BraveSoftware\Brave-Browser\Application\brave.exe");
        AddCandidate(localAppData, @"BraveSoftware\Brave-Browser\Application\brave.exe");

        // Opera / Vivaldi
        AddCandidate(localAppData, @"Programs\Opera\opera.exe");
        AddCandidate(localAppData, @"Vivaldi\Application\vivaldi.exe");

        return paths;
    }

    /// <summary>
    /// Creates a <see cref="ProcessStartInfo"/> that invokes the browser executable directly with <c>UseShellExecute = false</c>
    /// so Windows <c>AppUriHandler</c> never redirects <c>https://music.apple.com/</c> to the Apple Music Desktop app.
    /// </summary>
    public static ProcessStartInfo BuildBrowserProcessStartInfo(string browserExecutablePath, string url)
    {
        if (string.IsNullOrWhiteSpace(browserExecutablePath) || IsDisallowedBrowserTarget(browserExecutablePath))
        {
            throw new ArgumentException("A valid web browser executable path is required.", nameof(browserExecutablePath));
        }

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("A valid HTTP/HTTPS URL is required.", nameof(url));
        }

        var psi = new ProcessStartInfo
        {
            FileName = browserExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        psi.ArgumentList.Add(uri.AbsoluteUri);
        return psi;
    }

    /// <summary>
    /// Launches the given URL directly inside the user's default web browser executable, bypassing Windows AppUriHandlers.
    /// </summary>
    public static bool TryOpenUrlInDefaultBrowser(string url, out string? launchedBrowserPath, out string? errorMessage)
    {
        launchedBrowserPath = null;
        errorMessage = null;

        try
        {
            string? browserExe = ResolveDefaultBrowserExecutablePath();
            if (string.IsNullOrWhiteSpace(browserExe))
            {
                errorMessage = "Could not locate a default web browser executable on this system.";
                LocalDiagnosticLogger.LogWarning($"[BrowserLauncher] {errorMessage} URL={url}");
                return false;
            }

            var psi = BuildBrowserProcessStartInfo(browserExe, url);
            using var proc = Process.Start(psi);
            launchedBrowserPath = browserExe;
            LocalDiagnosticLogger.LogInfo($"[BrowserLauncher] Launched web browser '{browserExe}' with URL '{psi.ArgumentList[0]}' (UseShellExecute=false).");
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            LocalDiagnosticLogger.LogError($"[BrowserLauncher] Failed to launch browser for URL '{url}'", ex);
            return false;
        }
    }

    private static string? ReadRegistryValueWindows(string fullKeyPath, string valueName)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            object? val = Registry.GetValue(fullKeyPath, string.IsNullOrEmpty(valueName) ? null : valueName, null);
            return val as string;
        }
        catch
        {
            return null;
        }
    }
}
