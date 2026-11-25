// shoring/photos.py 의 1:1 C# 이식 (F15, 총괄 11-05) — 현장 사진 1장을 데이터 폴더 사진/YYYYMMDD/ 에 원본 그대로 저장하고
// CSV photo 칸 = 상대 경로, photo_sha256 칸 = SHA-256. 검사(8 MB [가정]·JPEG/PNG 앞 바이트)·파일 이름·같은 사진 다시 씀·문구가 photos.py 와 같다.
// 메타데이터를 읽거나 고치지 않는다(바이트 그대로). 판정·결론은 안 바꿈 — 근거 기록만.
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace RevitOnSite.Engine;

/// <summary>photos.py rows 한 줄 — 서버가 저장한 사진 (When = 그 줄의 시각/날짜, Kind = 동바리확인·해체후점검·해체신고)</summary>
public sealed record PhotoRow(string ZoneId, string When, string Kind, string Path, string Sha256);

public static class Photos
{
    /// <summary>[가정] 휴대폰 사진 1장 상한 (서버·화면 같은 값)</summary>
    public const int MaxBytes = 8 * 1024 * 1024;
    public const string Dir = "사진";
    public const string ShaCol = "photo_sha256";
    public static readonly Dictionary<string, string> KindName = new() { ["shore_check"] = "동바리확인", ["after_check"] = "해체후점검", ["report"] = "해체신고", ["cylinder"] = "공시체" };
    static readonly (string File, string Kind, string TimeCol)[] Files = { ("shore_checks.csv", "shore_check", "timestamp"), (AfterCheck.File, "after_check", "date"), ("cylinders.csv", "cylinder", "test_date") };
    static readonly Regex B64 = new("^[A-Za-z0-9+/]*={0,2}$", RegexOptions.CultureInvariant);
    static readonly Regex Unsafe = new("[^0-9A-Za-z가-힣_-]", RegexOptions.CultureInvariant);
    const string BadData = "사진 자료를 읽지 못했습니다 — 사진을 다시 고르세요";

    /// <summary>photos.py decode: 'data:image/…;base64,XXXX' 또는 base64 글자 → 바이트 (공백 없는 표준 base64 만). 못 읽으면 InvalidDataException</summary>
    public static byte[] Decode(string? s)
    {
        s ??= "";
        if (s.StartsWith("data:", StringComparison.Ordinal)) s = s.IndexOf(',') is int c and >= 0 ? s[(c + 1)..] : "";
        if (s.Length == 0 || s.Length % 4 != 0 || !B64.IsMatch(s)) throw new InvalidDataException(BadData);
        try { return Convert.FromBase64String(s); }
        catch (FormatException) { throw new InvalidDataException(BadData); }
    }

    /// <summary>앞 바이트 → "jpg" | "png" | null (파일 이름·확장자는 안 믿음)</summary>
    public static string? ExtOf(byte[] d) =>
        d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF ? "jpg"
        : d.Length >= 8 && d.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ? "png" : null;

    static string Sha(byte[] d) => Convert.ToHexString(SHA256.HashData(d)).ToLowerInvariant();

    /// <summary>photos.py save: 사진 바이트 → (상대 경로 "사진/YYYYMMDD/{구역}_{종류}_{HHMMSS}_{sha8}.jpg|png", sha256). 같은 날·구역·종류에 같은 사진이 있으면 그 파일. 검사 실패 InvalidDataException</summary>
    public static (string Path, string Sha256) Save(string folder, byte[] data, string zone, string kind, DateTime at)
    {
        if (data.Length > MaxBytes)
            throw new InvalidDataException($"사진이 너무 큽니다 ({data.Length.ToString("N0", Py.Inv)}바이트) — {MaxBytes.ToString("N0", Py.Inv)}바이트(8 MB) 이하로 찍어 주세요");
        var ext = ExtOf(data) ?? throw new InvalidDataException("사진은 JPEG·PNG 만 받습니다 — 휴대폰 카메라로 찍은 사진을 고르세요");
        var sha = Sha(data);
        var ymd = at.ToString("yyyyMMdd", Py.Inv);
        var day = System.IO.Path.Combine(folder, Dir, ymd);
        Directory.CreateDirectory(day);
        var stem = Unsafe.Replace(zone, "_") + "_" + KindName[kind] + "_";
        var same = Directory.EnumerateFileSystemEntries(day).Select(f => System.IO.Path.GetFileName(f))
            .Where(n => n.StartsWith(stem, StringComparison.Ordinal) && n.EndsWith($"_{sha[..8]}.{ext}", StringComparison.Ordinal)
                        && File.Exists(System.IO.Path.Combine(day, n)) && Sha(File.ReadAllBytes(System.IO.Path.Combine(day, n))) == sha)
            .OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault();
        var name = same ?? $"{stem}{at.ToString("HHmmss", Py.Inv)}_{sha[..8]}.{ext}";
        if (same is null)
            using (var f = new FileStream(System.IO.Path.Combine(day, name), FileMode.CreateNew)) f.Write(data);   // 원본 바이트 그대로 (Python "xb")
        return ($"{Dir}/{ymd}/{name}", sha);
    }

    /// <summary>데이터 폴더 사진\ 의 파일 수·바이트 (총괄 11-05: [프로젝트 설정] 표시용 — 자동 삭제는 하지 않음, 근거 자료). 폴더가 없거나 못 읽으면 (0, 0)</summary>
    public static (int Count, long Bytes) Usage(string? folder)
    {
        try
        {
            var d = System.IO.Path.Combine(folder ?? "", Dir);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(d)) return (0, 0);
            var fs = new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
            return (fs.Count, fs.Sum(f => f.Length));
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return (0, 0); }
    }

    /// <summary>photos.py rows: CSV 의 서버 저장 사진(photo_sha256 있는 줄) → 시각 순, 같은 경로는 한 번. zones = 그 구역만, day = 그 날짜(YYYY-MM-DD)까지</summary>
    public static List<PhotoRow> Rows(string folder, ISet<string>? zones = null, string? day = null)
    {
        var outp = new List<PhotoRow>();
        var seen = new HashSet<string>();
        foreach (var (file, kind, tcol) in Files)
            foreach (var r in Evaluator.Read(folder, file))
            {
                string G(string k) => (r.GetValueOrDefault(k) ?? "").Trim();
                string sha = G(ShaCol), p = G("photo"), z = G("zone_id"), w = G(tcol);
                if (sha.Length == 0 || p.Length == 0 || seen.Contains(p) || (zones is not null && !zones.Contains(z))
                    || (!string.IsNullOrEmpty(day) && string.CompareOrdinal(w.Length > 10 ? w[..10] : w, day) > 0)) continue;
                seen.Add(p);
                outp.Add(new(z, w, KindName[G("source") == Evidence.ReportSource ? "report" : kind], p, sha));
            }
        return outp.OrderBy(x => x.When, StringComparer.Ordinal).ThenBy(x => x.Path, StringComparer.Ordinal).ToList();
    }
}
