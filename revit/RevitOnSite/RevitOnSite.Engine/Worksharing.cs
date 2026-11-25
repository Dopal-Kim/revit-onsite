// v6.2 W7 워크셰어링 (중앙 모델): 다른 사용자가 가진(체크아웃) 요소는 매개변수를 쓰지 않고 건너뛴다 — Revit 없는 순수 함수 (run_all_checks Engine.Check 가 시험).
// 애드인 ZoneEvaluation.Apply 가 WorksharingUtils.GetCheckoutStatus 로 Item 을 만들어 넘긴다. 판정 결론은 데이터 폴더 판정표·eval_log 에 그대로 남는다.
namespace RevitOnSite.Engine;

public static class Worksharing
{
    /// <summary>요소 하나: Revit 요소 ID 글자, 결과 창 이름(구역 ID 등), 다른 사용자 소유, 소유자 이름</summary>
    public sealed record Item(string ElementId, string Label, bool OwnedByOther, string Owner);

    /// <summary>→ (쓸 요소 ID, 건너뛴 요소, 결과 창 문장 — 건너뛴 게 없으면 null). 문장 = 소유자마다 개수·이름 5개까지 (많은 순 → 이름 순)</summary>
    public static (HashSet<string> Write, List<Item> Skipped, string? Message) Split(IEnumerable<Item> items)
    {
        var all = items.ToList();
        var skipped = all.Where(i => i.OwnedByOther).ToList();
        var write = all.Where(i => !i.OwnedByOther).Select(i => i.ElementId).ToHashSet();
        if (skipped.Count == 0) return (write, skipped, null);
        var by = skipped.GroupBy(i => i.Owner.Trim().Length > 0 ? i.Owner.Trim() : "(이름 없음)").OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var labels = g.Select(x => x.Label).Distinct().ToList();
                return $"{g.Key} {g.Count()}개 ({string.Join(", ", labels.Take(5))}{(labels.Count > 5 ? " …" : "")})";
            });
        return (write, skipped, $"다른 사용자가 가진 요소 {skipped.Count}개는 매개변수를 쓰지 않았습니다 — 소유: {string.Join(" · ", by)}. "
            + "판정 결론은 데이터 폴더 판정표·eval_log 에 그대로 있습니다 — 그 사용자가 동기화해 요소를 내놓은 뒤 다시 [판정].");
    }
}
