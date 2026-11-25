# Claude 창을 잠깐 최소화하고 click.ps1 로 여러 점을 차례로 누른 뒤 되돌림 (실제 마우스). 사용: with_mouse.ps1 -Points "1614,551" [-Double] (UTF-8 BOM)
param([string[]]$Points, [switch]$Double, [int]$WaitMs = 1500)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SWM { [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c); }
"@
$cl = Get-Process -Name claude -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
try {
    if ($cl) { [SWM]::ShowWindow($cl.MainWindowHandle, 6) | Out-Null; Start-Sleep -Milliseconds 800 }
    foreach ($pt in $Points) { $x, $y = $pt -split ","; if ($Double) { & "$PSScriptRoot\click.ps1" -X $x -Y $y -Double } else { & "$PSScriptRoot\click.ps1" -X $x -Y $y }; Start-Sleep -Milliseconds $WaitMs }
} finally { if ($cl) { [SWM]::ShowWindow($cl.MainWindowHandle, 9) | Out-Null } }
