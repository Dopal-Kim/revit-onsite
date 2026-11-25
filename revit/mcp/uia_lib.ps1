# UI Automation 도우미 (dot-source: . .\uia_lib.ps1). 변수 이름은 대소문자 구별이 없으니 $T·$t 같은 짝을 쓰지 말 것. (UTF-8 BOM)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$UA = [System.Windows.Automation.AutomationElement]; $UTS = [System.Windows.Automation.TreeScope]; $UTRUE = [System.Windows.Automation.Condition]::TrueCondition
function Get-RvtProc { Get-Process -Name Revit -ErrorAction SilentlyContinue | Select-Object -First 1 }
function Get-RvtTops { $p = Get-RvtProc; $UA::RootElement.FindAll($UTS::Children, (New-Object System.Windows.Automation.PropertyCondition($UA::ProcessIdProperty, $p.Id))) }
function Get-RvtEls { Get-RvtTops | ForEach-Object { $_.FindAll($UTS::Descendants, $UTRUE) } }
# 주의: 같은 이름이 리본과 대시보드에 함께 있음(예 "판정") — 대시보드 쪽은 Find-Dash 사용 (2026-11-04 N11 오판 원인)
function Find-Dash([string]$Name) { Get-RvtEls | Where-Object { $_.Current.Name -eq $Name -and $_.Current.ControlType.ProgrammaticName -eq "ControlType.Button" -and $_.Current.BoundingRectangle.X -gt 1500 } | Select-Object -First 1 }
function Find-Rvt([string]$Name, [string]$Type) { Get-RvtEls | Where-Object { ($_.Current.Name -replace "`r?`n", " ") -eq $Name -and (-not $Type -or $_.Current.ControlType.ProgrammaticName -eq "ControlType.$Type") } | Select-Object -First 1 }
function Invoke-Rvt([string]$Name, [string]$Type = "Button") {
    $el = Find-Rvt $Name $Type; if (-not $el) { throw "없음: $Name ($Type)" }
    foreach ($pat in [System.Windows.Automation.InvokePattern]::Pattern, [System.Windows.Automation.SelectionItemPattern]::Pattern, [System.Windows.Automation.TogglePattern]::Pattern) {
        $o = $null; if ($el.TryGetCurrentPattern($pat, [ref]$o)) { if ($o -is [System.Windows.Automation.InvokePattern]) { $o.Invoke() } elseif ($o -is [System.Windows.Automation.SelectionItemPattern]) { $o.Select() } else { $o.Toggle() }; return "누름: $Name" } }
    throw "패턴 없음: $Name"
}
function Show-Dialogs { Get-RvtTops | ForEach-Object { $_.FindAll($UTS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($UA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window))) } | ForEach-Object { $_.Current.Name } }
function Show-In([string]$Window) {
    $w = Get-RvtTops | ForEach-Object { $_; $_.FindAll($UTS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($UA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window))) } | Where-Object { $_.Current.Name -like "*$Window*" } | Select-Object -First 1
    if (-not $w) { return "창 없음: $Window" }
    $w.FindAll($UTS::Descendants, $UTRUE) | Where-Object { $_.Current.Name -or $_.Current.AutomationId } | ForEach-Object { "{0} | {1} | {2}" -f $_.Current.ControlType.ProgrammaticName, ($_.Current.Name -replace "`r?`n", " "), $_.Current.AutomationId }
}
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class UM {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
  public struct POINT { public int X, Y; } }
