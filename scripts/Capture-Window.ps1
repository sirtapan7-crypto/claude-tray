<#
.SYNOPSIS
    Launch a window and capture it to a PNG — the visual feedback loop for UI work.

.DESCRIPTION
    Starts an executable (default: the Settings preview via `ClaudeTray.exe --settings`), waits for
    its main window, brings it to the foreground, and saves a PNG of just that window's rectangle.
    This is what lets the layout be verified by *looking* at it instead of guessing, and it is fully
    deterministic — no clicking through the tray menu.

.PARAMETER Exe
    Path to the executable to launch. Defaults to the Debug build's ClaudeTray.exe.

.PARAMETER AppArgs
    Arguments passed to the exe. Defaults to "--settings".

.PARAMETER Out
    Output PNG path. Defaults to docs\_preview\settings.png.

.PARAMETER WaitMs
    Milliseconds to wait for the window to render before capturing. Default 1500.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\Capture-Window.ps1
#>
param(
    [string]$Exe = "bin\Debug\net10.0-windows\win-x64\ClaudeTray.exe",
    [string]$AppArgs = "--settings",
    [string]$Out = "docs\_preview\settings.png",
    [int]$WaitMs = 1500
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$src = @"
using System;
using System.Runtime.InteropServices;
public static class Win {
    // Per-monitor-v2 DPI awareness so GetWindowRect/CopyFromScreen use the SAME physical-pixel
    // coordinate space the window actually lives in — otherwise the capture is offset/scaled on
    // high-DPI displays (e.g. 200%).
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public static readonly IntPtr PER_MONITOR_AWARE_V2 = new IntPtr(-4);
    public static void MakeDpiAware() {
        try { if (SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)) return; } catch {}
        try { SetProcessDPIAware(); } catch {}
    }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
Add-Type -TypeDefinition $src
[Win]::MakeDpiAware()

$outDir = Split-Path -Parent $Out
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Force $outDir | Out-Null }

$proc = Start-Process -FilePath $Exe -ArgumentList $AppArgs -PassThru
try {
    # Wait for a non-zero main window handle.
    $deadline = (Get-Date).AddMilliseconds($WaitMs + 4000)
    while ($proc.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 100
        $proc.Refresh()
    }
    if ($proc.MainWindowHandle -eq 0) { throw "Window never appeared for $Exe $AppArgs" }

    Start-Sleep -Milliseconds $WaitMs   # let WPF finish first paint / Mica settle
    $h = $proc.MainWindowHandle
    [Win]::ShowWindow($h, 9) | Out-Null   # SW_RESTORE
    [Win]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 300

    $r = New-Object Win+RECT
    [Win]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left
    $hgt = $r.Bottom - $r.Top
    if ($w -le 0 -or $hgt -le 0) { throw "Bad window rect ($w x $hgt)" }

    $bmp = New-Object System.Drawing.Bitmap $w, $hgt
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $hgt))
    $bmp.Save((Resolve-Path -LiteralPath $outDir).Path + "\" + (Split-Path -Leaf $Out),
              [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "Captured $w x $hgt -> $Out"
}
finally {
    if (-not $proc.HasExited) { $proc.Kill() }
}
