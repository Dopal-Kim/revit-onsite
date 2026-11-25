namespace RevitOnSite.Engine;

/// <summary>보고서 열람 공통 스타일 (shoring/report_style.py REPORT_CSS 와 같은 글자). 문서 &lt;style&gt; 끝에 붙임 —
/// 화면 = 회색 바탕 위 A4 폭 종이 · 쪽 나눔 점선, 인쇄 = 각 문서 @page 그대로 (11-09 사용자 요청)</summary>
public static class ReportStyle
{
    public const string Css = "@media screen{body{line-height:1.5}h1,h2,h3{color:#1B2A41}th,td{border-color:#A9B8CC}th{background:#EEF3F9;color:#1B2A41}caption{caption-side:top;text-align:left;color:#4A5D75;padding:2px 0 4px}html{background:#E6EBF1}body{box-sizing:border-box;max-width:210mm;margin:24px auto;padding:14mm;background:#fff;border-radius:6px;box-shadow:0 4px 24px rgba(27,42,65,.16)}.page+.page,section+section{border-top:2px dashed #C5D2E3;margin-top:12mm;padding-top:10mm}}@media screen and (min-width:1000px){body{zoom:1.2}}@media screen and (max-width:840px){body{margin:0;padding:14px;border-radius:0;box-shadow:none}}@media print{html{background:none}body{max-width:none;margin:0;padding:0;border-radius:0;box-shadow:none}}";
}
