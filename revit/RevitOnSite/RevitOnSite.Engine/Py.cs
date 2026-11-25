using System.Globalization;

namespace RevitOnSite.Engine;

/// <summary>Python 내장 동작을 그대로 흉내 내는 도우미 (일치 시험이 글자 단위로 맞도록).</summary>
internal static class Py
{
    internal static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>evaluate.py kdate: 인쇄물 날짜 "2026. 12. 31." (행정업무규정 제7조, 월·일 앞 0 없음). ISO 가 아니면 그대로</summary>
    internal static string KDate(string? s)
    {
        s ??= "";
        var p = (s.Length > 10 ? s[..10] : s).Split('-');
        if (p.Length != 3 || p.Any(x => x.Length == 0 || !x.All(char.IsAsciiDigit))) return s;
        return $"{int.Parse(p[0], Inv)}. {int.Parse(p[1], Inv)}. {int.Parse(p[2], Inv)}.";
    }

    /// <summary>evaluate.py kstamp: 인쇄물 일시 "2026. 12. 31. 09:00" — 날짜만이면 KDate</summary>
    internal static string KStamp(string? s)
    {
        s ??= "";
        return s.Length >= 16 && (s[10] == 'T' || s[10] == ' ') ? KDate(s[..10]) + " " + s[11..16] : KDate(s);
    }

    internal static string KStamp(DateTime at) => KStamp(at.ToString("yyyy-MM-dd'T'HH:mm:ss", Inv));

