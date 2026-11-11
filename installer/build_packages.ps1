# Lyrow Packaging Pipeline
# Builds clean, deterministic Portable ZIP and Inno Setup Installer packages.

[CmdletBinding()]
param (
    [string]$Version = "3.8",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishDir = "",
    [string]$DistDir = "",
    [switch]$SkipPublish,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $scriptDir) { $scriptDir = (Get-Location).Path }

if (-not $PublishDir) { $PublishDir = Join-Path $scriptDir "..\publish\win-x64" }
if (-not $DistDir) { $DistDir = Join-Path $scriptDir "..\dist" }

Write-Host "=========================================================="
Write-Host "LYROW DISTRIBUTION PACKAGING PIPELINE (v$Version - $Runtime)"
Write-Host "=========================================================="

# Resolve absolute paths
$resolvedPublishDir = [System.IO.Path]::GetFullPath($PublishDir)
$resolvedDistDir = [System.IO.Path]::GetFullPath($DistDir)

if (-not (Test-Path -LiteralPath $resolvedDistDir)) {
    New-Item -ItemType Directory -Path $resolvedDistDir -Force | Out-Null
}

# Locate dotnet binary
$dotnetCmd = "dotnet"
$dotnetCandidates = @(
    "C:\Users\omer\AppData\Local\Microsoft\dotnet\dotnet.exe",
    "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe",
    "C:\Program Files\dotnet\dotnet.exe"
)
foreach ($dc in $dotnetCandidates) {
    if (Test-Path -LiteralPath $dc) {
        $dotnetCmd = $dc
        break
    }
}

# -----------------------------------------------------------------
# 1. Publish Self-Contained Win-x64 Application
# -----------------------------------------------------------------
if (-not $SkipPublish) {
    Write-Host "`n[1/5] Publishing self-contained $Runtime application ($Configuration)..."
    $projectPath = [System.IO.Path]::GetFullPath((Join-Path $scriptDir "..\src\Lyrow.App\Lyrow.App.csproj"))
    
    # Pre-clean any previous logs or developer settings from publish folder
    if (Test-Path $resolvedPublishDir) {
        Remove-Item -Path (Join-Path $resolvedPublishDir "logs") -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -Path (Join-Path $resolvedPublishDir "settings.json") -Force -ErrorAction SilentlyContinue
        Remove-Item -Path (Join-Path $resolvedPublishDir "portable") -Force -ErrorAction SilentlyContinue
        Remove-Item -Path (Join-Path $resolvedPublishDir ".portable") -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Running: $dotnetCmd publish $projectPath -c $Configuration -r $Runtime --self-contained true -o $resolvedPublishDir /p:UseSharedCompilation=false /nr:false"
    & $dotnetCmd publish $projectPath -c $Configuration -r $Runtime --self-contained true -o $resolvedPublishDir /p:UseSharedCompilation=false /nr:false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
} else {
    Write-Host "`n[1/5] Skipping publish step as requested."
}

# -----------------------------------------------------------------
# 2. Strict Pre-Packaging Validation
# -----------------------------------------------------------------
Write-Host "`n[2/5] Validating publish artifacts in $resolvedPublishDir..."
$requiredFiles = @(
    "Lyrow.App.exe",
    "Lyrow.App.dll",
    "Lyrow.Core.dll",
    "Lyrow.Music.dll",
    "Lyrow.Audio.dll",
    "coreclr.dll",
    "clrjit.dll",
    "Microsoft.WindowsAppRuntime.Bootstrap.dll",
    "Microsoft.WindowsAppRuntime.dll",
    "Microsoft.ui.xaml.dll",
    "DWriteCore.dll",
    "Lyrow.App.pri",
    "Assets\lyrow.ico"
)

foreach ($req in $requiredFiles) {
    $fullPath = Join-Path $resolvedPublishDir $req
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Validation Failed: Required file '$req' is missing from publish output at '$fullPath'."
    }
}
Write-Host "Publish artifacts validation: PASS (All core binaries, runtime and resources verified)."

# Hygiene sweep: ensure no logs or local settings survive in publish directory
Remove-Item -Path (Join-Path $resolvedPublishDir "logs") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $resolvedPublishDir "settings.json") -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $resolvedPublishDir "portable") -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $resolvedPublishDir ".portable") -Force -ErrorAction SilentlyContinue

# -----------------------------------------------------------------
# 3. Clean Portable ZIP Packaging
# -----------------------------------------------------------------
$topFolderName = "Lyrow-v$Version"
$portableZipName = "Lyrow-v$Version-win-x64-portable.zip"
$portableZipPath = Join-Path $resolvedDistDir $portableZipName

Write-Host "`n[3/5] Building clean portable ZIP package: $portableZipName..."
$tempStageRoot = Join-Path $env:TEMP ("Lyrow_Stage_" + [Guid]::NewGuid().ToString("N"))
$stageAppDir = Join-Path $tempStageRoot $topFolderName

