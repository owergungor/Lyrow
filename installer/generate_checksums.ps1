# Lyrow SHA-256 Checksum Generator
# Generates sha256sums.txt for distribution files in dist/ directory

[CmdletBinding()]
param (
    [string]$DistPath = "$PSScriptRoot\..\dist",
    [string]$OutputFile = "$PSScriptRoot\..\dist\sha256sums.txt"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $DistPath)) {
    Write-Warning "Distribution directory '$DistPath' does not exist."
    exit 0
}

$files = Get-ChildItem -Path $DistPath -File | Where-Object { $_.Name -ne "sha256sums.txt" }

if ($files.Count -eq 0) {
    Write-Warning "No files found in '$DistPath' to calculate checksums."
    exit 0
}

$checksumEntries = [System.Collections.Generic.List[string]]::new()

foreach ($file in $files) {
    $hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $entry = "$hash  $($file.Name)"
    $checksumEntries.Add($entry)
    Write-Host "$entry"
}

[System.IO.File]::WriteAllLines($OutputFile, $checksumEntries)
Write-Host "Successfully generated SHA-256 checksums at: $OutputFile"
