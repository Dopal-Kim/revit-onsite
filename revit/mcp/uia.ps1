# Revit 화면 조작 (Windows UI Automation) — 마우스·키보드를 뺏지 않고 Revit 요소를 직접 누름
# 사용: uia.ps1 -Find <이름 일부>            : 이름에 글자가 들어간 요소 목록 (종류·이름·자동화ID)
#       uia.ps1 -Click <정확한 이름> [-Type Button|TabItem|...] : 첫 요소를 Invoke/Select/Toggle
#       uia.ps1 -Set <정확한 이름> -Value <값>   : 입력칸 값 넣기 (ValuePattern)
#       uia.ps1 -Windows                        : Revit 프로세스의 최상위 창(대화상자 포함) 목록
param([string]$Find, [string]$Click, [string]$Set, [string]$Value, [string]$Type, [switch]$Windows, [string]$Root)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$T = [System.Windows.Automation.TreeScope]
$revit = (Get-Process -Id ((Get-NetTCPConnection -State Listen -LocalPort 8080 -ErrorAction SilentlyContinue | Select-Object -First 1).OwningProcess) -ErrorAction SilentlyContinue)
$pidCond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $revit.Id)
$tops = $A::RootElement.FindAll($T::Children, $pidCond)
if ($Windows) { foreach ($w in $tops) { "{0} | {1}" -f $w.Current.ControlType.ProgrammaticName, $w.Current.Name }; return }
$scope = if ($Root) { $tops | Where-Object { $_.Current.Name -like "*$Root*" } } else { $tops }
function All() { foreach ($w in $scope) { $w.FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition) } }
if ($Find) {
  All | Where-Object { $_.Current.Name -like "*$Find*" } | Select-Object -First 40 | ForEach-Object {
    "{0} | {1} | {2}" -f $_.Current.ControlType.ProgrammaticName, ($_.Current.Name -replace "`r?`n", " "), $_.Current.AutomationId }
  return
}
$name = if ($Click) { $Click } else { $Set }
$el = All | Where-Object { ($_.Current.Name -replace "`r?`n", " ") -eq $name -and (-not $Type -or $_.Current.ControlType.ProgrammaticName -eq "ControlType.$Type") } | Select-Object -First 1
if (-not $el) { throw "요소 없음: $name" }
if ($Set) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value); "set $name = $Value"; return }
foreach ($pat in 'InvokePattern', 'SelectionItemPattern', 'TogglePattern', 'ExpandCollapsePattern') {
  $p = $null
  if ($el.TryGetCurrentPattern(([type]"System.Windows.Automation.$pat")::Pattern, [ref]$p)) {
    switch ($pat) { 'InvokePattern' { $p.Invoke() } 'SelectionItemPattern' { $p.Select() } 'TogglePattern' { $p.Toggle() } 'ExpandCollapsePattern' { $p.Expand() } }
    "clicked $name ($pat)"; return
  }
}
throw "누를 수 없는 요소: $name"
