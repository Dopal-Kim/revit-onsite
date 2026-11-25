// tools/ifc_add_evidence.py 의 C# 이식 — [IFC 내보내기] 결과 IFC 에 구역마다 Pset_RevitOnSite_Evidence(EvidenceStage·Seal·EvalAt·Warnings, 값 없는 속성은 뺌)를 덧붙인다.
// 원본 바이트는 그대로(BOM·CRLF 포함), 새 줄만 마지막 ENDSEC 앞에. # 번호 = 최대+1 부터, OwnerHistory = 첫 IFCOWNERHISTORY (없으면 $).
// GlobalId = uuid 128비트(RFC 바이트 순서, Python uuid.int) → IFC 압축 22자. 문자열 = STEP 인코딩(' → '', \ → \\, 인쇄 가능 ASCII 밖 \X2\·\X4\).
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace RevitOnSite.Engine;

public static class IfcEvidence
{
    public const string Evidence = "Pset_RevitOnSite_Evidence";
    const string ZonePset = "Pset_RevitOnSite_Zone";   // check_ifc.PSET
    static readonly (string Col, string Name)[] Fields = { ("evidence_stage", "EvidenceStage"), ("seal", "Seal"), ("eval_at", "EvalAt"), ("warnings", "Warnings") };
    const string B64 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";
    const RegexOptions MS = RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant;
    static readonly Regex Ent = new(@"#(\d+)\s*=\s*([A-Z0-9_]+)\((.*?)\);\s*$", MS);   // check_ifc.zone_psets 와 같은 식
    const string Str = @"'((?:[^']|'')*)'";

    /// <summary>ifc_guid: 128비트 → IFC 압축 GlobalId 22자 (첫 글자 2비트 + 21×6비트)</summary>
    public static string IfcGuid(UInt128 n) => B64[(int)(n >> 126)] + string.Concat(Enumerable.Range(0, 21).Select(k => B64[(int)((n >> (6 * (20 - k))) & 63)]));

    /// <summary>새 uuid 의 128비트 = Python uuid.int (RFC 바이트 순서 — Guid.ToByteArray() 그대로는 앞 세 칸이 뒤집힘)</summary>
    public static UInt128 NewId()
    {
        Span<byte> b = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(b, bigEndian: true, out _);
        return BinaryPrimitives.ReadUInt128BigEndian(b);
    }

    static readonly Encoding Utf32Be = new UTF32Encoding(bigEndian: true, byteOrderMark: false);

