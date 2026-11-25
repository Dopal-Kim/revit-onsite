# Revit 화면에서 파일 열기: 빠른 실행 도구 '열기' 클릭 → 파일 이름 입력 → 열기. 대화상자는 바로 닫힘
# Revit 열기 창은 Autodesk 전용이라 UIA 패턴이 없음 → Win32 메시지(WM_SETTEXT, BM_CLICK)로 처리
# -NoClick: 열기 창이 이미 떠 있을 때
param([string]$Path, [int]$X = 89, [int]$Y = 24, [switch]$NoClick)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class U {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string cls, string title); }
"@
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
if (-not $NoClick) { & "$PSScriptRoot\click.ps1" -X $X -Y $Y }
$rp = (Get-Process -Id ((Get-NetTCPConnection -State Listen -LocalPort 8080 -ErrorAction SilentlyContinue | Select-Object -First 1).OwningProcess) -ErrorAction SilentlyContinue)
$main = $A::FromHandle($rp.MainWindowHandle)
$dlg = $null
for ($i = 0; $i -lt 30 -and -not $dlg; $i++) {
  Start-Sleep -Milliseconds 500
  $dlg = $main.FindAll($T::Children, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -eq "열기" } | Select-Object -First 1   # Revit 대화상자는 메인 창의 하위 창
}
if (-not $dlg) { throw "열기 창이 뜨지 않음" }
$all = $dlg.FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$combo = $all | Where-Object { $_.Current.AutomationId -eq "13006" } | Select-Object -First 1      # 파일 이름 콤보
$ok = $all | Where-Object { $_.Current.Name -eq "열기(O)" } | Select-Object -First 1
$edit = [U]::FindWindowEx([IntPtr]$combo.Current.NativeWindowHandle, [IntPtr]::Zero, "Edit", $null)
if ($edit -eq [IntPtr]::Zero) { $edit = [IntPtr]$combo.Current.NativeWindowHandle }
[U]::SendMessage($edit, 0x000C, [IntPtr]::Zero, $Path) | Out-Null                                  # WM_SETTEXT
[U]::PostMessage([IntPtr]$ok.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null   # BM_CLICK
"opened $Path"
