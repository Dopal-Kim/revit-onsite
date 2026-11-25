// shoring/weather_import.py 의 1:1 C# 이식 (방향성 4판 F8) — 기상자료개방포털에서 내려받은 ASOS CSV(인증키 없이) → 데이터 폴더 weather.csv (date, avgTa, source).
// 일자료('평균기온') 또는 시간자료('기온' → 날짜 평균), CP949·UTF-8, 열은 이름 일부로 찾음 [미확인: 포털 형식이 바뀌면 열 찾기 실패 → 오류 문구]. 결측은 건너뜀.
// evaluate 는 date·avgTa 만 읽고 source 는 출처 기록 (Evaluator.WeatherLabel 이 근거 문구 이름으로 씀).
using System.Text;

namespace RevitOnSite.Engine;

public static class WeatherImport
{
    internal static string Text(string path)   // sensor_import 도 씀 (weather_import._text)
    {
        var raw = Py.ReadBytes(path);   // 받은 ASOS 파일을 Excel 로 열어 둔 채여도 읽음
        try { return Py.Utf8Strict.GetString(raw, raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF ? 3 : 0, raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF ? raw.Length - 3 : raw.Length); }
        catch (DecoderFallbackException) { }
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(raw);
        }
        catch (DecoderFallbackException) { throw new InvalidDataException($"{path}: 인코딩을 읽을 수 없음 (CP949·UTF-8 아님)"); }
    }

    static int? Col(List<string> head, string[] keys, string[]? exclude = null)
    {
        for (int i = 0; i < head.Count; i++)
            if (keys.Any(k => head[i].Contains(k, StringComparison.Ordinal)) && !(exclude ?? Array.Empty<string>()).Any(x => head[i].Contains(x, StringComparison.Ordinal)))
                return i;
        return null;
    }

    /// <summary>weather_import.py read_asos: → ({날짜: 평균 기온 소수 1자리}, 출처 문구 "기상청 ASOS 108 서울 [입력]"). 시간자료면 날짜별 평균. 오류는 InvalidDataException (Python 문구)</summary>
    public static (Dictionary<string, double> Temps, string Source) ReadAsos(string path)
    {
        var rows = Evaluator.ParseCsv(Text(path));
        if (rows.Count == 0) throw new InvalidDataException($"{path}: 빈 파일");
        var head = rows[0].Select(h => h.Trim()).ToList();
        int? it = Col(head, new[] { "일시", "date", "tm" });
        int? ta = Col(head, new[] { "평균기온" }) is not null ? Col(head, new[] { "평균기온" }, new[] { "최저", "최고", "지면" })
            : Col(head, new[] { "기온", "avgTa", "ta" }, new[] { "최저", "최고", "지면", "이슬" });
        if (it is null || ta is null) throw new InvalidDataException($"{path}: '일시'·'기온' 열을 찾지 못함 — 머리 줄: {Py.ReprList(head.Take(8))}");
        int st = Col(head, new[] { "지점명" }) ?? -1, stn = Col(head, new[] { "지점" }, new[] { "지점명" }) ?? -1;
        var acc = new Dictionary<string, List<double>>();   // 넣은 순서 = Python dict
        foreach (var r in rows.Skip(1))
        {
            if (r.Count <= Math.Max(it.Value, ta.Value) || r[ta.Value].Trim().Length == 0) continue;   // 결측(빈칸)은 건너뜀
            var d = r[it.Value].Trim();
            d = d.Length > 10 ? d[..10] : d;
            if (!acc.TryGetValue(d, out var l)) acc[d] = l = new();
            l.Add(double.Parse(r[ta.Value].Trim(), System.Globalization.NumberStyles.Float, Py.Inv));
        }
        if (acc.Count == 0) throw new InvalidDataException($"{path}: 기온 값이 있는 줄이 없음");
        var first = rows[1];
        string where = string.Join(" ", new[] { stn >= 0 ? first[stn].Trim() : "", st >= 0 ? first[st].Trim() : "" }.Where(x => x.Length > 0));
        return (acc.ToDictionary(kv => kv.Key, kv => Py.Round(Py.Sum(kv.Value) / kv.Value.Count, 1)), $"기상청 ASOS {where}".Trim() + " [입력]");
    }

    /// <summary>weather_import.py merge: weather.csv 에 합침 → (새 날짜 수, 바뀐 날짜 수). 날짜 순 정렬, 머리 date,avgTa,source, 옛 줄 source 빈칸 → "weather.csv 기록 [입력]".
    /// Python csv 와 같은 바이트 (UTF-8 BOM 없음, CRLF, 필요할 때만 따옴표)</summary>
    public static (int New, int Changed) Merge(string folder, IReadOnlyDictionary<string, double> temps, string source)
    {
        var old = new Dictionary<string, (string T, string S)>();
        foreach (var r in Evaluator.Read(folder, "weather.csv"))
        {
            var src = r.GetValueOrDefault("source");
            old[r["date"]!] = (r["avgTa"] ?? "", string.IsNullOrEmpty(src) ? "weather.csv 기록 [입력]" : src);
        }
        int n = temps.Keys.Count(d => !old.ContainsKey(d));
        int c = temps.Count(kv => old.TryGetValue(kv.Key, out var o) && Py.Float(o.T) != kv.Value);
        foreach (var (d, t) in temps) old[d] = (Py.G(t), source);
        var lines = old.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => Py.CsvLine(new[] { kv.Key, kv.Value.T, kv.Value.S }));
        File.WriteAllText(Path.Combine(folder, "weather.csv"), Py.CsvLine(new[] { "date", "avgTa", "source" }) + string.Concat(lines), new UTF8Encoding(false));
        return (n, c);
    }
}
