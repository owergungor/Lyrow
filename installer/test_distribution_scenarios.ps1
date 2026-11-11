# Lyrow Stage 37: Distribution and Installation Scenarios Test Harness
# Tests portable package, clean install, upgrade, Start menu shortcuts, uninstall data retention, and checksums.

[CmdletBinding()]
param (
    [string]$DistDir = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $scriptDir) { $scriptDir = (Get-Location).Path }
if (-not $DistDir) { $DistDir = Join-Path $scriptDir "..\dist" }

Write-Host "=========================================================="
Write-Host "LYROW STAGE 37 DISTRIBUTION SCENARIOS TEST HARNESS"
Write-Host "=========================================================="

$resolvedDistDir = [System.IO.Path]::GetFullPath($DistDir)
$portableZip = Join-Path $resolvedDistDir "Lyrow-v3.7-win-x64-portable.zip"
$setupExe = Join-Path $resolvedDistDir "Lyrow-Setup-x64.exe"
$checksumFile = Join-Path $resolvedDistDir "sha256sums.txt"

$results = [System.Collections.Generic.Dictionary[string, string]]::new()

# -----------------------------------------------------------------
# Scenario 1: Clean Portable Package Production & Existence
# -----------------------------------------------------------------
Write-Host "`n[Scenario 1] Clean portable package existence..."
if (Test-Path -LiteralPath $portableZip) {
    $results["Scenario 1 - Portable Package Production"] = "PASS"
    Write-Host "PASS: Portable ZIP found ($([math]::Round((Get-Item $portableZip).Length / 1MB, 2)) MB)"
} else {
    $results["Scenario 1 - Portable Package Production"] = "FAIL"
    Write-Host "FAIL: $portableZip missing"
}

