# Lyrow Packaging Pipeline
# Builds clean, deterministic Portable ZIP and Inno Setup Installer packages.

[CmdletBinding()]
param (
    [string]$Version = "4.2",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishDir = "",
    [string]$DistDir = "",
    [switch]$SkipPublish,
    [switch]$SkipInstaller,
    [switch]$SkipMsix
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

    # Also produce versioned installer copy (Lyrow-<Version>-Setup-x64.exe)
    $versionedSetupPath = Join-Path $resolvedDistDir "Lyrow-$Version-Setup-x64.exe"
    Copy-Item -LiteralPath $setupExePath -Destination $versionedSetupPath -Force
    Write-Host "Versioned Setup Installer: $versionedSetupPath"
} else {
    Write-Host "`n[4/6] Skipping installer step as requested."
}

# -----------------------------------------------------------------
# 5. Build MSIX Package (x64) via MakeAppx.exe
# -----------------------------------------------------------------
$msixFileName = "Lyrow-v$Version-x64.msix"
$msixPath = Join-Path $resolvedDistDir $msixFileName

if (-not $SkipMsix) {
    Write-Host "`n[5/6] Building MSIX x64 package: $msixFileName..."

    $makeAppxPath = $null
    $makeAppxCandidates = @(
        (Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Filter "makeappx.exe" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*\x64\*" } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName),
        (Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Filter "makeappx.exe" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*\x64\*" } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName)
    )
    foreach ($cand in $makeAppxCandidates) {
        if ($cand -and (Test-Path -LiteralPath $cand)) {
            $makeAppxPath = $cand
            break
        }
    }
    if (-not $makeAppxPath) {
        throw "makeappx.exe was not found on the system. Cannot build MSIX package."
    }

    $manifestTemplatePath = Join-Path $scriptDir "AppxManifest.xml.template"
    if (-not (Test-Path -LiteralPath $manifestTemplatePath)) {
        throw "MSIX AppxManifest template not found at: $manifestTemplatePath"
    }

    # Normalize version to 4-part MSIX format (e.g. 4.1 -> 4.1.0.0)
    $vParts = $Version.Split('.')
    $msixVersion = switch ($vParts.Length) {
        1 { "$($vParts[0]).0.0.0" }
        2 { "$($vParts[0]).$($vParts[1]).0.0" }
        3 { "$($vParts[0]).$($vParts[1]).$($vParts[2]).0" }
        default { "$($vParts[0]).$($vParts[1]).$($vParts[2]).$($vParts[3])" }
    }

    $msixStagingDir = Join-Path $env:TEMP ("Lyrow_Msix_Stage_" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $msixStagingDir -Force | Out-Null
    try {
        # Copy clean publish artifacts (excluding PDBs, logs, settings.json, portable marker)
        $msixSourceItems = Get-ChildItem -LiteralPath $resolvedPublishDir
        foreach ($item in $msixSourceItems) {
            if ($item.Name -like "*.pdb" -or
                $item.Name -eq "logs" -or
                $item.Name -eq "settings.json" -or
                $item.Name -eq "portable" -or
                $item.Name -eq ".portable" -or
                $item.Name -like "*.tmp") {
                continue
            }
            if ($item.PSIsContainer) {
                Copy-Item -LiteralPath $item.FullName -Destination $msixStagingDir -Recurse -Force
            } else {
                Copy-Item -LiteralPath $item.FullName -Destination $msixStagingDir -Force
            }
        }
        # Strip nested PDBs safely using -Filter (never -Include with -LiteralPath in PS 5.1)
        Get-ChildItem -LiteralPath $msixStagingDir -Recurse -Filter "*.pdb" -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

        # Generate exact required MSIX PNG assets from lyrow-logo.png
        $msixAssetsDir = Join-Path $msixStagingDir "Assets"
        if (-not (Test-Path -LiteralPath $msixAssetsDir)) {
            New-Item -ItemType Directory -Path $msixAssetsDir -Force | Out-Null
        }
        $sourceLogo = Join-Path $scriptDir "..\src\Lyrow.App\Assets\lyrow-logo.png"
        if (Test-Path -LiteralPath $sourceLogo) {
            Add-Type -AssemblyName System.Drawing
            $srcImg = [System.Drawing.Image]::FromFile((Resolve-Path $sourceLogo).Path)
            try {
                $targets = @(
                    @{ Name = "StoreLogo.png"; W = 50; H = 50 },
                    @{ Name = "Square44x44Logo.png"; W = 44; H = 44 },
                    @{ Name = "Square150x150Logo.png"; W = 150; H = 150 },
                    @{ Name = "Wide310x150Logo.png"; W = 310; H = 150 }
                )
                foreach ($t in $targets) {
                    $bmp = New-Object System.Drawing.Bitmap($t.W, $t.H)
                    $g = [System.Drawing.Graphics]::FromImage($bmp)
                    try {
                        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                        $g.Clear([System.Drawing.Color]::Transparent)
                        $side = [Math]::Min($t.W, $t.H)
                        $x = [int](($t.W - $side) / 2)
                        $y = [int](($t.H - $side) / 2)
                        $g.DrawImage($srcImg, $x, $y, $side, $side)
                        $bmp.Save((Join-Path $msixAssetsDir $t.Name), [System.Drawing.Imaging.ImageFormat]::Png)
                    } finally {
                        $g.Dispose()
                        $bmp.Dispose()
                    }
                }
            } finally {
                $srcImg.Dispose()
            }
        }

        # Render AppxManifest.xml
        $manifestContent = (Get-Content -LiteralPath $manifestTemplatePath -Raw).Replace("{{MSIX_VERSION}}", $msixVersion)
        Set-Content -LiteralPath (Join-Path $msixStagingDir "AppxManifest.xml") -Value $manifestContent -Encoding UTF8

        if (Test-Path -LiteralPath $msixPath) {
            Remove-Item -LiteralPath $msixPath -Force
        }

        Write-Host "Using MakeAppx: $makeAppxPath"
        & $makeAppxPath pack /d $msixStagingDir /p $msixPath /o
        if ($LASTEXITCODE -ne 0) {
            throw "makeappx pack failed with exit code $LASTEXITCODE"
        }

        $msixItem = Get-Item -LiteralPath $msixPath
        Write-Host "MSIX x64 package generated: $($msixItem.FullName) ($([math]::Round($msixItem.Length / 1MB, 2)) MB)"

        # Check for signing certificate (never claim signed if no certificate exists)
        if ($env:LYROW_PFX_PATH -and (Test-Path -LiteralPath $env:LYROW_PFX_PATH)) {
            $signToolPath = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Filter "signtool.exe" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*\x64\*" } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
            if ($signToolPath) {
                Write-Host "Signing MSIX with certificate: $env:LYROW_PFX_PATH"
                & $signToolPath sign /fd SHA256 /a /f $env:LYROW_PFX_PATH /p $env:LYROW_PFX_PASSWORD $msixPath
                if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }
                Write-Host "MSIX Signing Status: SIGNED"
            }
        } else {
            Write-Host "MSIX Signing Status: UNSIGNED (No code-signing certificate configured via LYROW_PFX_PATH)"
        }
    }
    finally {
        if (Test-Path -LiteralPath $msixStagingDir) {
            Remove-Item -LiteralPath $msixStagingDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
} else {
    Write-Host "`n[5/6] Skipping MSIX step as requested."
}

# -----------------------------------------------------------------
# 6. Verification & SHA-256 Checksums
# -----------------------------------------------------------------
Write-Host "`n[6/6] Verifying package contents and computing SHA-256..."

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
