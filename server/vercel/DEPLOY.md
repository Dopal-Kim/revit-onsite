# Vercel 현장 서버 배포 (한 장)

화면 `pages\index.html`·`view.html` 은 `web\field\` 와 같은 파일 (고칠 땐 web\field 를 고치고 여기로 복사 — `python tools\test_server_sync.py` 가 다르면 실패).

올릴 폴더: `server\vercel\` 통째로 (`node_modules` 는 빼도 됨 — Vercel 이 package.json 으로 설치).

1. vercel.com → Add New → Project → 이 폴더 업로드(또는 PowerShell 에서 이 폴더로 가서 `vercel deploy --prod`). Framework = Other, 빌드 설정 비움.
2. 프로젝트 → Storage → Create → **Blob**, 접근 **Private** → 이 프로젝트에 연결 (BLOB_READ_WRITE_TOKEN 자동 생성).
3. 프로젝트 → Settings → Environment Variables (Production) — 값은 무작위 긴 글자(영문·숫자 20자 이상), 파일·채팅에 적지 않기:
   | 이름 | 쓰임 |
   |---|---|
   | FIELD_TOKEN | 휴대폰 입력 주소 `?t=` |
   | FIELD_VIEW_TOKEN | 층 보기 주소 `/view?v=` |
   | FIELD_REPORT_TOKEN | 협력업체 신고 주소 `/view?r=` |
   | FIELD_SYNC_KEY | Revit(PC) 동기화 쓰기 키 |
   | FIELD_SUPERVISOR_TOKEN | 감리 확인 주소 `/supervisor?s=` (v6.2 S3) |
   | FIELD_VAPID_PUBLIC · FIELD_VAPID_PRIVATE | 웹 푸시 키 한 쌍 (v6.2 S8) — 이 폴더에서 `node -e "…require('web-push').generateVAPIDKeys()…"` 로 만들어 화면에 내지 말고 바로 `vercel env add` 로 |
   | FIELD_VAPID_SUBJECT | 알림 보낸 쪽 연락처 `mailto:…` (비밀 아님) |
   새 화면: `/timeline?v=` (강도 타임라인, 보기 토큰) · `/supervisor?s=` (감리) — 값이 없으면 그 기능만 꺼짐(403/알림 버튼 숨김).
4. 환경 변수를 바꿨으면 Deployments → Redeploy (바꾼 값은 다음 배포부터 적용).
5. PC 사용자 환경 변수: FIELD_SERVER_URL = 배포 주소, FIELD_SYNC_KEY = 3번 값 (휴대폰 주소용 FIELD_TOKEN·FIELD_VIEW_TOKEN·FIELD_REPORT_TOKEN 도 같은 값으로).
6. 확인 (PC, build-up-2 폴더):
   `python tools\server_sync.py sync --data <데이터 폴더>` → "올림: n개 층" → 휴대폰에서 `<주소>/?t=<FIELD_TOKEN>` 열림 → 입력 1건 → 다시 sync → "받음: 저장 1건".

로컬 시험 (Vercel 없이): `python tools\test_server_sync.py` (node 필요).
