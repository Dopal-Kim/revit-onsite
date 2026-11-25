# N9 점검: 대시보드 버튼을 실제 마우스로 누른 뒤 키보드가 Revit 에 가는지 (VV → 가시성/그래픽 창, Space → 판정 다시 안 돎). (UTF-8 BOM)
param([int]$X = 1797, [int]$Y = 602)   # 기본: [같은 층만]
. "$PSScriptRoot\uia_lib.ps1"
$cl = Get-Process -Name claude -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
try {
    if ($cl) { [UM]::ShowWindow($cl.MainWindowHandle, 6) | Out-Null; Start-Sleep -Milliseconds 800 }
    & "$PSScriptRoot\click.ps1" -X $X -Y $Y; Start-Sleep 2
    $before = @(Show-Dialogs)
    & "$PSScriptRoot\keys.ps1" -Keys "VV"; Start-Sleep 3
    $vv = @(Show-Dialogs) | Where-Object { $_ -notin $before }
    "VV 뒤 새 창: " + ($vv -join ", ")
    if ($vv) { & "$PSScriptRoot\keys.ps1" -Keys "{ESC}"; Start-Sleep 2 }
    & "$PSScriptRoot\click.ps1" -X $X -Y $Y; Start-Sleep 2
    & "$PSScriptRoot\keys.ps1" -Keys " "; Start-Sleep 4
    "Space 뒤 창: " + ((@(Show-Dialogs) | Where-Object { $_ -notin $before }) -join ", ")
} finally { if ($cl) { [UM]::ShowWindow($cl.MainWindowHandle, 9) | Out-Null } }
