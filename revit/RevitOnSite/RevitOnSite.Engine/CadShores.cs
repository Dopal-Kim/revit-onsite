// v6 §2 [놓기 ▾ · 도면(CAD)에서] 의 Revit 없는 부분: 링크 DWG 에서 읽은 원·블록 표식을 (종류, 레이어, 크기)로 묶어 고르게 한다.
//   형상 읽기(ImportInstance → 원 중심·블록 중심)는 애드인 ShoreCad.cs. Python 기준본 없음 (Revit 형상 도구 — 판정 계산 아님, FloorSign 과 같음).
//   크기 = 원 지름 / 블록 평면 상자의 긴 변 (mm), SizeStepMm 단위로 묶음. 동바리 같아 보이는 묶음 = 크기 ShoreMinMm–ShoreMaxMm [가정] 처음부터 체크.
namespace RevitOnSite.Engine;

public sealed record CadMark(string Kind, string Layer, double SizeMm, double X, double Y);
public sealed record CadGroup(string Kind, string Layer, double SizeMm, List<(double X, double Y)> Points)
{
    public string Label => $"{Kind} · 레이어 {(Layer.Length > 0 ? Layer : "(없음)")} · {(Kind == CadShores.Circle ? "지름" : "크기")} {SizeMm:0} mm — {Points.Count}개";
    public bool LikelyShore => SizeMm >= CadShores.ShoreMinMm && SizeMm <= CadShores.ShoreMaxMm;
}

public static class CadShores
{
    public const string Circle = "원", Block = "블록";
    public const double SizeStepMm = 10;                     // [가정] 같은 기호로 보는 크기 단위
    public const double ShoreMinMm = 30, ShoreMaxMm = 300;   // [가정] 파이프 서포트 Φ48.6–60.5·시스템 수직재·받침판 기호 범위 (화면 체크 기본값만, 고르는 건 사람)

    /// <summary>표식 → 묶음 (많은 것부터, 같으면 종류·레이어·크기 순). 같은 묶음 안 같은 자리(1 mm 안)는 한 번만</summary>
    public static List<CadGroup> Groups(IEnumerable<CadMark> marks, double mm = 1.0 / 304.8)
    {
        var outp = new List<CadGroup>();
        foreach (var g in marks.GroupBy(m => (m.Kind, m.Layer, Size: Math.Round(m.SizeMm / SizeStepMm, MidpointRounding.AwayFromZero) * SizeStepMm)))
        {
            var pts = new List<(double X, double Y)>();
            foreach (var m in g)
                if (!pts.Any(p => Math.Abs(p.X - m.X) < mm && Math.Abs(p.Y - m.Y) < mm)) pts.Add((m.X, m.Y));
            outp.Add(new CadGroup(g.Key.Kind, g.Key.Layer, g.Key.Size, pts));
        }
        return outp.OrderByDescending(g => g.Points.Count).ThenBy(g => g.Kind, StringComparer.Ordinal).ThenBy(g => g.Layer, StringComparer.Ordinal).ThenBy(g => g.SizeMm).ToList();
    }
}
