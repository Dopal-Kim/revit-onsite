# 앞에 있는 Revit 창(대화상자 포함)에 키 입력. SendKeys 문법: {ENTER} {TAB} {RIGHT} ^o (Ctrl+O) 등
# 안전장치: 맨 앞 창이 Revit 프로세스가 아니면 입력하지 않음
param([string]$Keys)
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class K { [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid); }
"@
$rid = ((Get-Process -Id ((Get-NetTCPConnection -State Listen -LocalPort 8080 -ErrorAction SilentlyContinue | Select-Object -First 1).OwningProcess) -ErrorAction SilentlyContinue)).Id
$fg = [K]::GetForegroundWindow(); $owner = 0; [K]::GetWindowThreadProcessId($fg, [ref]$owner) | Out-Null
if ($owner -ne $rid) { throw "맨 앞 창이 Revit 이 아님 (프로세스 $owner) — 입력 안 함" }
[System.Windows.Forms.SendKeys]::SendWait($Keys)
"sent $Keys"