"@
[UM]::SetProcessDPIAware() | Out-Null
function Click-El($el) {   # 패턴 없는 TaskDialog 버튼 등: 먼저 그 버튼 창에 BM_CLICK(가려져도 됨, Revit 프로세스 창일 때만) → 안 되면 실제 마우스 (Claude 창은 잠깐 최소화)
    $bh = [IntPtr]$el.Current.NativeWindowHandle
    if ($bh -ne [IntPtr]::Zero) { $o0 = 0; [UM]::GetWindowThreadProcessId($bh, [ref]$o0) | Out-Null
        if ($o0 -eq (Get-RvtProc).Id) { [UM]::SendMessage($bh, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; return "눌림(BM_CLICK): " + $el.Current.Name } }
    $r = $el.Current.BoundingRectangle; $pt = New-Object UM+POINT; $pt.X = [int]($r.X + $r.Width / 2); $pt.Y = [int]($r.Y + $r.Height / 2)
    $cl = Get-Process -Name claude -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    try {
        if ($cl) { [UM]::ShowWindow($cl.MainWindowHandle, 6) | Out-Null; Start-Sleep -Milliseconds 600 }
        [UM]::SetForegroundWindow((Get-RvtProc).MainWindowHandle) | Out-Null; Start-Sleep -Milliseconds 400   # 브라우저 등 다른 창이 덮어도 Revit 을 앞으로
        $h = [UM]::WindowFromPoint($pt); $o = 0; [UM]::GetWindowThreadProcessId($h, [ref]$o) | Out-Null
        if ($o -ne (Get-RvtProc).Id) { throw "($($pt.X), $($pt.Y)) 는 Revit 창이 아님 — 누르지 않음" }
        [UM]::SetForegroundWindow([UM]::GetAncestor($h, 2)) | Out-Null; Start-Sleep -Milliseconds 300
        $old = New-Object UM+POINT; [UM]::GetCursorPos([ref]$old) | Out-Null; [UM]::SetCursorPos($pt.X, $pt.Y) | Out-Null; Start-Sleep -Milliseconds 100
        [UM]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [UM]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 200; [UM]::SetCursorPos($old.X, $old.Y) | Out-Null
    } finally { if ($cl) { [UM]::ShowWindow($cl.MainWindowHandle, 9) | Out-Null } }
    "눌림: " + $el.Current.Name
}
function Close-Note([string]$Window) {   # 그 창의 '닫기(C)' 를 누름
    $w = Get-RvtTops | ForEach-Object { $_; $_.FindAll($UTS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($UA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window))) } | Where-Object { $_.Current.Name -eq $Window -and $_.Current.ControlType.ProgrammaticName -eq "ControlType.Window" } | Select-Object -Last 1
    $b = $w.FindAll($UTS::Descendants, $UTRUE) | Where-Object { $_.Current.Name -eq "닫기(C)" } | Select-Object -First 1
    if (-not $b) { return "닫기 없음: $Window" }; Click-El $b
}
function Approve([string]$Action, [string]$Actor, [string]$Note) {   # 열린 [승인 기록] 창에 행위·입력자·메모 넣고 [기록]
    $w = Get-RvtTops | ForEach-Object { $_; $_.FindAll($UTS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($UA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window))) } | Where-Object { $_.Current.Name -eq "승인 기록" } | Select-Object -First 1
    if (-not $w) { throw "승인 기록 창 없음" }
    $by = { param($id) $w.FindFirst($UTS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($UA::AutomationIdProperty, $id))) }
    $cb = & $by "Action"; $cb.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
    $it = $cb.FindAll($UTS::Descendants, $UTRUE) | Where-Object { $_.Current.ControlType.ProgrammaticName -eq "ControlType.ListItem" -and $_.Current.Name -match [regex]::Escape($Action) } | Select-Object -First 1
    if (-not $it) { throw "행위 없음: $Action — 목록: " + (($cb.FindAll($UTS::Descendants, $UTRUE) | Where-Object { $_.Current.ControlType.ProgrammaticName -eq "ControlType.ListItem" } | ForEach-Object { $_.Current.Name }) -join ", ") }
    $it.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $cb.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    (& $by "Actor").GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Actor)
    (& $by "Note").GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Note)
    "행위 힌트: " + (& $by "ActionHint").Current.Name
    (& $by "OkButton").GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Click-At([int]$X, [int]$Y) {   # 화면 좌표(DPI 보정 픽셀)를 실제 마우스로 — Revit 창일 때만, Claude 창은 잠깐 최소화
    $pt = New-Object UM+POINT; $pt.X = $X; $pt.Y = $Y
    $cl = Get-Process -Name claude -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    try {
        if ($cl) { [UM]::ShowWindow($cl.MainWindowHandle, 6) | Out-Null; Start-Sleep -Milliseconds 600 }
        [UM]::SetForegroundWindow((Get-RvtProc).MainWindowHandle) | Out-Null; Start-Sleep -Milliseconds 400   # 브라우저 등 다른 창이 덮어도 Revit 을 앞으로
        $h = [UM]::WindowFromPoint($pt); $o = 0; [UM]::GetWindowThreadProcessId($h, [ref]$o) | Out-Null
        if ($o -ne (Get-RvtProc).Id) { throw "($X, $Y) 는 Revit 창이 아님 — 누르지 않음" }
        [UM]::SetForegroundWindow([UM]::GetAncestor($h, 2)) | Out-Null; Start-Sleep -Milliseconds 300
        $old = New-Object UM+POINT; [UM]::GetCursorPos([ref]$old) | Out-Null; [UM]::SetCursorPos($X, $Y) | Out-Null; Start-Sleep -Milliseconds 100
        [UM]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [UM]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 200; [UM]::SetCursorPos($old.X, $old.Y) | Out-Null
    } finally { if ($cl) { [UM]::ShowWindow($cl.MainWindowHandle, 9) | Out-Null } }
    "눌림 ($X, $Y)"
}
