# 8차 점검: 실제 마우스 ▶ → 재생 중 캡처 2장 → ❚❚ (같은 자리) → 멈춤 캡처. Claude 창은 클릭 동안만 최소화. (UTF-8 BOM)
param([int]$PlayX = 1560, [int]$PauseX = 1589, [int]$Y = 663, [Parameter(Mandatory)][string]$Prefix)   # Prefix 예 ui11 — 이전 캡처를 덮지 않게
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SW9 { [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c); }
"@
$m = $PSScriptRoot; $o = Join-Path $PSScriptRoot "..\screens"
$cl = Get-Process -Name claude -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
try {
    if ($cl) { [SW9]::ShowWindow($cl.MainWindowHandle, 6) | Out-Null; Start-Sleep -Milliseconds 800 }
    & "$m\capture.ps1" -Out "$o\${Prefix}_00_재생전.png"
    & "$m\click.ps1" -X $PlayX -Y $Y; Start-Sleep 4; & "$m\capture.ps1" -Out "$o\${Prefix}_01_재생중.png"
    Start-Sleep 6; & "$m\capture.ps1" -Out "$o\${Prefix}_02_재생중_6초뒤.png"
    & "$m\click.ps1" -X $PauseX -Y $Y; Start-Sleep 3; & "$m\capture.ps1" -Out "$o\${Prefix}_03_멈춤.png"
} finally { if ($cl) { [SW9]::ShowWindow($cl.MainWindowHandle, 9) | Out-Null } }
