# Revit 시작 뒤 (deploy.ps1 다음): ① "서명 없는 애드인" 창이 Revit On-Site 이면 [항상 로드] ② 모델 창 대기
# ③ 애드인 탭 → Revit MCP Switch (8080 이 닫혀 있을 때만) ④ MCP 플러그인 "Open Server" 알림 [닫기]
# 버튼이 UIA 패턴 없는 창(TaskDialog)이라 실제 마우스 클릭 — 점이 Revit 창일 때만 누르고 커서를 되돌림. (UTF-8 BOM 저장)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class AS {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  public struct POINT { public int X, Y; } }
"@
[AS]::SetProcessDPIAware() | Out-Null
$A = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]; $TRUEC = [System.Windows.Automation.Condition]::TrueCondition
function Get-Rvt { Get-Process -Name Revit -ErrorAction SilentlyContinue | Select-Object -First 1 }
function Tops($p) { $A::RootElement.FindAll($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id))) }
function Press($el) { . "$PSScriptRoot\uia_lib.ps1"; Click-El $el; return }   # Claude 창이 덮어도 되게 uia_lib Click-El (잠깐 최소화)
function PressOld($el) {   # 요소 가운데를 실제 마우스로
    $r = $el.Current.BoundingRectangle; $pt = New-Object AS+POINT; $pt.X = [int]($r.X + $r.Width / 2); $pt.Y = [int]($r.Y + $r.Height / 2)
    $h = [AS]::WindowFromPoint($pt); $o = 0; [AS]::GetWindowThreadProcessId($h, [ref]$o) | Out-Null
    if ($o -ne (Get-Rvt).Id) { throw "($($pt.X), $($pt.Y)) 는 Revit 창이 아님 — 누르지 않음" }
    [AS]::SetForegroundWindow([AS]::GetAncestor($h, 2)) | Out-Null; Start-Sleep -Milliseconds 300
    $old = New-Object AS+POINT; [AS]::GetCursorPos([ref]$old) | Out-Null
    [AS]::SetCursorPos($pt.X, $pt.Y) | Out-Null; Start-Sleep -Milliseconds 100
    [AS]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [AS]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 200; [AS]::SetCursorPos($old.X, $old.Y) | Out-Null
}
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 3; $p = Get-Rvt; if (-not $p) { continue }
    $t = @(Tops $p)
    $sec = $t | ForEach-Object { $_; $_.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window))) } | Where-Object { $_.Current.Name -eq "보안 - 서명 없는 애드인" } | Select-Object -First 1
    if ($sec) {
        $info = ($sec.FindAll($TS::Descendants, $TRUEC) | Where-Object { $_.Current.Name -match "이름:" } | Select-Object -First 1).Current.Name
        if ($info -notmatch "Revit On-Site" -or $info -notmatch "Addins\\2026\\RevitOnSite\\RevitOnSite.dll") { throw "모르는 애드인 확인 창 — 사람이 판단: $info" }
        Press ($sec.FindAll($TS::Descendants, $TRUEC) | Where-Object { $_.Current.Name -eq "항상 로드" } | Select-Object -First 1); "항상 로드 (Revit On-Site)"; continue
    }
    if ($t | Where-Object { $_.Current.Name -like "Autodesk Revit 2026*.rvt*" }) { "모델 창: " + ($t[0].Current.Name); break }
}
if (-not (Get-NetTCPConnection -State Listen -LocalPort 8080 -ErrorAction SilentlyContinue)) {
    $els = Tops (Get-Rvt) | ForEach-Object { $_.FindAll($TS::Descendants, $TRUEC) }
    ($els | Where-Object { $_.Current.Name -eq "애드인" -and $_.Current.ControlType.ProgrammaticName -eq "ControlType.Button" } | Select-Object -First 1).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    $sw = Tops (Get-Rvt) | ForEach-Object { $_.FindAll($TS::Descendants, $TRUEC) } | Where-Object { $_.Current.ControlType.ProgrammaticName -eq "ControlType.Button" -and $_.Current.Name -match "Revit MCP" } | Select-Object -First 1
    $sw.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    for ($i = 0; $i -lt 10 -and -not (Get-NetTCPConnection -State Listen -LocalPort 8080 -ErrorAction SilentlyContinue); $i++) { Start-Sleep 1 }
    "MCP 8080 " + $(if (Get-NetTCPConnection -State Listen -LocalPort 8080 -ErrorAction SilentlyContinue) { "열림" } else { "닫힘" })
}
for ($i = 0; $i -lt 6; $i++) {
    $note = Tops (Get-Rvt) | ForEach-Object { $_.FindAll($TS::Children, $TRUEC) } | Where-Object { $_.Current.Name -match "revitMCP" } | Select-Object -First 1
    if ($note) { Press ($note.FindAll($TS::Descendants, $TRUEC) | Where-Object { $_.Current.Name -eq "닫기(C)" } | Select-Object -First 1); "MCP 알림 닫음"; break }
    Start-Sleep 1
}