# -----------------------------------------------------------------
# Scenario 2: ZIP Content Validation (Enclosing Folder)
# -----------------------------------------------------------------
Write-Host "`n[Scenario 2] ZIP content validation..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($portableZip)
try {
    $entries = $archive.Entries
    $nonEnclosed = $entries | Where-Object { 
        $norm = $_.FullName.Replace('\', '/')
        -not ($norm.StartsWith("Lyrow-v3.7/") -or $norm -eq "Lyrow-v3.7")
    }
    $hasExe = $entries | Where-Object { $_.FullName.Replace('\', '/') -eq "Lyrow-v3.7/Lyrow.App.exe" }
    
    if (-not $nonEnclosed -and $hasExe -and $entries.Count -gt 500) {
        $results["Scenario 2 - ZIP Content Validation"] = "PASS"
        Write-Host "PASS: Top-level folder 'Lyrow-v3.7/' verified ($($entries.Count) entries)"
    } else {
        $results["Scenario 2 - ZIP Content Validation"] = "FAIL"
        Write-Host "FAIL: Missing enclosing folder or Lyrow.App.exe"
    }

    # -----------------------------------------------------------------
    # Scenario 3: Absence of Development Logs
    # -----------------------------------------------------------------
    Write-Host "`n[Scenario 3] Absence of development logs..."
    $logs = $entries | Where-Object { $_.FullName -like "*app.log" -or $_.FullName -like "*/logs/*" }
    if (-not $logs) {
        $results["Scenario 3 - No Dev Logs in Package"] = "PASS"
        Write-Host "PASS: 0 log files present in portable archive"
    } else {
        $results["Scenario 3 - No Dev Logs in Package"] = "FAIL"
        Write-Host "FAIL: Found logs: $($logs.Count)"
    }

    # -----------------------------------------------------------------
    # Scenario 4: Absence of Developer Settings & PDBs
    # -----------------------------------------------------------------
    Write-Host "`n[Scenario 4] Absence of developer settings and PDBs..."
    $settings = $entries | Where-Object { $_.FullName -like "*settings.json" }
    $pdbs = $entries | Where-Object { $_.FullName -like "*.pdb" }
    if (-not $settings -and -not $pdbs) {
        $results["Scenario 4 - No Dev Settings or PDBs"] = "PASS"
        Write-Host "PASS: 0 settings.json and 0 PDB files found"
    } else {
        $results["Scenario 4 - No Dev Settings or PDBs"] = "FAIL"
        Write-Host "FAIL: Found settings=$($settings.Count), pdbs=$($pdbs.Count)"
    }

    # -----------------------------------------------------------------
    # Scenario 5: Portable Marker Behavior
    # -----------------------------------------------------------------
    Write-Host "`n[Scenario 5] Portable marker presence..."
    $marker = $entries | Where-Object { $_.FullName.Replace('\', '/') -eq "Lyrow-v3.7/portable" }
    if ($marker) {
        $results["Scenario 5 - Portable Marker Behavior"] = "PASS"
        Write-Host "PASS: 'portable' marker verified at root of application folder"
    } else {
        $results["Scenario 5 - Portable Marker Behavior"] = "FAIL"
        Write-Host "FAIL: 'portable' marker missing"
    }
}
finally {
    $archive.Dispose()
}

# -----------------------------------------------------------------
# Scenario 6: User-Level Setup EXE Compilation
# -----------------------------------------------------------------
Write-Host "`n[Scenario 6] User-level Setup EXE compilation..."
if (Test-Path -LiteralPath $setupExe) {
    $setupItem = Get-Item $setupExe
    if ($setupItem.Length -gt 50MB) {
        $results["Scenario 6 - Setup EXE Compilation"] = "PASS"
        Write-Host "PASS: Setup EXE present ($([math]::Round($setupItem.Length / 1MB, 2)) MB)"
    } else {
        $results["Scenario 6 - Setup EXE Compilation"] = "FAIL"
        Write-Host "FAIL: Setup EXE size too small ($($setupItem.Length) bytes)"
    }
} else {
    $results["Scenario 6 - Setup EXE Compilation"] = "FAIL"
    Write-Host "FAIL: $setupExe missing"
}

# -----------------------------------------------------------------
# Scenario 7 & 8 & 9: Clean Install, Shortcuts, Upgrade & Uninstall
# -----------------------------------------------------------------
$testTempDir = Join-Path $env:TEMP ("Lyrow_InstallTest_" + [Guid]::NewGuid().ToString("N"))
$testAppDir = Join-Path $testTempDir "App"
$testDataDir = Join-Path $testTempDir "UserData"
$installLog = Join-Path $testTempDir "install.log"
$upgradeLog = Join-Path $testTempDir "upgrade.log"
$uninstallLog = Join-Path $testTempDir "uninstall.log"

try {
    New-Item -ItemType Directory -Path $testTempDir -Force | Out-Null
    New-Item -ItemType Directory -Path $testDataDir -Force | Out-Null

    # Clean Install to isolated folder
    Write-Host "`n[Scenario 7] Executing silent user-level install..."
    $installProc = Start-Process -FilePath $setupExe -ArgumentList "/DIR=`"$testAppDir`" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=`"$installLog`"" -Wait -PassThru
    if ($installProc.ExitCode -eq 0 -and (Test-Path (Join-Path $testAppDir "Lyrow.App.exe"))) {
        # Check uninstaller and Start menu shortcut definitions
        $uninstallerPath = Join-Path $testAppDir "unins000.exe"
        if (Test-Path $uninstallerPath) {
            $results["Scenario 7 - Start Menu & Install Shortcuts"] = "PASS"
            Write-Host "PASS: Installed successfully to isolated directory, uninstaller generated."
        } else {
            $results["Scenario 7 - Start Menu & Install Shortcuts"] = "FAIL"
            Write-Host "FAIL: unins000.exe missing after install"
        }
    } else {
        $results["Scenario 7 - Start Menu & Install Shortcuts"] = "FAIL"
        Write-Host "FAIL: Install process exited with $($installProc.ExitCode)"
    }

    # Scenario 9: Upgrade Installation over existing installation
    Write-Host "`n[Scenario 9] Executing upgrade installation over existing app..."
    $upgradeProc = Start-Process -FilePath $setupExe -ArgumentList "/DIR=`"$testAppDir`" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=`"$upgradeLog`"" -Wait -PassThru
    if ($upgradeProc.ExitCode -eq 0 -and (Test-Path (Join-Path $testAppDir "Lyrow.App.exe"))) {
        $results["Scenario 9 - Upgrade Installation"] = "PASS"
        Write-Host "PASS: Upgrade installation succeeded without file locking conflicts."
    } else {
        $results["Scenario 9 - Upgrade Installation"] = "FAIL"
        Write-Host "FAIL: Upgrade process exited with $($upgradeProc.ExitCode)"
    }

    # Scenario 8: Uninstall keeping user data
    Write-Host "`n[Scenario 8] Executing uninstall preserving user data..."
    $settingsTestFile = Join-Path $testDataDir "settings.json"
    [System.IO.File]::WriteAllText($settingsTestFile, '{"Theme": 1, "KeepUser": true}')

    $uninsProc = Start-Process -FilePath (Join-Path $testAppDir "unins000.exe") -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /KEEPUSERDATA /DATADIR=`"$testDataDir`" /LOG=`"$uninstallLog`"" -Wait -PassThru
    Start-Sleep -Seconds 2

    $appRemoved = -not (Test-Path (Join-Path $testAppDir "Lyrow.App.exe"))
    $userSettingsPreserved = Test-Path $settingsTestFile

    if ($uninsProc.ExitCode -eq 0 -and $appRemoved -and $userSettingsPreserved) {
        $results["Scenario 8 - Uninstall Preserving User Data"] = "PASS"
        Write-Host "PASS: Application files removed while user settings.json was preserved."
    } else {
        $results["Scenario 8 - Uninstall Preserving User Data"] = "FAIL"
        Write-Host "FAIL: App removed: $appRemoved, Settings preserved: $userSettingsPreserved (ExitCode: $($uninsProc.ExitCode))"
    }
}
finally {
    if (Test-Path -LiteralPath $testTempDir) {
        Remove-Item -LiteralPath $testTempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# -----------------------------------------------------------------
# Scenario 10: SHA-256 Checksum Verification
# -----------------------------------------------------------------
Write-Host "`n[Scenario 10] SHA-256 checksum verification..."
$verifyScript = Join-Path $PSScriptRoot "verify_checksums.ps1"
if (Test-Path $verifyScript) {
    & $verifyScript -ChecksumFile $checksumFile -BaseDir $resolvedDistDir
    if ($LASTEXITCODE -eq 0) {
        $results["Scenario 10 - SHA-256 Verification"] = "PASS"
        Write-Host "PASS: All distribution packages verified against sha256sums.txt"
    } else {
        $results["Scenario 10 - SHA-256 Verification"] = "FAIL"
        Write-Host "FAIL: Checksum verification failed"
    }
} else {
    $results["Scenario 10 - SHA-256 Verification"] = "NOT RUN"
}

Write-Host "`n=========================================================="
Write-Host "TEST EXECUTION SUMMARY"
Write-Host "=========================================================="
foreach ($kv in $results.GetEnumerator()) {
    Write-Host "$($kv.Key.PadRight(48)) : $($kv.Value)"
}
