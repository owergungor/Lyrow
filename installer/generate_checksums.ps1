# Lyrow SHA-256 Checksum Generator
# Generates sha256sums.txt for distribution files in dist/ directory

[CmdletBinding()]
param (
    [string]$DistPath = "$PSScriptRoot\..\dist",
    [string]$OutputFile = "$PSScriptRoot\..\dist\sha256sums.txt"
)

try {
    if (-not (Test-Path -LiteralPath $DistPath -PathType Container)) {
        [Console]::Error.WriteLine("Error: Distribution directory '$DistPath' does not exist or is not a directory.")
        exit 1
    }

    $distDirInfo = Get-Item -LiteralPath $DistPath
    $resolvedDistPath = $distDirInfo.FullName

    $outputFileName = [System.IO.Path]::GetFileName($OutputFile)
    $files = Get-ChildItem -LiteralPath $resolvedDistPath -File | Where-Object { $_.Name -ne $outputFileName }

    if (-not $files -or $files.Count -eq 0) {
        [Console]::Error.WriteLine("Error: No distribution files found in '$resolvedDistPath' to compute checksums.")
        exit 1
    }

    $parentDir = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($OutputFile))
    if (-not [string]::IsNullOrWhiteSpace($parentDir) -and -not (Test-Path -LiteralPath $parentDir)) {
        [System.IO.Directory]::CreateDirectory($parentDir) | Out-Null
    }

    $checksumEntries = [System.Collections.Generic.List[string]]::new()

    foreach ($file in $files) {
        $hashResult = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
        $hash = $hashResult.Hash.ToLowerInvariant()
        $entry = "$hash  $($file.Name)"
        $checksumEntries.Add($entry)
        Write-Host "$entry"
    }

    [System.IO.File]::WriteAllLines([System.IO.Path]::GetFullPath($OutputFile), $checksumEntries, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Successfully generated SHA-256 checksums for $($checksumEntries.Count) file(s) at: $OutputFile"
    exit 0
}
catch {
    [Console]::Error.WriteLine("Error generating checksums: $_")
    exit 1
}
