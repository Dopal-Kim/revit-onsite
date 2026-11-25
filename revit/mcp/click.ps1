# Revit 창의 (X, Y) 를 실제 마우스로 클릭. 좌표 = capture.ps1 스크린샷의 픽셀 위치.
# 안전장치: Revit 을 앞으로 가져온 뒤, 그 점이 Revit 창이 아니면 클릭하지 않음. 끝나면 커서를 원래 자리로.
param([int]$X, [int]$Y, [switch]$Double)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class M {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, IntPtr e);
  public struct RECT { public int L, T, R, B; }
  public struct POINT { public int X, Y; } }
"@
[M]::SetProcessDPIAware() | Out-Null
$p = (Get-Process -Id ((Get-NetTCPConnection -State Listen -LocalPort 8080 -ErrorAction SilentlyContinue | Select-Object -First 1).OwningProcess) -ErrorAction SilentlyContinue)
$h = $p.MainWindowHandle
if ([M]::GetForegroundWindow() -ne $h) {
  [M]::keybd_event(0x12, 0, 0, [IntPtr]::Zero); [M]::keybd_event(0x12, 0, 2, [IntPtr]::Zero)   # Alt 눌렀다 떼기 → 포커스 전환 허용
  [M]::SetForegroundWindow($h) | Out-Null; Start-Sleep -Milliseconds 500
}
$r = New-Object M+RECT; [M]::GetWindowRect($h, [ref]$r) | Out-Null
$pt = New-Object M+POINT; $pt.X = $r.L + $X; $pt.Y = $r.T + $Y
$under = [M]::WindowFromPoint($pt); $owner = 0; [M]::GetWindowThreadProcessId($under, [ref]$owner) | Out-Null
if ($owner -ne $p.Id) { throw "클릭 위치가 Revit 창이 아님 (프로세스 $owner) — 클릭 안 함" }
$old = New-Object M+POINT; [M]::GetCursorPos([ref]$old) | Out-Null
[M]::SetCursorPos($pt.X, $pt.Y) | Out-Null; Start-Sleep -Milliseconds 150
$n = if ($Double) { 2 } else { 1 }
for ($i = 0; $i -lt $n; $i++) { [M]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [M]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 80 }
Start-Sleep -Milliseconds 300
[M]::SetCursorPos($old.X, $old.Y) | Out-Null
"clicked ($X, $Y) → screen ($($pt.X), $($pt.Y))"
