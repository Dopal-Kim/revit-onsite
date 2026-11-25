# Revit 경고 창(오류 0, 경고만) 이 뜨면 [확인] — 백그라운드 문서 작업(업그레이드·가져오기) 중 막힘 방지. 오류가 있는 창은 건드리지 않음
# 사용: ok_warnings.ps1 -Until <이 파일이 생기면 끝> [-Minutes 20]
param([string]$Until, [int]$Minutes = 20)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text; using System.Collections.Generic;
public static class OW {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int InternalGetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static List<IntPtr> Find(uint pid, string title) {
    var r = new List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) { var sb = new StringBuilder(256); InternalGetWindowText(h, sb, 256); if (sb.ToString() == title) r.Add(h); } return true; }, IntPtr.Zero);
    return r; } }
"@
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
$rid = [uint32](Get-NetTCPConnection -State Listen -LocalPort 8080 | Select-Object -First 1).OwningProcess
$end = (Get-Date).AddMinutes($Minutes)
while ((Get-Date) -lt $end -and -not (Test-Path $Until)) {
  foreach ($h in [OW]::Find($rid, "Autodesk Revit 2026")) {
    $names = $A::FromHandle($h).FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
    $head = $names | Where-Object { $_ -match '\d+ 오류, \d+ 경고' } | Select-Object -First 1
    if ($head -match '(^|\s)0 오류') {
      # 감사 기록: 닫기 전에 제목·본문 전부를 dismissed_warnings.log 에 남김 (총괄 결정 3)
      $body = ($names | Where-Object { $_ -and $_ -notmatch '^(확인\(O\)|취소\(C\)|>>|<<|표시\(S\)|자세한 정보\(I\)|확장\(E\) >>|\d+/\d+)$' }) -join ' / '
      Add-Content -Path "$PSScriptRoot\dismissed_warnings.log" -Encoding UTF8 -Value ("{0}`t자동 확인`t{1}`t{2}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), ($head -replace '\s+', ' '), ($body -replace "\s+", ' '))
      $ok = $A::FromHandle($h).FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -eq '확인(O)' } | Select-Object -First 1
      [OW]::PostMessage([IntPtr]$ok.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
      "$(Get-Date -Format HH:mm:ss) 경고 창 확인: $($head -replace '\s+', ' ')"
    } elseif ($head) { "$(Get-Date -Format HH:mm:ss) 오류 창 — 그대로 둠: $($head -replace '\s+', ' ')"; exit 1 }
  }
  Start-Sleep 10
}
if (Test-Path $Until) { "끝: $Until" } else { "시간 초과" }
