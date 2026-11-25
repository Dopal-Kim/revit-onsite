// 열린 문서 목록과 저장되지 않은 변경 여부 (읽기만 — 배포 전 확인용)
return document.Application.Documents.Cast<Document>()
    .Select(d => d.Title + " | 변경 " + d.IsModified + " | 링크 " + d.IsLinked + " | " + d.PathName).ToList();
