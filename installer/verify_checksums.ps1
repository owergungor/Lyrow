# Lyrow SHA-256 Checksum Verifier
# Verifies distribution files against sha256sums.txt

[CmdletBinding()]
param (
    [string]$ChecksumFile = "$PSScriptRoot\..\dist\sha256sums.txt",
    [string]$BaseDir = "$PSScriptRoot\..\dist"
)

function Get-FileSha256Hex([string]$filePath) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($filePath)
    try {
        $hashBytes = $sha.ComputeHash($stream)
        return [System.BitConverter]::ToString($hashBytes).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
        $sha.Dispose()
    }
}

try {
    if (-not (Test-Path -LiteralPath $ChecksumFile -PathType Leaf)) {
        [Console]::Error.WriteLine("Error: Checksum file '$ChecksumFile' does not exist.")
        exit 1
    }

    if (-not (Test-Path -LiteralPath $BaseDir -PathType Container)) {
        [Console]::Error.WriteLine("Error: Base directory '$BaseDir' does not exist.")
        exit 1
    }

    $baseDirResolved = (Get-Item -LiteralPath $BaseDir).FullName
    $lines = [System.IO.File]::ReadAllLines([System.IO.Path]::GetFullPath($ChecksumFile))

    $passed = 0
    $failed = 0
    $missing = 0
    $total = 0

    foreach ($rawLine in $lines) {
        $line = $rawLine.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) {
            continue
        }

        $total++

        if ($line -match '^([a-fA-F0-9]{64})\s+(.+)$') {
            $expectedHash = $matches[1].ToLowerInvariant()
            $fileName = $matches[2].Trim()
            $targetPath = Join-Path $baseDirResolved $fileName

            if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
                [Console]::WriteLine("$fileName : MISSING")
                $missing++
                continue
            }

            $actualHash = Get-FileSha256Hex -filePath $targetPath
            if ($actualHash -eq $expectedHash) {
                [Console]::WriteLine("$fileName : OK")
                $passed++
            }
            else {
                [Console]::WriteLine("$fileName : FAILED (Checksum mismatch! Expected: $expectedHash, Computed: $actualHash)")
                $failed++
            }
        }
        else {
            [Console]::WriteLine("Unrecognized line format: $line")
            $failed++
        }
    }

    [Console]::WriteLine("Verification Summary: Total: $total | Passed: $passed | Failed: $failed | Missing: $missing")

    if ($total -eq 0) {
        [Console]::Error.WriteLine("Error: No valid checksum entries found in '$ChecksumFile'.")
        exit 1
    }

    if ($failed -gt 0 -or $missing -gt 0) {
        [Console]::Error.WriteLine("Error: Checksum verification failed ($failed corrupted, $missing missing).")
        exit 1
    }

    [Console]::WriteLine("All $passed file(s) successfully verified.")
    exit 0
}
catch {
    [Console]::Error.WriteLine("Error during checksum verification: $_")
    exit 1
}