    /// <summary>encode: 글자 → STEP 문자열 본문. 인쇄 가능 ASCII 묶음은 \ → \\, ' → '' / 그 밖 묶음은 \X2\UTF-16BE\X0\ (BMP 밖이 섞이면 묶음 전체 \X4\UTF-32BE\X0\)</summary>
    public static string Encode(string s)
    {
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(s, "[ -~]+|[^ -~]+"))
        {
            var t = m.Value;
            if (t[0] is >= ' ' and <= '~') sb.Append(t.Replace("\\", "\\\\").Replace("'", "''"));
            else if (!t.Any(char.IsSurrogate)) sb.Append(@"\X2\").Append(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(t))).Append(@"\X0\");
            else sb.Append(@"\X4\").Append(Convert.ToHexString(Utf32Be.GetBytes(t))).Append(@"\X0\");
        }
        return sb.ToString();
    }

    /// <summary>check_ifc.decode: '' → ', \X2\…\X0\ (UTF-16BE), \X4\…\X0\ (UTF-32BE)</summary>
    public static string Decode(string s)
    {
        s = Regex.Replace(s, @"\\X2\\((?:[0-9A-Fa-f]{4})+)\\X0\\", m => Encoding.BigEndianUnicode.GetString(Convert.FromHexString(m.Groups[1].Value)));
        s = Regex.Replace(s, @"\\X4\\((?:[0-9A-Fa-f]{8})+)\\X0\\", m => Utf32Be.GetString(Convert.FromHexString(m.Groups[1].Value)));
        return s.Replace("''", "'");
    }

    /// <summary>zone_elements: {ZoneID: [요소 #id…]} — Pset_RevitOnSite_Zone 의 ZoneID → 그 세트 → 그것을 쓰는 IFCRELDEFINESBYPROPERTIES 의 대상 (파일 순서)</summary>
    public static Dictionary<string, List<string>> ZoneElements(string text)
    {
        var ent = new Dictionary<string, (string Kind, string Args)>();   // Python dict: 같은 # 이 또 나오면 값만 바뀌고 자리는 처음
        foreach (Match m in Ent.Matches(text)) ent[m.Groups[1].Value] = (m.Groups[2].Value, m.Groups[3].Value);
        var psetZone = new Dictionary<string, string>();
        foreach (var (i, (kind, args)) in ent)
        {
            if (kind != "IFCPROPERTYSET") continue;
            var names = Regex.Matches(args, Str).Select(x => x.Groups[1].Value).ToList();
            if (names.Count < 2 || Decode(names[1]) != ZonePset) continue;
            var refs = Regex.Match(args, @"\(([^()]*)\)\s*$");
            foreach (Match r in Regex.Matches(refs.Success ? refs.Groups[1].Value : "", @"#(\d+)"))
            {
                var (k, a) = ent.TryGetValue(r.Groups[1].Value, out var e) ? e : ("", "");
                var m = k == "IFCPROPERTYSINGLEVALUE" ? Regex.Match(a, @"\A\s*'ZoneID'\s*,[^,]*,\s*IFC[A-Z]+\(\s*" + Str) : Match.Empty;
                if (m.Success) psetZone[i] = Decode(m.Groups[1].Value);
            }
        }
        var outp = new Dictionary<string, List<string>>();
        foreach (var (kind, args) in ent.Values)
        {
            if (kind != "IFCRELDEFINESBYPROPERTIES") continue;
            var m = Regex.Match(args, @"\(([^()]*)\)\s*,\s*#(\d+)\s*$");
            if (!m.Success || !psetZone.TryGetValue(m.Groups[2].Value, out var zid)) continue;
            if (!outp.TryGetValue(zid, out var lst)) outp[zid] = lst = new();
            foreach (Match r in Regex.Matches(m.Groups[1].Value, @"#(\d+)"))
                if (!lst.Contains(r.Groups[1].Value)) lst.Add(r.Groups[1].Value);
        }
        return outp;
    }

    /// <summary>add_evidence: IFC + 곁 CSV(zone_id·evidence_stage·seal·eval_at·warnings) → outPath 에 근거 세트를 덧붙인 IFC (outPath = ifcPath 면 그 자리에서 바꿈).
    /// 반환 = (붙인 구역 수, CSV 에만 있는 구역). newId = GlobalId 128비트 공급 (시험용 고정 시드, 기본 NewId)
    /// shortcut: STEP 은 Latin-1 로 읽어 바이트 위치를 그대로 씀 — 문자열에 날 UTF-8 이 든 비표준 IFC 면 구역 ID 비교가 Python(UTF-8)과 다를 수 있음, Revit 내보내기는 \X2\ 로 씀</summary>
    public static (int Done, List<string> Missing) Add(string ifcPath, string csvPath, string outPath, Func<UInt128>? newId = null)
    {
        newId ??= NewId;
        var raw = Py.ReadBytes(ifcPath);
        var text = Encoding.Latin1.GetString(raw);   // 글자 위치 = 바이트 위치
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var ends = Regex.Matches(text, "^ENDSEC;", RegexOptions.Multiline);
        if (ends.Count < 2) throw new InvalidDataException($"DATA 끝 ENDSEC 없음: {ifcPath}");
        int at = ends[^1].Index;
        var ohm = Regex.Match(text, @"^#(\d+)\s*=\s*IFCOWNERHISTORY\(", RegexOptions.Multiline);
        string oh = ohm.Success ? "#" + ohm.Groups[1].Value : "$";
        long nid = Regex.Matches(text, @"^#(\d+)\s*=", RegexOptions.Multiline).Max(x => long.Parse(x.Groups[1].Value, Py.Inv)) + 1;
        var zones = ZoneElements(text);
        var recs = Evaluator.ParseCsv(Py.ReadText(csvPath, Py.Utf8Strict)).Where(r => r.Count > 0).ToList();
        var head = recs.Count > 0 ? recs[0] : new();
        string Get(List<string> r, string col) { int k = head.IndexOf(col); return k >= 0 && k < r.Count ? r[k] : ""; }
        var lines = new List<string>();
        int done = 0;
        var missing = new List<string>();
        foreach (var row in recs.Skip(1))
        {
            var zid = Get(row, "zone_id").Trim();
            if (zid.Length == 0) continue;
            if (!zones.TryGetValue(zid, out var els)) { missing.Add(zid); continue; }
            var props = new List<string>();
            foreach (var (col, name) in Fields)
            {
                var v = Get(row, col).Trim();
                if (v.Length == 0) continue;
                lines.Add($"#{nid}=IFCPROPERTYSINGLEVALUE('{name}',$,IFCTEXT('{Encode(v)}'),$);");
                props.Add($"#{nid}");
                nid++;
            }
            if (props.Count == 0) continue;
            lines.Add($"#{nid}=IFCPROPERTYSET('{IfcGuid(newId())}',{oh},'{Evidence}',$,({string.Join(",", props)}));");
            lines.Add($"#{nid + 1}=IFCRELDEFINESBYPROPERTIES('{IfcGuid(newId())}',{oh},$,$,({string.Join(",", els.Select(e => "#" + e))}),#{nid});");
            nid += 2;
            done++;
        }
        var add = Encoding.ASCII.GetBytes(string.Concat(lines.Select(l => l + nl)));   // Encode 뒤라 전부 ASCII
        var outBytes = new byte[raw.Length + add.Length];
        raw.AsSpan(0, at).CopyTo(outBytes);
        add.CopyTo(outBytes, at);
        raw.AsSpan(at).CopyTo(outBytes.AsSpan(at + add.Length));
        var tmp = outPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";   // 다 쓴 뒤 바꿔 끼움 (도중 실패해도 원본 보존)
        File.WriteAllBytes(tmp, outBytes);
        File.Move(tmp, outPath, overwrite: true);
        return (done, missing);
    }
}
