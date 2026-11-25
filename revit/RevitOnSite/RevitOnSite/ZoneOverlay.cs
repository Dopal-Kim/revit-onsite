// 표시 토글 (ui_spec §2 ④ 라디오 "표시: 상태 / 강도 / 일정") — 활성 뷰의 구역·동바리에 요소별 색 재지정만 한다 (구역 값은 그대로).
//   상태 = 재지정을 지워 상태 색 필터가 다시 보이게 / 강도 = 추정 강도 ÷ fck 5단계 파랑 / 일정 = 오늘부터 예측 해체일까지 남은 날 5단계
//   요소별 재지정이 뷰 필터보다 우선하므로 검측 뷰의 상태 색 위에 덮인다. 값이 없는 구역은 회색
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Color = Autodesk.Revit.DB.Color;

namespace RevitOnSite
{
    public static class ZoneOverlay
    {
        public enum Mode { Status, Strength, Schedule }

        // [가정] 구간은 화면 표시용 (판정 기준 아님): fEst/fck < 40 · 60 · 80 · 100 % · 이상 — 짙을수록 강함. 범례(ReportViews.Legend)도 이 표를 씀
        internal static readonly (Color C, string Label)[] StrengthSteps =
            { (new(222, 235, 247), "40 % 미만"), (new(158, 202, 225), "40–60 %"), (new(107, 174, 214), "60–80 %"), (new(49, 130, 189), "80–100 %"), (new(8, 81, 156), "100 % 이상") };
        // [가정] 예측 해체일까지 ≤0 · ≤7 · ≤14 · ≤28 · 그 뒤 (일)
        internal static readonly (Color C, string Label)[] DueSteps =
            { (new(26, 152, 80), "지남·오늘"), (new(145, 207, 96), "1–7일"), (new(254, 224, 139), "8–14일"), (new(244, 109, 67), "15–28일"), (new(215, 48, 39), "29일 이후") };
        internal static readonly Color NoData = new(160, 160, 160);

        // 뷰마다 마지막으로 누른 표시 (대시보드 머리글에 보임). Revit 을 끄면 사라지고 그 뒤엔 상태로 봄
        static readonly Dictionary<string, Mode> modes = new();
        static string ViewKey(Document doc, View v) => Dashboard.Key(doc) + "|" + v.Id;
        internal static string ModeName(Document doc, View v) =>
            v == null ? "상태" : modes.GetValueOrDefault(ViewKey(doc, v)) switch { Mode.Strength => "강도", Mode.Schedule => "일정", _ => "상태" };

        /// <summary>표시 명령 하나 (트랜잭션 그룹 하나)</summary>
        internal static Result Run(ExternalCommandData data, Mode mode, string title)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var view = data.Application.ActiveUIDocument.ActiveView;
                if (!App.Write(doc, title, () => { Apply(doc, view, mode); }, out var err))
                    return App.WriteFailed(title, err);
                modes[ViewKey(doc, view)] = mode;
                Dashboard.Refresh(doc);   // 머리글의 "표시: …" 갱신
                return Result.Succeeded;   // 라디오 버튼이라 성공 알림은 띄우지 않음 (색이 바뀐 것이 결과)
            }
            catch (Exception ex)
            {
                return App.Fail(title, ex);
            }
        }

        /// <summary>열린 트랜잭션 안에서 호출. 반환 = 색을 바꾼(또는 지운) 요소 수</summary>
        public static int Apply(Document doc, View view, Mode mode)
        {
            if (!view.AreGraphicsOverridesAllowed()) throw new InvalidOperationException("이 뷰는 색을 바꿀 수 없습니다. 3D 뷰나 평면 뷰에서 다시 누르세요.");
            var zones = ShoringModel.Zones(doc).ToList();
            if (zones.Count == 0) throw new InvalidOperationException("구역이 없습니다. [구역·동바리 생성]을 먼저 누르세요.");
            double fckDefault = ProjectSettings.Read(doc).DefaultFck;
            var colorOf = new Dictionary<string, Color>();
            foreach (var z in zones)
                colorOf[z.LookupParameter("SHR_ZoneID").AsString()] = mode == Mode.Strength ? Strength(z, fckDefault) : mode == Mode.Schedule ? Due(z) : null;
            var solid = ReportViews.SolidFill(doc);
            int n = 0;
            foreach (var e in zones.Concat(ShoringModel.Shores(doc)))
            {
                var id = e.LookupParameter("SHR_ZoneID")?.AsString();
                if (string.IsNullOrEmpty(id)) continue;
                var c = mode == Mode.Status ? null : colorOf.GetValueOrDefault(id) ?? NoData;
                view.SetElementOverrides(e.Id, c == null ? new OverrideGraphicSettings() : new OverrideGraphicSettings()
                    .SetSurfaceForegroundPatternId(solid).SetSurfaceForegroundPatternColor(c)
                    .SetCutForegroundPatternId(solid).SetCutForegroundPatternColor(c));
                n++;
            }
            return n;
        }

        static Color Strength(Element z, double fckDefault)
        {
            double fEst = Num(z, "SHR_fEst"), fck = Num(z, "SHR_fck");
            if (fEst <= 0) return NoData;
            double r = fEst / (fck > 0 ? fck : fckDefault);
            return StrengthSteps[r < 0.4 ? 0 : r < 0.6 ? 1 : r < 0.8 ? 2 : r < 1.0 ? 3 : 4].C;
        }

        static Color Due(Element z)
        {
            int ymd = z.LookupParameter("SHR_DueDate") is Parameter p && p.HasValue ? p.AsInteger() : 0;
            if (ymd < 19000101 || !DateTime.TryParseExact(ymd.ToString(System.Globalization.CultureInfo.InvariantCulture), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var due)) return NoData;
            int d = (due - DateTime.Today).Days;
            return DueSteps[d <= 0 ? 0 : d <= 7 ? 1 : d <= 14 ? 2 : d <= 28 ? 3 : 4].C;
        }

        static double Num(Element e, string name) => e.LookupParameter(name) is Parameter p && p.HasValue ? p.AsDouble() : 0;
    }
}