    /// <summary>파일 바이트를 공유 모드로 읽음 — Excel 이 열어 둔 CSV 도 읽힘 (File.ReadAllBytes 는 FileShare.Read 라 실패, Python open() 은 됨)</summary>
    internal static byte[] ReadBytes(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>File.ReadAllText(path, enc) 와 같은 글자(BOM 감지·제거) — 공유 모드로 읽음</summary>
    internal static string ReadText(string path, System.Text.Encoding enc)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs, enc, detectEncodingFromByteOrderMarks: true);
        return sr.ReadToEnd();
    }

    /// <summary>Python 3.12+ sum(): 실수 합은 Neumaier 보정 합산. (3.11 이하 Python 은 단순 합 — 마지막 자리만 다름)</summary>
    internal static double Sum(IEnumerable<double> xs)
    {
        double f = 0.0, c = 0.0;
        foreach (var x in xs)
        {
            double t = f + x;
            c += Math.Abs(f) >= Math.Abs(x) ? (f - t) + x : (x - t) + f;
            f = t;
        }
        return c != 0 && double.IsFinite(c) ? f + c : f;
    }

    /// <summary>Python statistics.mean (실수): 정확한 유리수 합 ÷ n 을 한 번만 반올림(가장 가까운 값, 동률 짝수) — 단순 합/n 은 경계(평균 20.0 ℃ 등)에서 마지막 자리가 달라질 수 있음</summary>
    internal static double Mean(IEnumerable<double> xs)
    {
        var parts = xs.Select(x =>
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else man |= 1L << 52;
            return (M: bits < 0 ? -(System.Numerics.BigInteger)man : man, E: exp - 1075);   // x = M · 2^E (정확)
        }).ToList();
        if (parts.Count == 0) throw new InvalidOperationException("mean requires at least one data point");
        int e0 = parts.Min(p => p.E);
        System.Numerics.BigInteger num = 0;
        foreach (var (m, e) in parts) num += m << (e - e0);
        if (num.IsZero) return 0.0;
        int sign = num.Sign;
        num = System.Numerics.BigInteger.Abs(num);
        var den = new System.Numerics.BigInteger(parts.Count);
        int shift = 60 - (int)(num.GetBitLength() - den.GetBitLength());
        var q = System.Numerics.BigInteger.DivRem(shift >= 0 ? num << shift : num, shift >= 0 ? den : den << -shift, out var rem);
        int ex = e0 - shift, extra = (int)q.GetBitLength() - 53;                          // 값 = (q + rem/den′) · 2^ex
        var low = q & ((System.Numerics.BigInteger.One << extra) - 1);
        var half = System.Numerics.BigInteger.One << (extra - 1);
        q >>= extra;
        ex += extra;
        if (low > half || (low == half && (!rem.IsZero || !q.IsEven)))
        {
            q += 1;
            if (q.GetBitLength() > 53) { q >>= 1; ex++; }
        }
        return sign * Math.ScaleB((double)(long)q, ex);
    }

    /// <summary>Python round(x, n). .NET "F" 서식은 정확한 2진값 기준·동률은 짝수 쪽이라 Python 과 같다 (Math.Round 는 다름: 0.35→0.4).</summary>
    internal static double Round(double x, int n) => double.Parse(x.ToString("F" + n, Inv), NumberStyles.Float, Inv);

    /// <summary>Python f"{x:.nf}".</summary>
    internal static string F(double x, int n) => x.ToString("F" + n, Inv);

    /// <summary>Python repr(float): 최단 왕복 표기, 정수값이면 ".0".
    /// shortcut: 1e16 이상·1e-4 미만은 Python 이 지수 표기(1e+16)라 다름 — 좌표·강도 범위에선 안 나옴.</summary>
    internal static string Repr(double x)
    {
        if (double.IsNaN(x)) return "nan";
        if (double.IsInfinity(x)) return x > 0 ? "inf" : "-inf";
        var s = x.ToString("R", Inv);
        return s.Contains('.') || s.Contains('E') ? s : s + ".0";
    }

    /// <summary>Python f"{x:g}". shortcut: 지수 표기(1e+06 이상)는 .NET 이 "E" 대문자라 소문자로만 맞춤.</summary>
    internal static string G(double x) => x.ToString("G6", Inv).Replace("E", "e");

    /// <summary>Python str(date).</summary>
    internal static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);

    /// <summary>Python str(x) — 엑셀 빈 &lt;v/&gt; 값은 None → "None".</summary>
    internal static string Str(string? s) => s ?? "None";

    /// <summary>Python str(list[str]) — 오류 문구용. shortcut: 따옴표 이스케이프 안 함.</summary>
    internal static string ReprList(IEnumerable<string?> xs) => "[" + string.Join(", ", xs.Select(s => s is null ? "None" : "'" + s + "'")) + "]";

    /// <summary>utf-8-sig 읽기: BOM 제거, UTF-8 이 아니면(엑셀 기본 CSV = CP949) Python 처럼 오류.</summary>
    internal static readonly System.Text.Encoding Utf8Strict = new System.Text.UTF8Encoding(false, true);

    /// <summary>csv.writer (excel 방언) + utf-8-sig: BOM, CRLF, 쉼표·따옴표·줄바꿈이 있을 때만 따옴표.</summary>
    internal static void WriteCsv(string path, IEnumerable<string[]> recs)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var rec in recs)
            sb.Append(CsvLine(rec));
        File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true));
    }

    /// <summary>csv.writer 한 줄 (excel 방언, CRLF 포함)</summary>
    internal static string CsvLine(IEnumerable<string> rec)
    {
        static string Q(string v) => v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        return string.Join(",", rec.Select(Q)) + "\r\n";
    }

    /// <summary>Python html.escape (따옴표 포함) — 서류 HTML 공통</summary>
    internal static string Html(string? s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#x27;");

    /// <summary>Python " ".join(s.split()) — 앞뒤·겹친 공백을 한 칸으로</summary>
    internal static string Squash(string s) => string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Python i % n (음수도 0..n-1).</summary>
    internal static int Mod(int i, int n) => ((i % n) + n) % n;

    /// <summary>Python float(str).</summary>
    internal static double Float(string? s) => double.Parse(s!, NumberStyles.Float, Inv);

    /// <summary>Python int(str).</summary>
    internal static int Int(string? s) => int.Parse(s!, NumberStyles.Integer, Inv);

    /// <summary>Python next((a for a in range(1, 400) if cond(a)), None)</summary>
    internal static int? FirstDayOrNull(Func<int, bool> cond)
    {
        for (int a = 1; a < 400; a++)
            if (cond(a)) return a;
        return null;
    }
}
