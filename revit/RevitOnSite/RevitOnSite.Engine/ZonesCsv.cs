// zones.csv 의 slab_mm 열 (D1 하중비교 — 모델 슬래브 두께) 채우기. Revit 없는 부분: 애드인 SyncZonesCsv 가 모델에서 잰 두께를 넘긴다 (총괄 11-05 결정 (b)).
// 다른 칸·줄 순서·줄바꿈·BOM 은 그대로. 열이 없으면 맨 뒤에 더하고, 있으면 빈칸만 채움 (값 있는 칸은 안 건드림). evaluate.py 는 읽기만.
using System.Text;

namespace RevitOnSite.Engine;

public static class ZonesCsv
{
    public const string SlabColumn = "slab_mm";

    /// <summary>path 의 zones.csv 에 slab_mm 을 mmByZone(구역 ID → "250" 같은 mm 정수 글자, 못 찾은 구역은 빼거나 빈 값)으로 채운다.
    /// 반환 (열을 더했나, 값을 새로 넣은 구역 수). 파일이 없거나 zone_id 열이 없으면 (false, 0) 이고 파일은 그대로</summary>
    public static (bool Added, int Filled) FillSlab(string path, IReadOnlyDictionary<string, string> mmByZone, string column = SlabColumn)   // column: v6.2 W11 shore_mm 도 같은 방식
    {
        if (!File.Exists(path)) return (false, 0);
        var bytes = Py.ReadBytes(path);
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        if (text.Length == 0) return (false, 0);
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        bool endsNl = text.EndsWith('\n');
        if (endsNl) lines.RemoveAt(lines.Count - 1);
        var head = Evaluator.ParseCsv(lines[0]).FirstOrDefault() ?? new List<string>();
        int zi = head.IndexOf("zone_id");
        if (zi < 0) return (false, 0);
        int si = head.IndexOf(column);
        bool added = si < 0;
        if (added) { lines[0] += "," + column; si = head.Count; }
        int filled = 0;
        for (int i = 1; i < lines.Count; i++)
        {
            if (lines[i].Length == 0) continue;   // 빈 줄은 그대로 (csv.DictReader 도 건너뜀)
            var f = Evaluator.ParseCsv(lines[i]).FirstOrDefault() ?? new List<string>();
            string zid = zi < f.Count ? f[zi] : "";
            string mm = mmByZone.TryGetValue(zid, out var v) ? (v ?? "") : "";
            if (added)
            {
                // 모자란 칸(짧은 줄)은 빈칸으로 메워 slab_mm 이 제자리에 오게
                lines[i] += new string(',', Math.Max(0, si - f.Count)) + "," + Q(mm);
                if (mm.Length > 0) filled++;
            }
            else if (mm.Length > 0 && (si >= f.Count || f[si].Trim().Length == 0))
            {
                while (f.Count <= si) f.Add("");
                f[si] = mm;
                lines[i] = string.Join(",", f.Select(Q));   // 바뀐 줄만 다시 씀 (필요할 때만 따옴표 — Python csv 와 같은 규칙)
                filled++;
            }
        }
        if (!added && filled == 0) return (false, 0);
        var outText = string.Join(nl, lines) + (endsNl ? nl : "");
        File.WriteAllBytes(path, (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(new UTF8Encoding(false).GetBytes(outText)).ToArray());
        return (added, filled);
    }

    static string Q(string v) => v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
}
