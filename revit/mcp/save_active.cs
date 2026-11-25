// 활성 문서 저장: 이 코드는 트랜잭션 안이라 Save 불가 → Revit '저장' 명령을 예약(PostCommand)
var docs = document.Application.Documents.Cast<Document>().Select(d => d.Title + " (변경 " + d.IsModified + ", 경로 " + d.PathName + ")").ToList();
new UIApplication(document.Application).PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Save));
return docs;
