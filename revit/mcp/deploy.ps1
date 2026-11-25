# 애드인 재배포 (총괄용): 저장은 먼저 save_active.cs 로 끝내 둘 것 (변경 False 확인 뒤 실행).
# Revit 정상 종료(CloseMainWindow, 강제 종료 없음) → 빌드 복사·해시 확인 → onsite.settings.json preview4d true → Revit 시작(사본 열기)
# 시작 뒤 "서명 없는 애드인" [항상 로드] 와 MCP Switch 는 화면에서 따로 (PowerShell 5.1: 이 파일은 UTF-8 BOM 으로 저장)
param([string]$Model = "C:\Users\admin\Desktop\시스템2\build-up-2\revit\shoring_demo_check8.rvt",   # 사용자 지시 11-05: 점검 사본은 check8 하나만, 새 사본·백업 쌓지 말 것
      [switch]$SelfTest)   # 저장 창 '아니요' 대상 이름 규칙만 시험 (Revit 안 건드림)
$ErrorActionPreference = "Stop"
# 11-08 총괄 승인 (재발 방지 ③): 닫을 때 뜬 '변경사항을 …에 저장하시겠습니까?' 는 회귀 점검 사본(*_check*)일 때만 '아니요'. 원본·시연 파일은 누르지 않고 멈춤
function Test-NoSaveTarget([string]$Name) { ($Name -like "*_check*.rvt") -and ($Name -notin @("shoring_demo.rvt", "shoring_demo_onsite.rvt")) }
if ($SelfTest) {
    $cases = [ordered]@{ "shoring_demo_check8.rvt" = $true; "kbim10_check.rvt" = $true; "shoring_demo_check8.0001.rvt" = $true; "shoring_demo.rvt" = $false;
                         "shoring_demo_onsite.rvt" = $false; "shoring_demo_mcp.rvt" = $false; "kbim10.rvt" = $false; "RAVA3pro_ARK_2026.rvt" = $false; "check8.rvt" = $false }
    $bad = @($cases.Keys | Where-Object { (Test-NoSaveTarget $_) -ne $cases[$_] })
    if ($bad.Count) { "저장 창 '아니요' 대상 시험 실패: $($bad -join ', ')"; exit 1 }
    "저장 창 '아니요' 대상 시험 통과 ($($cases.Count)가지: 점검 사본 3 = 아니요, 원본·시연·MCP 사본·다른 모델 6 = 누르지 않음)"; exit 0
}
$name = Split-Path $Model -Leaf
if ($name -in @("shoring_demo.rvt", "shoring_demo_onsite.rvt")) { throw "원본/시연 파일은 열지 않음: $name" }
$src = Join-Path $PSScriptRoot "..\RevitOnSite\RevitOnSite\bin\Release\net8.0-windows" | Resolve-Path
$dst = "$env:APPDATA\Autodesk\Revit\Addins\2026\RevitOnSite"
$p = Get-Process -Name Revit -ErrorAction SilentlyContinue | Select-Object -First 1
if ($p) {   # 열린 대화상자가 있으면 저장·종료가 막힘 (11-04 판정 결과 창 남아 종료 실패) → 먼저 닫게
    . "$PSScriptRoot\uia_lib.ps1"; $dl = @(Show-Dialogs | Where-Object { $_ -and $_ -ne "Revit On-Site — 결과" })   # 결과 창은 모델리스·주 창 소유라 Revit 과 같이 닫힘
    if ($dl.Count) { throw "Revit 에 열린 창이 있음: $($dl -join ", ") — 닫고 저장 확인 뒤 다시" }
}
if ($p) {
    $null = $p.CloseMainWindow()
    . "$PSScriptRoot\uia_lib.ps1"
    for ($i = 0; $i -lt 60 -and -not $p.HasExited; $i++) {
        Start-Sleep -Seconds 2
        try {   # 저장 창: 점검 사본이면 '아니요', 아니면 누르지 않고 멈춤 (Revit 이 이미 끝났으면 건너뜀)
            foreach ($top in @(Get-RvtTops)) {
                $els = @($top.FindAll($UTS::Descendants, $UTRUE))
                $q = $els | Where-Object { $_.Current.Name -match "변경사항을 (.+?\.rvt)에 저장" } | Select-Object -First 1
                if (-not $q -or $q.Current.Name -notmatch "변경사항을 (.+?\.rvt)에 저장") { continue }
                $nm = $Matches[1]
                if (-not (Test-NoSaveTarget $nm)) { throw "저장 창이 떴지만 점검 사본이 아님: $nm — 화면에서 직접 고르세요 (강제 종료하지 말 것)" }
                $no = $els | Where-Object { $_.Current.Name -like "아니요*" } | Select-Object -First 1
                if ($no) { Click-El $no; "저장 창 '아니요': $nm (점검 사본은 저장하지 않음)" }
            }
        } catch [System.Windows.Automation.ElementNotAvailableException] { } catch { if (-not $p.HasExited) { throw } }   # 11-12: Revit 이 막 끝나면 UIA 조건 만들기가 오류 (PropertyCondition) — 끝났으면 무시
    }
    if (-not $p.HasExited) { throw "Revit 이 닫히지 않음 (저장 확인 창?) — 화면 확인, 강제 종료하지 말 것" }
    "Revit 종료"
}
if ($name -like "*_check*.rvt") {   # 저장할 때 Revit 이 만드는 점검 사본 백업(.0001.rvt …)은 남기지 않음 — 휴지통으로 (비우기는 사용자)
    Add-Type -AssemblyName Microsoft.VisualBasic
    $stem = [IO.Path]::GetFileNameWithoutExtension($Model)
    Get-ChildItem (Split-Path $Model) -File | Where-Object { $_.Name -match ("^" + [regex]::Escape($stem) + "\.\d{4}\.rvt$") } | ForEach-Object {
        [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($_.FullName, 'OnlyErrorDialogs', 'SendToRecycleBin'); "백업 휴지통: $($_.Name)" }
}
for ($i = 0; $i -lt 30; $i++) {   # Revit 프로세스가 끝나도 DLL 잠금이 몇 초 남음 (11-04 23:5x 복사 실패)
    try { [IO.File]::Open("$dst\RevitOnSite.dll", 'Open', 'ReadWrite', 'None').Close(); break } catch { Start-Sleep -Seconds 1 }
}
Copy-Item "$src\*" $dst -Recurse -Force
[IO.File]::WriteAllText("$dst\onsite.settings.json", "{`n  `"preview4d`": true`n}`n")
$bad = @(Get-ChildItem $src -Recurse -File | Where-Object { $_.Name -ne "onsite.settings.json" } |
    Where-Object { (Get-FileHash $_.FullName).Hash -ne (Get-FileHash ($dst + $_.FullName.Substring($src.Path.Length))).Hash })
"복사 완료, 해시 불일치 $($bad.Count) · RevitOnSite.dll $((Get-Item "$dst\RevitOnSite.dll").LastWriteTime)"
Start-Process -FilePath "C:\Program Files\Autodesk\Revit 2026\Revit.exe" -ArgumentList "`"$Model`""
"Revit 시작: $name"
