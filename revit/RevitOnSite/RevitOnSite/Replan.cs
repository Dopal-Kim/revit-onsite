// 모델 구역 목록·데이터 폴더 읽기 (강도 타임라인·웹 공유판 공통).
//   v6 (2026-11-08 사용자 확정): [일정 다시 계산]·[공정표 파일] 명령은 뺌 — 날짜는 [타설 등록]·[동바리 사양]으로 직접. 3단계: 4D 공정 계산(Plan)도 뺌 (강도 예측 타임라인)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    public static class Replan
    {
        /// <summary>모델의 구역 (요소, 구역 ID, 층 번호)</summary>
        internal static List<(Element E, string Id, int Floor)> ZoneList(Document doc) => ShoringModel.Zones(doc)
            .Select(e => (E: e, Id: e.LookupParameter("SHR_ZoneID").AsString(), Floor: e.LookupParameter("SHR_Floor") is Parameter p && p.HasValue ? p.AsInteger() : 0)).ToList();
    }
}
