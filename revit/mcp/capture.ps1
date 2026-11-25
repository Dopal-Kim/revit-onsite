# Revit 창(또는 Revit 의 대화상자)만 캡처 → PNG. PrintWindow: 다른 창이 위에 있어도 그 창 내용만 찍힘
# 사용: capture.ps1 -Out <출력.png> [-Window <창 제목 일부>]   (-Window 없으면 Revit 메인 창)
param([string]$Out, [string]$Window)
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public struct RECT { public int L, T, R, B; } }
"@
[W]::SetProcessDPIAware() | Out-Null
$p = (Get-Process -Id ((Get-NetTCPConnection -State Listen -LocalPort 8080 -ErrorAction SilentlyContinue | Select-Object -First 1).OwningProcess) -ErrorAction SilentlyContinue)
if (-not $p) { throw "Revit 창이 없습니다" }
$h = $p.MainWindowHandle
if ($Window) {
  $A = [System.Windows.Automation.AutomationElement]
  $cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)
  $el = $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond) | Where-Object { $_.Current.Name -like "*$Window*" } | Select-Object -First 1
  if (-not $el) { $el = $A::FromHandle($h).FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -like "*$Window*" } | Select-Object -First 1 }
  if (-not $el) { throw "창 없음: $Window" }
  $h = [IntPtr]$el.Current.NativeWindowHandle
}
$r = New-Object W+RECT; [W]::GetWindowRect($h, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc(); [W]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc)   # 2 = PW_RENDERFULLCONTENT
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"saved $Out ($($r.R - $r.L) x $($r.B - $r.T))"