try {
    New-Item -ItemType Directory -Path $stageAppDir -Force | Out-Null

    # Copy files excluding *.pdb, logs, settings.json, *.tmp
    $sourceItems = Get-ChildItem -LiteralPath $resolvedPublishDir
    foreach ($item in $sourceItems) {
        if ($item.Name -like "*.pdb" -or 
            $item.Name -eq "logs" -or 
            $item.Name -eq "settings.json" -or 
            $item.Name -eq "portable" -or
            $item.Name -eq ".portable" -or
            $item.Name -like "*.tmp") {
            continue
        }

        if ($item.PSIsContainer) {
            Copy-Item -LiteralPath $item.FullName -Destination $stageAppDir -Recurse -Force
        } else {
            Copy-Item -LiteralPath $item.FullName -Destination $stageAppDir -Force
        }
    }

    # Ensure clean portable marker file is added inside portable package
    $portableMarkerPath = Join-Path $stageAppDir "portable"
    [System.IO.File]::WriteAllText($portableMarkerPath, "")

    # Remove any existing zip with same name
    if (Test-Path -LiteralPath $portableZipPath) {
        Remove-Item -LiteralPath $portableZipPath -Force
    }

    # Compress enclosing top-level folder
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $tempStageRoot,
        $portableZipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false
    )

    $zipItem = Get-Item -LiteralPath $portableZipPath
    Write-Host "Portable ZIP generated: $($zipItem.FullName) ($([math]::Round($zipItem.Length / 1MB, 2)) MB)"
}
finally {
    if (Test-Path -LiteralPath $tempStageRoot) {
        Remove-Item -LiteralPath $tempStageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# -----------------------------------------------------------------
# 4. Inno Setup Windows Installer Packaging
# -----------------------------------------------------------------
$setupExeName = "Lyrow-Setup-x64.exe"
$setupExePath = Join-Path $resolvedDistDir $setupExeName

if (-not $SkipInstaller) {
    Write-Host "`n[4/5] Building Inno Setup Installer: $setupExeName..."
    
    # Locate ISCC.exe
    $isccCandidates = @(
        "C:\Users\omer\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    $isccPath = $null
    foreach ($cand in $isccCandidates) {
        if (Test-Path -LiteralPath $cand) {
            $isccPath = $cand
            break
        }
    }
    if (-not $isccPath) {
        $cmdCheck = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmdCheck) { $isccPath = $cmdCheck.Source }
    }

    if (-not $isccPath) {
        throw "Inno Setup Compiler (ISCC.exe) was not found on the system. Cannot compile installer."
    }

    Write-Host "Using Inno Setup compiler: $isccPath"
    $issPath = [System.IO.Path]::GetFullPath((Join-Path $scriptDir "setup.iss"))

    & $isccPath "/DMyAppVersion=$Version" "/O$resolvedDistDir" "/FLyrow-Setup-x64" "/Q" $issPath
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC compilation failed with exit code $LASTEXITCODE"
    }

    if (-not (Test-Path -LiteralPath $setupExePath)) {
        throw "Installer output not found at: $setupExePath"
    }

    $setupItem = Get-Item -LiteralPath $setupExePath
    Write-Host "Inno Setup Installer generated: $($setupItem.FullName) ($([math]::Round($setupItem.Length / 1MB, 2)) MB)"
} else {
    Write-Host "`n[4/5] Skipping installer step as requested."
}

# -----------------------------------------------------------------
# 5. Verification & SHA-256 Checksums
# -----------------------------------------------------------------
Write-Host "`n[5/5] Verifying package contents and computing SHA-256..."

# Verify ZIP contents
$archive = [System.IO.Compression.ZipFile]::OpenRead($portableZipPath)
try {
    $entries = $archive.Entries
    Write-Host " - Portable ZIP entries: $($entries.Count)"

    # Check top-level folder prefix (normalize slashes)
    $invalidTop = $entries | Where-Object { 
        $norm = $_.FullName.Replace('\', '/')
        -not ($norm.StartsWith("$topFolderName/") -or $norm -eq $topFolderName)
    }
    if ($invalidTop) {
        throw "Validation Failed: Found entries not inside top-level folder '$topFolderName/': $($invalidTop[0].FullName)"
    }

    # Check Lyrow.App.exe presence
    $hasExe = $entries | Where-Object { 
        $norm = $_.FullName.Replace('\', '/')
        $norm -eq "$topFolderName/Lyrow.App.exe"
    }
    if (-not $hasExe) { throw "Validation Failed: $topFolderName/Lyrow.App.exe missing from portable ZIP." }

    # Check portable marker presence
    $hasMarker = $entries | Where-Object { 
        $norm = $_.FullName.Replace('\', '/')
        $norm -eq "$topFolderName/portable"
    }
    if (-not $hasMarker) { throw "Validation Failed: $topFolderName/portable marker missing from portable ZIP." }

    # Check absence of PDBs
    $pdbs = $entries | Where-Object { $_.FullName -like "*.pdb" }
    if ($pdbs) { throw "Validation Failed: PDB files were found in portable ZIP: $($pdbs[0].FullName)" }

    # Check absence of logs
    $logs = $entries | Where-Object { $_.FullName -like "*logs/app.log" -or $_.FullName -like "*/logs/*" }
    if ($logs) { throw "Validation Failed: Development log files were found in portable ZIP: $($logs[0].FullName)" }

    # Check absence of settings.json
    $settings = $entries | Where-Object { $_.FullName -like "*settings.json" }
    if ($settings) { throw "Validation Failed: Pre-existing settings.json was found in portable ZIP: $($settings[0].FullName)" }

    Write-Host " - Portable ZIP structure and cleanliness: PASS"
}
finally {
    $archive.Dispose()
}

# Generate sha256sums.txt
$shaScript = Join-Path $scriptDir "generate_checksums.ps1"
if (Test-Path $shaScript) {
    & $shaScript -DistPath $resolvedDistDir -OutputFile (Join-Path $resolvedDistDir "sha256sums.txt")
}

# Verify sha256sums.txt
$verifyScript = Join-Path $scriptDir "verify_checksums.ps1"
if (Test-Path $verifyScript) {
    & $verifyScript -ChecksumFile (Join-Path $resolvedDistDir "sha256sums.txt") -BaseDir $resolvedDistDir
}

Write-Host "`n=========================================================="
Write-Host "LYROW PACKAGING PIPELINE COMPLETED SUCCESSFULLY"
Write-Host "=========================================================="
