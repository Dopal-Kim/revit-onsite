# 4D 미리보기 색 확인 (개발용, 사본에서만): 날짜 3개 × 표시 방식 3개를 frame4d.cs 로 그리게 하고 Revit 창을 PrintWindow 로 캡처
# 사용: powershell -ExecutionPolicy Bypass -File revit\mcp\capture4d.ps1 -OutDir revit\screens\4d_check
param([string]$OutDir = (Join-Path $PSScriptRoot "..\screens\4d_check"))
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $OutDir | Out-Null
$times = @("2026-10-05T09:00", "2026-11-26T13:00", "2026-12-31T09:00")
# 사실적(Realistic)에선 4D 덧그림이 안 보여 애드인이 음영으로 바꿈 (9차 N3) → 시험하는 방식은 일관된 색상·음영·음영+모서리. 마지막을 음영으로 두어 뒤의 재생 점검에 그대로
$styles = @("FlatColors", "ShadingWithEdges", "Shading")
Push-Location $PSScriptRoot
try {
    node rv.js frame4d.cs none ('[\"' + $times[0] + '\",\"\"]') | Out-Null   # 미리보기 뷰가 없으면 이 프레임에서 만듦
    Start-Sleep -Seconds 8
    foreach ($s in $styles) {
        foreach ($t in $times) {
            $r = node rv.js frame4d.cs none ('[\"' + $t + '\",\"' + $s + '\"]')   # PowerShell 5.1 은 따옴표를 빼고 넘기므로 \" 로
            Start-Sleep -Seconds 5
            $name = "4d_" + $t.Substring(5, 5).Replace("-", "") + "_" + $t.Substring(11, 2) + "h_" + $s + ".png"
            & (Join-Path $PSScriptRoot "capture.ps1") -Out (Join-Path $OutDir $name)
            ($r | Select-String "result") -join " "
        }
    }
} finally { Pop-Location }
