$ErrorActionPreference = "Stop"

Write-Host "========================================"
Write-Host "LYROW v2.8 WINDOWS SMOKE TEST HARNESS"
Write-Host "========================================"

Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class SmokeWin32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
"@ -ErrorAction SilentlyContinue

# A) Launch
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath "publish\win-x64\Lyrow.App.exe" -ArgumentList "--smoke-test" -PassThru
$startupElapsedMs = $stopwatch.ElapsedMilliseconds

# B) PID
$pidNum = $proc.Id
Write-Host "[A] Launch: Triggered"
Write-Host "[B] PID: $pidNum (Startup detected in ${startupElapsedMs} ms)"

$startupPass = ($proc -ne $null -and -not $proc.HasExited)
Write-Host "Startup: $(if ($startupPass) { 'PASS' } else { 'FAIL' })"

# C) 2-3 saniye bekle
Start-Sleep -Seconds 3
$proc.Refresh()

# D & E) process alive & Responding kontrolü
$aliveAt3s = -not $proc.HasExited
$respondingAt3s = $proc.Responding
Write-Host "[C, D, E] At 3s - Alive: $aliveAt3s, Responding: $respondingAt3s"

# F & G) 6+ saniye bekle ve tekrar kontrol et (Toplam 9s)
Start-Sleep -Seconds 6
$proc.Refresh()
$aliveAt9s = -not $proc.HasExited
$respondingAt9s = $proc.Responding

$targetHwnd = [IntPtr]::Zero
[SmokeWin32]::EnumWindows({
    param($h, $l)
    [uint32]$wPid = 0
    [SmokeWin32]::GetWindowThreadProcessId($h, [ref]$wPid) | Out-Null
    if ($wPid -eq $pidNum) {
        $sb = New-Object System.Text.StringBuilder 256
        [SmokeWin32]::GetClassName($h, $sb, 256) | Out-Null
        if ($sb.ToString() -eq "WinUIDesktopWin32WindowClass") {
            $script:targetHwnd = $h
            return $false
        }
    }
    return $true
}, [IntPtr]::Zero) | Out-Null

$hwnd = if ($targetHwnd -ne [IntPtr]::Zero) { $targetHwnd } else { $proc.MainWindowHandle }
Write-Host "[F, G] At 9s - Alive: $aliveAt9s, Responding: $respondingAt9s, HWND: $hwnd"

$runtimePass = ($aliveAt3s -and $aliveAt9s)
$respondingPass = ($respondingAt3s -and $respondingAt9s)
$hwndPass = ($hwnd -ne [IntPtr]::Zero)

# H) Graceful shutdown
Write-Host "[H] Requesting Graceful Shutdown via WM_CLOSE / CloseMainWindow()..."
$proc.CloseMainWindow() | Out-Null
if ($targetHwnd -ne [IntPtr]::Zero) {
    [SmokeWin32]::PostMessage($targetHwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
}

# I) Kapanma timeout'u (up to 5 seconds)
$gracefulExited = $proc.WaitForExit(5000)
$forcedTerminated = $false

# J & K) Graceful kapanmazsa force terminate
if (-not $gracefulExited) {
    Write-Host "[J] Graceful shutdown timed out; executing forced termination (Kill)..."
    $proc.Kill()
    $proc.WaitForExit(2000)
    $forcedTerminated = $true
} else {
    Write-Host "[I] Process gracefully terminated within timeout."
}

$exitCode = $proc.ExitCode
$crashEvidence = if ($exitCode -ne 0 -and -not $forcedTerminated) { "YES (Exit code $exitCode)" } else { "NONE" }
$hangEvidence = if (-not $respondingPass) { "YES" } else { "NONE" }

Write-Host "========================================"
Write-Host "SMOKE TEST RESULTS SUMMARY"
Write-Host "========================================"
Write-Host "Startup:             $(if ($startupPass) { 'PASS' } else { 'FAIL' })"
Write-Host "Responding:          $(if ($respondingPass) { 'PASS' } else { 'FAIL' })"
Write-Host "Runtime:             $(if ($runtimePass) { 'PASS' } else { 'FAIL' })"
Write-Host "HWND:                $(if ($hwndPass) { 'PASS' } else { 'FAIL' }) ($hwnd)"
Write-Host "Graceful shutdown:   $(if ($gracefulExited) { 'PASS' } else { 'FAIL' })"
Write-Host "Forced termination:  $(if ($forcedTerminated) { 'YES' } else { 'NO' })"
Write-Host "Process exit code:   $exitCode"
Write-Host "Crash evidence:      $crashEvidence"
Write-Host "Hang evidence:       $hangEvidence"
Write-Host "========================================"
