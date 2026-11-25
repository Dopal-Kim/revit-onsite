# HTML → PDF (Chrome 헤드리스 인쇄, 머리·바닥글 없음) — 시뮬레이션 가이드·보고서 (총괄 60차, 사용자 지시 11-08: Edge 말고 Chrome).
# 실행: powershell -ExecutionPolicy Bypass -File tools\make_pdf.ps1 docs\simulation\guide_template.html docs\simulation\guide.pdf
# 출력: "PDF: <경로> (n쪽, k KB)". 실패하면 종료 코드 1. 쪽 크기·여백은 HTML 의 @page 를 따름.
param([Parameter(Mandatory = $true)][string]$Html, [Parameter(Mandatory = $true)][string]$Pdf)
$ErrorActionPreference = 'Stop'
$chrome = @("$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe", "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $chrome) { Write-Output "Chrome(chrome.exe)을 찾지 못했습니다."; exit 1 }
$src = (Resolve-Path $Html).Path
$out = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($Pdf)) { $Pdf } else { Join-Path (Get-Location) $Pdf }))
$before = if (Test-Path $out) { (Get-Item $out).LastWriteTimeUtc } else { [datetime]::MinValue }   # 같은 이름 PDF 는 Chrome 이 덮어씀 — 실패를 이전 파일로 착각하지 않게 시각 비교
$tmpProfile = Join-Path $env:TEMP ("make_pdf_" + [guid]::NewGuid().ToString("N"))   # 켜져 있는 Chrome 과 프로필이 겹치지 않게
try {
    $chromeArgs = @('--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--no-pdf-header-footer', '--print-to-pdf-no-header',
        "--user-data-dir=`"$tmpProfile`"", "--print-to-pdf=`"$out`"", ([Uri]$src).AbsoluteUri)
    Start-Process -FilePath $chrome -ArgumentList $chromeArgs -Wait -WindowStyle Hidden
} finally {
    if (Test-Path $tmpProfile) { Remove-Item $tmpProfile -Recurse -Force -ErrorAction SilentlyContinue }   # 임시 프로필 (이 스크립트가 만든 것만)
}
if (-not (Test-Path $out) -or (Get-Item $out).LastWriteTimeUtc -le $before) { Write-Output "PDF 를 만들지 못했습니다: $out"; exit 1 }
$text = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($out))   # 바이트 그대로 글자로 (쪽 객체 세기)
$pages = ([regex]::Matches($text, '/Type\s*/Page(?![a-zA-Z])')).Count
if ($pages -eq 0) { $pages = ([regex]::Matches($text, '/Count\s+(\d+)') | ForEach-Object { [int]$_.Groups[1].Value } | Measure-Object -Maximum).Maximum }
Write-Output ("PDF: {0} ({1}쪽, {2:N0} KB)" -f $out, $pages, ((Get-Item $out).Length / 1KB))
