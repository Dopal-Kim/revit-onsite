// [폴더 열기] (총괄 32차: 결과물 명령마다 탐색기 창이 새로 떠 8개 쌓임) — 결과물 명령은 탐색기를 자동으로 열지 않고 결과 창 버튼으로만.
//   누르면 그 폴더를 보고 있는 탐색기 창이 이미 있으면 그 창을 앞으로, 없으면 새로 (Shell.Application 창 목록 — 실패하면 그냥 새로 엶).
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace RevitOnSite
{
    internal static class FolderOpen
    {
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        const int SwRestore = 9;

        /// <summary>결과 창 버튼 한 개: ("폴더 열기", …). select = 그 파일을 고른 채로 (새 창일 때만)</summary>
        internal static (string, Action) Button(string folder, string select = null) => ("폴더 열기", () => Open(folder, select));

        internal static void Open(string folder, string select = null)
        {
            var want = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            try
            {
                var t = Type.GetTypeFromProgID("Shell.Application");
                dynamic shell = t == null ? null : Activator.CreateInstance(t);
                if (shell != null)
                    foreach (dynamic w in shell.Windows())
                    {
                        string path = null;
                        try { path = w.Document?.Folder?.Self?.Path; } catch { }   // 인터넷 창 등은 Folder 가 없음
                        if (path == null || !string.Equals(Path.TrimEndingDirectorySeparator(path), want, StringComparison.OrdinalIgnoreCase)) continue;
                        var h = new IntPtr((long)w.HWND);
                        ShowWindow(h, SwRestore);
                        SetForegroundWindow(h);
                        Dashboard.Log("폴더 열기: 이미 열린 창을 앞으로 — " + want);
                        return;
                    }
            }
            catch (Exception ex) { Dashboard.Log("폴더 열기: 열린 창 찾기 실패 — 새로 엶 (" + ex.Message + ")"); }
            Process.Start(new ProcessStartInfo("explorer.exe", select != null && File.Exists(select) ? $"/select,\"{select}\"" : $"\"{want}\"") { UseShellExecute = true });
        }
    }
}
