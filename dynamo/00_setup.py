# 00_Setup — Dynamo "Python Script" 노드에 그대로 붙여 넣는 코드 (Revit 2026 CPython3 / 2027 PythonNet3 공통)
# 입력: IN[0] = 공유 매개변수 파일 경로 (File Path 노드), IN[1] = 실행 여부 (Boolean 노드, True 일 때만 실행)
# 하는 일: 파일의 SHR_Zone·SHR_Shore·SNS 그룹 → 일반 모델(Generic Models), PRJ 그룹 → 프로젝트 정보에 인스턴스 매개변수로 바인딩.
#          이미 있는 매개변수는 건너뛰므로 여러 번 실행해도 안전.
# 출력: [추가한 이름 목록, 건너뛴 이름 목록] 또는 오류 메시지
import clr
clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager
clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import BuiltInCategory, Category, CategorySet, GroupTypeId

doc = DocumentManager.Instance.CurrentDBDocument
app = doc.Application
path, run = IN[0], IN[1]


def category_set(bic):
    cs = CategorySet()
    cs.Insert(Category.GetCategory(doc, bic))
    return cs


if not run:
    OUT = "Run 을 True 로 바꾸고 다시 실행하세요"
else:
    old_path = app.SharedParametersFilename
    app.SharedParametersFilename = str(path)
    spf = app.OpenSharedParameterFile()
    if spf is None:
        OUT = "공유 매개변수 파일을 열 수 없습니다: " + str(path)
    else:
        existing = set()
        it = doc.ParameterBindings.ForwardIterator()
        while it.MoveNext():
            existing.add(it.Key.Name)
        generic = category_set(BuiltInCategory.OST_GenericModel)
        project = category_set(BuiltInCategory.OST_ProjectInformation)
        added, skipped = [], []
        TransactionManager.Instance.EnsureInTransaction(doc)
        for group in spf.Groups:
            cats = project if group.Name == "PRJ" else generic
            for d in group.Definitions:
                if d.Name in existing:
                    skipped.append(d.Name)
                    continue
                doc.ParameterBindings.Insert(d, app.Create.NewInstanceBinding(cats), GroupTypeId.Data)
                added.append(d.Name)
        TransactionManager.Instance.TransactionTaskDone()
        if old_path:
            app.SharedParametersFilename = old_path   # 사용자 설정 원래대로
        OUT = [added, skipped]
