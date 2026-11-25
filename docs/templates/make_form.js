// 동바리 해체 검측요청서 A4 템플릿 생성 (주택건설공사 감리업무 세부기준 별지 제5·6·7호 준용)
const fs = require('fs');
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell, WidthType, ShadingType,
  AlignmentType, VerticalAlign, BorderStyle, Header, Footer, PageNumber, PageBreak, HeightRule,
  TableLayoutType,
} = require('docx');

const OUT = process.argv[2] || 'out.docx';
const FONT = 'Malgun Gothic';
const W = 10206;                       // A4 210mm - 좌우 여백 15mm×2 (DXA)
const LABEL = 'E7E9EE';                // 항목 칸 (회색)
const INPUT = 'EEF4FB';                // 입력 칸 (옅은 파랑)
const HINT = '8A93A3';                 // 입력 예시 글자색

const b = (c = '444444', sz = 6) => ({ style: BorderStyle.SINGLE, size: sz, color: c });
const BORDERS = { top: b(), bottom: b(), left: b(), right: b() };
const NONE = { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' };
const NOB = { top: NONE, bottom: NONE, left: NONE, right: NONE };

// runs: 문자열 또는 [{t, b, c, s, i}] 배열
function runs(x, base = {}) {
  const arr = Array.isArray(x) ? x : [{ t: x }];
  return arr.map(r => (typeof r === 'string' ? { t: r } : r)).map(r => new TextRun({
    text: r.t, bold: r.b ?? base.b, color: r.c ?? base.c, size: r.s ?? base.s ?? 18,
    italics: r.i, font: FONT,
  }));
}
function P(x, o = {}) {
  return new Paragraph({
    children: runs(x, o), alignment: o.align ?? AlignmentType.LEFT,
    spacing: { before: o.before ?? 0, after: o.after ?? 0, line: o.line ?? 240 },
    indent: o.indent, keepNext: o.keepNext,
  });
}
// 셀: lines = 문단 배열(문자열/runs/Paragraph)
function C(lines, w, o = {}) {
  const ls = Array.isArray(lines) && (lines.length === 0 || typeof lines[0] !== 'object' || lines[0] instanceof Paragraph || Array.isArray(lines[0]))
    ? lines : [lines];
  const children = (ls.length ? ls : ['']).map(l => (l instanceof Paragraph ? l : P(l, o)));
  return new TableCell({
    children, width: { size: w, type: WidthType.DXA }, columnSpan: o.span, rowSpan: o.rows,
    shading: o.fill ? { fill: o.fill, type: ShadingType.CLEAR, color: 'auto' } : undefined,
    verticalAlign: o.v ?? VerticalAlign.CENTER, borders: o.borders ?? BORDERS,
    margins: { top: 50, bottom: 50, left: 90, right: 90 },
  });
}
const L = (t, w, o = {}) => C(t, w, { fill: LABEL, b: true, align: AlignmentType.CENTER, ...o });
const I = (hint, w, o = {}) => C(hint ? [{ t: hint, c: HINT }] : '', w, { fill: INPUT, ...o });
function R(cells, h, rule = HeightRule.ATLEAST) {
  return new TableRow({ children: cells, height: h ? { value: h, rule } : undefined, cantSplit: true });
}
function T(widths, rows, o = {}) {
  const sum = widths.reduce((a, c) => a + c, 0);
  if (sum !== W && !o.free) throw new Error('width sum ' + sum + ' != ' + W);
  return new Table({ width: { size: sum, type: WidthType.DXA }, columnWidths: widths, rows, layout: TableLayoutType.FIXED });
}
const gap = (h = 80) => P('', { before: 0, after: 0, line: h });
const sect = (t, n) => P([{ t: n ? n + '  ' : '', b: true, c: '1F3A68' }, { t, b: true }], { s: 21, before: 120, after: 60, keepNext: true });
const box = (on) => (on ? '■' : '□');

// ── 문서 머리 (모든 쪽 같은 제목 띠) ──────────────────────────
function titleBlock(formNo, title, sub) {
  return [
    T([6806, 3400], [R([
      C([{ t: formNo, s: 16, c: '555555' }], 6806, { borders: NOB }),
      C([{ t: '(재)검측  □', s: 16, c: 'B00020', b: true }], 3400, { borders: NOB, align: AlignmentType.RIGHT }),
    ])]),
    T([W], [R([C([
      P([{ t: title, b: true, s: 32 }], { align: AlignmentType.CENTER, line: 300 }),
      P([{ t: sub, s: 16, c: '555555' }], { align: AlignmentType.CENTER, before: 20 }),
    ], W, { borders: { top: b('222222', 18), bottom: b('222222', 18), left: NONE, right: NONE } })], 640)]),
    gap(60),
  ];
}

// ── 1쪽: 검측요청서 + 검측결과 통보 (별지 제6호) ─────────────
const p1 = [
  ...titleBlock('[별지 제6호서식 준용] 주택건설공사 감리업무 세부기준', '동바리 해체 검측요청서',
    '거푸집·동바리 해체 — 감리 검측요청 서식 준용 (검측요청 · 결과 통보)'),
  sect('검 측 요 청 서', '①'),
  T([1300, 3800, 1300, 3806], [
    R([L('번호', 1300), I('20  .   .   .  -   호', 3800), L('받음', 1300), I('○○공사 총괄감리원 ○○○', 3806)], 420),
    R([L('공사명', 1300), I('예) ○○지구 공동주택 신축공사 ○○동', 3800), L('요청일', 1300), I('20  .   .   .', 3806)], 420),
  ]),
  P('다음과 같은 세부공종에 대하여 검측요청 하오니 검사 후 승인하여 주시기 바랍니다.', { before: 50, after: 50, align: AlignmentType.CENTER }),
  T([1800, 8406], [
    R([L('위치 및 공종', 1800), I('예) 101동 12층 슬래브 A~D 구역 / 거푸집·동바리 해체', 8406)], 400),
    R([L('검측부위', 1800), I('예) 12층 슬래브 동바리 해체 · 9~11층 존치 동바리 확인', 8406)], 400),
    R([L('검측요구일시', 1800), I('20  .   .   .  (  )   :   ', 8406)], 420),
    R([L('검측사항', 1800), C([
      ['□ 해체 기준 강도 충족 — 현장양생공시체 평균 ≥ 해체 기준 강도 (2쪽 근거표)'],
      ['□ 센서 구역 최소 추정 강도 확인 (센서 위치별)'],
      ['□ 타설층 포함 하부 3개 층 동바리 존치'],
      ['□ 해체 후 하중 재하 검토 (하중비교 · 지지층 14 MPa 이상)'],
      ['□ 해체 작업계획서 승인 · 해체 전 체크리스트 (3쪽)'],
    ], 8406, { fill: INPUT })]),
    R([L('도구 판정', 1800), C([[
      { t: '해체 승인 요청 가능  ' }, { t: '  구역', c: HINT }, { t: '   /   존치  ' }, { t: '  구역', c: HINT },
      { t: '      마감 값  ' }, { t: '________', c: HINT },
    ], [{ t: '도구는 "요청 가능 / 존치"만 표시합니다. 해체 승인은 감리 검측으로만 정합니다.', s: 15, c: '555555' }]], 8406, { fill: INPUT })], 520),
    R([L('붙임', 1800), C([
      ['□ 시공자 검측 체크리스트 (3쪽)     □ 시험성과 (현장양생공시체 압축강도 시험)'],
      ['□ 판정 근거표 · 근거 원장 (2쪽)     □ 야장 및 도면 (동바리 배치도 · 센서 위치도)'],
      ['□ 해체 작업계획서     □ 공사 참여자(기능공 포함) 실명부 (4쪽)'],
    ], 8406)]),
  ]),
  T([1800, 1600, 2603, 1600, 2603], [
    R([L('시공자', 1800, { rows: 1 }), L('점검직원', 1600), I('                              (인)', 2603), L('현장대리인', 1600), I('                              (인)', 2603)], 480),
  ]),
  gap(160),
  sect('검 측 결 과 통 보', '②'),
  T([1300, 3800, 1300, 3806], [
    R([L('번호', 1300), I('20  .   .   .  -   호', 3800), L('받음', 1300), I('○○공사 현장대리인 ○○○', 3806)], 420),
  ]),
  P([{ t: '문서번호 ' }, { t: '「            」', c: HINT }, { t: ' 로 검측요청 한 건에 대하여 20   .    .    . 검측한 결과를 다음과 같이 통보합니다.' }],
    { before: 50, after: 50, align: AlignmentType.CENTER }),
  T([1800, 8406], [
    R([L('1. 검측결과', 1800), C([
      ['□ 합격 — 해체 승인 구역 :  ', { t: '예) 12F-A, 12F-B', c: HINT }],
      ['□ 불합격 — 존치 구역 :  ', { t: '예) 12F-D (센서 구역 최소 강도 미달)', c: HINT }],
      ['□ 재검측 필요 — 재검측 예정일 :  20   .    .    .'],
    ], 8406, { fill: INPUT })]),
    R([L('2. 지시사항', 1800), I('', 8406)], 760),
    R([L('붙임', 1800), C('□ 감리원의 검측 점검표 (3쪽 감리 확인란)', 8406)], 380),
  ]),
  T([1800, 1600, 2603, 1600, 2603], [
    R([L('감리', 1800), L('검측감리원', 1600), I('                              (인)', 2603), L('총괄감리원', 1600), I('                              (인)', 2603)], 480),
    R([L('서명 일시', 1800), I('20  .   .   .     :   ', 3203 + 1000, { span: 2 }), L('감리 성명', 1600), I('', 2603)], 400),
  ], { free: true }),
  P([{ t: '※ 작성요령  ', b: true, s: 15 }, {
    t: '1. 재검측 시에는 붉은 글씨로 "(재)"를 우측 상단에 표시합니다.  2. 시공자가 재검측을 요청할 때에는 잘못 시공한 기능공의 명단을 첨부합니다.  3. 2부 작성하여 시공자, 감리원이 각 1부를 보관합니다.  4. 옅은 파란 칸이 입력란이며 회색 예시 글자는 지우고 씁니다.', s: 15,
  }], { before: 100, line: 240 }),
];

// ── 2쪽: 판정 근거표 · 존치 구역 · 감리 확인 3가지 · 현장 경고 ──
const zW = [700, 560, 900, 600, 700, 1300, 1400, 1000, 746, 2300];
const zoneHead = R([
  L('구역', zW[0]), L('층', zW[1]), L('타설일', zW[2]), L(['재령', '(일)'], zW[3]), L(['fck', '(MPa)'], zW[4]),
  L(['공시체 평균', '(MPa) / 시험일'], zW[5]), L(['센서 구역 최소', '(MPa) / 위치'], zW[6]),
  L(['존치 층수', '(타설층 포함)'], zW[7], { s: 15 }), L('근거', zW[8]), L(['도구 판정', '(요청 가능 / 존치)'], zW[9]),
].map(c => c), 560);
const zoneRow = (ex) => R(zW.map((w, i) => (ex ? I(ex[i], w, { align: AlignmentType.CENTER, s: 16 }) : I('', w))), 380);
const p2 = [
  ...titleBlock('[붙임 1] 판정 근거표 — 근거 원장 요약', '해체 요청 구역 판정 근거', '도구 계산값 · 현장양생공시체 시험 · 센서 위치별 추정 강도'),
  sect('요청 구역 근거표', '③'),
  T(zW, [
    zoneHead,
    zoneRow(['예) A', '12F', '10.20', '16', '24', '25.1 / 11.04', '24.3 / A-2', '3 ✓', '8/8', '요청 가능']),
    zoneRow(), zoneRow(), zoneRow(),
  ]),
  P([{ t: '판정 근거  ', b: true, s: 16 }, {
    s: 16, t: '거푸집 및 동바리 해체 가이드라인(국토교통부, 2023.1) 표 2.1.1 · KCS 14 20 12:2024 3.3.1·3.3.2 — 다층 슬래브 해체 기준 강도 ≥ 설계기준압축강도(필러 동바리 존치·구조계산 시 단축 가능, 최소 14 MPa), 연속 다층은 타설층 포함 최소 3개 층 존치, 강도 확인은 같은 위치에서 현장 양생한 공시체 시험(가이드라인 2.1.2(3)④).',
  }], { before: 60, line: 240 }),
  sect('존치 구역 및 사유', '④'),
  T([900, 700, 5806, 1400, 1400], [
    R([L('구역', 900), L('층', 700), L('존치 사유', 5806), L('재검측 예정일', 1400), L('비고', 1400)], 420),
    R([I('예) D', 900, { align: AlignmentType.CENTER }), I('12F', 700, { align: AlignmentType.CENTER }), I('센서 구역 최소 추정 강도 19.8 MPa < 해체 기준 24 MPa', 5806), I('11.08', 1400, { align: AlignmentType.CENTER }), I('', 1400)], 380),
    ...[0, 1].map(() => R([I('', 900), I('', 700), I('', 5806), I('', 1400), I('', 1400)], 380)),
  ]),
  sect('감리가 현장에서 확인할 3가지', '⑤'),
  T([600, 5406, 1100, 1100, 2000], [
    R([L('№', 600), L('확인 항목', 5406), L('시공자', 1100), L('감리', 1100), L('비고', 2000)], 420),
    ...[
      ['1', '근거 원장 8단계 완료 (작업계획서 · 공시체 · 검측요청 · 감리 확인 등)', '   /8'],
      ['2', '이 층 및 바로 위 3개 층 동바리 존치 상태 (타설층 포함 3개 층)', ''],
      ['3', '해체 작업계획서 승인 · 해체 전 체크리스트 (가이드라인 4.1)', ''],
    ].map(([n, t, ex]) => R([C(n, 600, { align: AlignmentType.CENTER }), C(t, 5406), I('□ 적합', 1100, { align: AlignmentType.CENTER }), I('□ 적합', 1100, { align: AlignmentType.CENTER }), I(ex, 2000)], 400)),
  ]),
  sect('이 층 · 바로 위 3개 층 현장 경고', '⑥'),
  T([1300, 8906], [
    R([L('층', 1300), L('경고 내용 (한중 · 센서 이상 · 존치 부족 · 하중 초과 등 — 도구 표시 그대로)', 8906)], 420),
    ...['이 층', '+1층', '+2층', '+3층'].map(f => R([L(f, 1300, { b: false }), I('', 8906)], 360)),
  ]),
  gap(80),
  T([1800, 3000, 5406], [
    R([L('마감 값', 1800), I('예) 3f9a1c2e', 3000, { align: AlignmentType.CENTER }),
      C([{ t: '판정 시점 근거를 고정한 값(SHA 앞 8자리) — 앱 화면 값과 같아야 합니다.', s: 15, c: '555555' }], 5406)], 400),
  ]),
];

// ── 3쪽: 검측 체크리스트 (별지 제5호 검측점검표 준용 · 가이드라인 4.1~4.3) ──
const cW = [900, 3400, 2706, 1000, 1000, 1200];
const ck = (grp, item, std, rows) => R([
  ...(grp ? [L(grp, cW[0], { rows, s: 16 })] : []),
  C(item, cW[1], { s: 16 }), C(std, cW[2], { s: 15 }),
  I('합 · 불', cW[3], { align: AlignmentType.CENTER, s: 15 }), I('합 · 불', cW[4], { align: AlignmentType.CENTER, s: 15 }), I('', cW[5]),
], 400);
const p3 = [
  ...titleBlock('[별지 제5호서식 준용] 검측 점검표', '거푸집·동바리 해체 검측 체크리스트', '거푸집 및 동바리 해체 가이드라인(2023.1) 4.1 해체 전 · 4.2 해체 중 · 4.3 해체 후'),
  T([900, 1400, 3106, 2400, 2400], [
    R([L('№', 900), L('공종 CODE', 1400), L('위치 및 부위', 3106), L('공종(세부공종)', 2400), L('공사량', 2400)], 400),
    R([I('', 900), I('', 1400), I('예) 101동 12층 슬래브 A~D', 3106), I('거푸집·동바리 해체', 2400), I('해체 면적      m²', 2400)], 420),
  ]),
  gap(100),
  T(cW, [
    R([L('구분', cW[0], { rows: 2 }), L('검사항목', cW[1], { rows: 2 }), L(['검사기준', '(시방서 · 가이드라인 · 도면)'], cW[2], { rows: 2 }), L('검사결과', cW[3] + cW[4], { span: 2 }), L('조치사항', cW[5], { rows: 2 })], 300),
    R([L('시공자', cW[3], { s: 16 }), L('감리', cW[4], { s: 16 })], 300),
    ck(['4.1', '해체 전'], '해체 작업계획서 작성 · 검토', '계획서 승인 (해체 순서 · 범위 · 일정)', 7),
    ck(null, '존치기간 준수 · 강도 발현 확인', '현장양생공시체 ≥ 해체 기준 강도 (가이드라인 표 2.1.1)'),
    ck(null, '압축강도 확인 후 해체작업 허가', '시공사 해체 허가 기록'),
    ck(null, '해체 후 하중 재하 시 필러 동바리 존치 · 재설치 구조검토', '하중비교 (가이드라인 2.1.2 계산 예)'),
    ck(null, '하부 지지층 강도', '최소 14 MPa (가이드라인 2.1.2)'),
    ck(null, '작업 준비 · 근로자 교육 · 출입 통제', '관리감독자 확인'),
    ck(null, '센서 위치별 추정 강도 (구역 최소)', '센서 구역 최소 ≥ 해체 기준 강도'),
    ck(['4.2', '해체 중'], '해체 순서 (벽 → 기둥 → 보 → 슬래브) · 추락 방지 · 보호구 · 기상', '가이드라인 4.2 체크리스트 — 관리감독자 현장 확인', 1),
    ck(['4.3', '해체 후'], '필러 · 지지용 동바리 타설층 포함 하부 3개 층 설치', 'KCS 14 20 12:2024 3.3.2(2) · 가이드라인 2.1.2(1)④', 5),
    ck(null, '각 층 같은 위치에 설치 (상하 정렬)', '층 간 편차 허용값 이내 (설치 검측)'),
    ck(null, '동바리 연직도', '설치 검측 기준'),
    ck(null, '자재 변형 · 침하', '육안 · 측정'),
    ck(null, '콘크리트 면 결함 (돌기물 등)', '가이드라인 4.3'),
    R([L('기타', cW[0]), I('', cW[1]), I('', cW[2]), I('합 · 불', cW[3], { align: AlignmentType.CENTER, s: 15 }), I('합 · 불', cW[4], { align: AlignmentType.CENTER, s: 15 }), I('', cW[5])], 400),
  ]),
  gap(100),
  T([1800, 1300, 2303, 1300, 3503], [
    R([L('시공자', 1800), L('점검일자', 1300), I('20   .    .    .', 2303), L('점검직원', 1300), I('                                   (인)', 3503)], 440),
    R([L('감리원', 1800), L('검측일자', 1300), I('20   .    .    .', 2303), L('검측감리원', 1300), I('                                   (인)', 3503)], 440),
  ]),
  P([{ t: '※ 작성요령  ', b: true, s: 15 }, {
    s: 15, t: '1. "시공자" 칸은 시공자 담당기술자가 먼저 점검하여 합격으로 확인한 뒤 검측요청서와 함께 제출합니다.  2. "감리" 칸은 감리원이 현장 확인 후 기록합니다.  3. 불합격 항목은 조치사항을 적고 보완 시공 후 재검측을 받습니다.  4. 4.2 해체 중 항목은 도구가 관리하지 않으며 관리감독자가 현장에서 확인합니다.',
  }], { before: 100, line: 240 }),
];

// ── 4쪽: 공사 참여자 실명부 (별지 제7호) ─────────────────────
const nW = [1000, 1900, 1200, 900, 1100, 1300, 1806, 1000];
const p4 = [
  ...titleBlock('[별지 제7호서식 준용] 공사 참여자(기능공 포함) 실명부', '공사 참여자 실명부', '거푸집·동바리 해체 작업 참여자'),
  T([1800, 8406], [R([L('공사명', 1800), I('예) ○○지구 공동주택 신축공사 ○○동', 8406)], 420)]),
  gap(100),
  T(nW, [
    R([L('작업일', nW[0]), L(['작업위치', '및 공종'], nW[1]), L('소속', nW[2]), L('직위', nW[3]), L('성명', nW[4]), L(['생년월일', '(성별)'], nW[5]), L('공사한 내용', nW[6]), L('서명', nW[7])], 520),
    R(nW.map((w, i) => I(['예) 11.05', '12F A 동바리 해체', '○○건설', '반장', '', '', '동바리 해체 · 반출', ''][i], w, { s: 16 })), 440),
    ...Array.from({ length: 17 }, () => R(nW.map(w => I('', w)), 440)),
  ]),
  P([{ t: '※ ', b: true, s: 15 }, { t: '실명부는 시공자가 작성하여 감리원에게 제출하고, 감리원은 확인 후 유지·관리합니다. 개인정보는 공사 관리 목적으로만 쓰고 보관 기간이 지나면 파기합니다.', s: 15 }], { before: 100, line: 240 }),
];

// ── 머리·바닥글 ─────────────────────────────────────────────
const header = new Header({ children: [P([{ t: '동바리 해체 검측요청서 (감리 검측요청 서식 준용)', s: 14, c: '777777' }], { align: AlignmentType.RIGHT })] });
const footer = new Footer({ children: [
  new Paragraph({
    alignment: AlignmentType.CENTER, spacing: { before: 0, after: 0 },
    border: { top: { style: BorderStyle.SINGLE, size: 4, color: 'AAAAAA', space: 4 } },
    children: [
      new TextRun({ text: '본 문서는 계산 도구가 만든 초안이며 해체 승인이 아닙니다. 해체 승인은 감리 검측 결과로만 정합니다.   ', size: 14, color: '555555', font: FONT }),
      new TextRun({ children: [PageNumber.CURRENT, ' / ', PageNumber.TOTAL_PAGES], size: 14, color: '555555', font: FONT }),
    ],
  }),
] });

const pb = () => new Paragraph({ pageBreakBefore: true, spacing: { before: 0, after: 0, line: 20, lineRule: 'exact' }, children: [] });
const doc = new Document({
  creator: 'build-up-2', title: '동바리 해체 검측요청서 (감리 검측요청 서식 준용)',
  styles: { default: { document: { run: { font: FONT, size: 18 } } } },
  sections: [{
    properties: { page: { size: { width: 11906, height: 16838 }, margin: { top: 850, bottom: 700, left: 850, right: 850, header: 400, footer: 360 } } },
    headers: { default: header }, footers: { default: footer },
    children: [...p1, pb(), ...p2, pb(), ...p3, pb(), ...p4],
  }],
});
Packer.toBuffer(doc).then(buf => { fs.writeFileSync(OUT, buf); console.log('wrote', OUT, buf.length); });
