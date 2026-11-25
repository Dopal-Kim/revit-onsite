# make_tooltips.ps1 - ToolTipImage pictures (320x180) for the large ribbon buttons (ui_spec.md section 8).
# Windows PowerShell 5.1 + System.Drawing only. Run:  powershell -ExecutionPolicy Bypass -File design\make_tooltips.ps1
# Saved as UTF-8 with BOM: PS 5.1 reads BOM-less files as ANSI and would break the Korean labels.
# Style follows make_icons.ps1: flat fills, thin dark outlines, no shadow/gradient, light background (Revit tooltips are light).
param([string]$OutDir = (Join-Path $PSScriptRoot 'tooltips'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Hex([string]$h) { [System.Drawing.ColorTranslator]::FromHtml($h) }
function Rgb($r, $g, $b) { [System.Drawing.Color]::FromArgb($r, $g, $b) }

# ui_spec 8 light palette + ui_spec 6 status colours
$O = Hex '#3C3C3C'; $C = Hex '#BFBFBF'; $B = Hex '#2B78C5'; $Y = Hex '#F2B705'; $Gn = Hex '#3AA757'; $R = Hex '#D7302A'
$Tx = Hex '#1E1E1E'; $Sub = Hex '#5A5A5A'; $Bg = Hex '#FFFFFF'; $Wh = Hex '#FFFFFF'; $Lt = Hex '#ECECEC'; $Frame = Hex '#C8C8C8'
$St = @{ 0 = (Rgb 160 160 160); 1 = (Rgb 171 190 209); 2 = (Rgb 215 48 39); 3 = (Rgb 69 117 180); 4 = (Rgb 254 224 139)
         5 = (Rgb 145 207 96); 6 = (Rgb 26 152 80); 7 = (Rgb 115 115 115); 8 = (Rgb 128 100 162); 9 = (Rgb 244 109 67) }
$FontName = 'Malgun Gothic'

# ---------------------------------------------------------------- primitives ($script:gr = current Graphics)
function Pn($col, [double]$w = 1.5, [switch]$Dash) {
    $p = New-Object System.Drawing.Pen $col, ([float]$w)
    if ($Dash) { $p.DashPattern = [float[]]@(4, 3) }
    $p
}
function Br($col) { New-Object System.Drawing.SolidBrush $col }
function Rect($x, $y, $w, $h, $fill, $stroke, [double]$sw = 1.5) {
    if ($fill) { $b = Br $fill; $script:gr.FillRectangle($b, [float]$x, [float]$y, [float]$w, [float]$h); $b.Dispose() }
    if ($stroke) { $p = Pn $stroke $sw; $script:gr.DrawRectangle($p, [float]$x, [float]$y, [float]$w, [float]$h); $p.Dispose() }
}
function RRect($x, $y, $w, $h, [double]$r, $fill, $stroke, [double]$sw = 1.5) {
    $d = 2 * $r; $pa = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pa.AddArc([float]$x, [float]$y, [float]$d, [float]$d, 180, 90)
    $pa.AddArc([float]($x + $w - $d), [float]$y, [float]$d, [float]$d, 270, 90)
    $pa.AddArc([float]($x + $w - $d), [float]($y + $h - $d), [float]$d, [float]$d, 0, 90)
    $pa.AddArc([float]$x, [float]($y + $h - $d), [float]$d, [float]$d, 90, 90)
    $pa.CloseFigure()
    if ($fill) { $b = Br $fill; $script:gr.FillPath($b, $pa); $b.Dispose() }
    if ($stroke) { $p = Pn $stroke $sw; $script:gr.DrawPath($p, $pa); $p.Dispose() }
    $pa.Dispose()
}
function Ln($x1, $y1, $x2, $y2, $col, [double]$w = 1.5, [switch]$Dash) {
    $p = Pn $col $w -Dash:$Dash; $script:gr.DrawLine($p, [float]$x1, [float]$y1, [float]$x2, [float]$y2); $p.Dispose()
}
function Ell($cx, $cy, [double]$r, $fill, $stroke, [double]$sw = 1.5) {
    if ($fill) { $b = Br $fill; $script:gr.FillEllipse($b, [float]($cx - $r), [float]($cy - $r), [float](2 * $r), [float](2 * $r)); $b.Dispose() }
    if ($stroke) { $p = Pn $stroke $sw; $script:gr.DrawEllipse($p, [float]($cx - $r), [float]($cy - $r), [float](2 * $r), [float](2 * $r)); $p.Dispose() }
}
function PolyF([double[]]$xy, $fill, $stroke, [double]$sw = 1.5) {
    $pts = for ($i = 0; $i -lt $xy.Count; $i += 2) { New-Object System.Drawing.PointF ([float]$xy[$i]), ([float]$xy[$i + 1]) }
    $pts = [System.Drawing.PointF[]]$pts
    if ($fill) { $b = Br $fill; $script:gr.FillPolygon($b, $pts); $b.Dispose() }
    if ($stroke) { $p = Pn $stroke $sw; $p.LineJoin = 'Round'; $script:gr.DrawPolygon($p, $pts); $p.Dispose() }
}
function PLine([double[]]$xy, $col, [double]$w = 2) {
    $pts = [System.Drawing.PointF[]]$(for ($i = 0; $i -lt $xy.Count; $i += 2) { New-Object System.Drawing.PointF ([float]$xy[$i]), ([float]$xy[$i + 1]) })
    $p = Pn $col $w; $p.StartCap = 'Round'; $p.EndCap = 'Round'; $p.LineJoin = 'Round'; $script:gr.DrawLines($p, $pts); $p.Dispose()
}
# line with a filled triangular head at (x2,y2)
function Arrow($x1, $y1, $x2, $y2, $col, [double]$w = 1.5, [double]$len = 7, [double]$hw = 4) {
    $dx = $x2 - $x1; $dy = $y2 - $y1; $m = [Math]::Sqrt($dx * $dx + $dy * $dy); $ux = $dx / $m; $uy = $dy / $m
    $bx = $x2 - $ux * $len; $by = $y2 - $uy * $len
    Ln $x1 $y1 ($bx + $ux) ($by + $uy) $col $w
    PolyF @($x2, $y2, ($bx - $uy * $hw), ($by + $ux * $hw), ($bx + $uy * $hw), ($by - $ux * $hw)) $col $null
}
function NewFont([double]$size, [switch]$Bold) {
    $style = $(if ($Bold) { [System.Drawing.FontStyle]::Bold } else { [System.Drawing.FontStyle]::Regular })
    New-Object System.Drawing.Font $FontName, ([float]$size), $style, ([System.Drawing.GraphicsUnit]::Pixel)
}
# text with top at y; align l = x is left edge, c = x is centre, r = x is right edge
function T([string]$s, $x, $y, [double]$size = 12, $col = $Tx, [string]$align = 'l', [switch]$Bold) {
    $f = NewFont $size -Bold:$Bold; $b = Br $col; $sf = New-Object System.Drawing.StringFormat
    switch ($align) { 'c' { $sf.Alignment = 'Center'; $rx = $x - 200 } 'r' { $sf.Alignment = 'Far'; $rx = $x - 400 } default { $rx = $x } }
    $script:gr.DrawString($s, $f, $b, (New-Object System.Drawing.RectangleF ([float]$rx), ([float]$y), 400, 200), $sf)
    $sf.Dispose(); $b.Dispose(); $f.Dispose()
}
# text centred in a box (both directions)
function TC([string]$s, $x, $y, $w, $h, [double]$size = 12, $col = $Tx, [switch]$Bold) {
    $f = NewFont $size -Bold:$Bold; $b = Br $col; $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $script:gr.DrawString($s, $f, $b, (New-Object System.Drawing.RectangleF ([float]$x), ([float]$y), ([float]$w), ([float]$h)), $sf)
    $sf.Dispose(); $b.Dispose(); $f.Dispose()
}
# status chip: rounded fill + centred label
function Chip($x, $y, $w, $h, $fill, [string]$s, $txt = $Tx, [double]$size = 12) {
    RRect $x $y $w $h 4 $fill $O 1; TC $s $x $y $w $h $size $txt -Bold
}

# ---------------------------------------------------------------- motifs (same shapes as the 32 px icons)
function Slab($x, $y, $w, [double]$h = 7) { Rect $x $y $w $h $C $O 1.2 }
# telescopic pipe support: inner rod above, blue outer tube below, head/base plates
function Shore($cx, $top, $bot, [double]$plate = 5) {
    $j = $top + ($bot - $top) * 0.45
    Ln $cx $top $cx $j $O 1.6
    Rect ($cx - 2) $j 4 ($bot - $j) $B $O 1
    Ln ($cx - $plate) ($top + 0.8) ($cx + $plate) ($top + 0.8) $O 1.6
    Ln ($cx - $plate) ($bot - 0.8) ($cx + $plate) ($bot - 0.8) $O 1.6
}
function CheckBadge($cx, $cy, [double]$rad = 7, $fill = $Gn) {
    Ell $cx $cy $rad $fill $null
    PLine @(($cx - $rad * 0.5), ($cy + $rad * 0.02), ($cx - $rad * 0.12), ($cy + $rad * 0.42), ($cx + $rad * 0.52), ($cy - $rad * 0.38)) $Wh ([Math]::Max(1.6, $rad / 4))
}
function CrossBadge($cx, $cy, [double]$rad = 7) {
    Ell $cx $cy $rad $R $null; $a = $rad * 0.42
    Ln ($cx - $a) ($cy - $a) ($cx + $a) ($cy + $a) $Wh 2; Ln ($cx - $a) ($cy + $a) ($cx + $a) ($cy - $a) $Wh 2
}
# plan of a slab split into 3 x 2 zones A1..C2; $hi = zone label to highlight with status colour 3
function ZonePlan($x, $y, $w, $h, [string]$hi = '', [switch]$Dots) {
    Rect $x $y $w $h $Lt $null
    $cw = $w / 3; $ch = $h / 2; $cols = 'A', 'B', 'C'
    for ($c = 0; $c -lt 3; $c++) { for ($r = 0; $r -lt 2; $r++) {
        $id = $cols[$c] + ($r + 1)
        if ($id -eq $hi) { Rect ($x + $c * $cw) ($y + $r * $ch) $cw $ch $St[3] $null }
    } }
    if ($Dots) {
        for ($dx = $x + 6; $dx -lt $x + $w - 2; $dx += 11) { for ($dy = $y + 6; $dy -lt $y + $h - 2; $dy += 11) { Ell $dx $dy 1.3 $O $null } }
    }
    for ($c = 1; $c -lt 3; $c++) { Ln ($x + $c * $cw) ($y - 8) ($x + $c * $cw) ($y + $h + 4) $O 1 -Dash }
    Ln ($x - 4) ($y + $ch) ($x + $w + 4) ($y + $ch) $O 1 -Dash
    Rect $x $y $w $h $null $O 1.5
    for ($c = 0; $c -lt 3; $c++) { for ($r = 0; $r -lt 2; $r++) {
        $id = $cols[$c] + ($r + 1); $cx = $x + ($c + 0.5) * $cw; $cy = $y + ($r + 0.5) * $ch
        if ($id -eq $hi) { TC $id ($cx - 14) ($cy - 9) 28 18 12 $Wh -Bold }
        else { if ($Dots) { Rect ($cx - 12) ($cy - 8) 24 16 $Wh $null }; TC $id ($cx - 14) ($cy - 9) 28 18 12 $B -Bold }
    } }
}

# ---------------------------------------------------------------- pictures (one scriptblock each)
$Pics = [ordered]@{}

$Pics['project_setup'] = {
    # left: the settings window (ui_spec 7-1), right: what the first-pour date drives
    Rect 6 8 158 164 (Hex '#F7F7F7') $Frame 1
    T '프로젝트 설정' 14 13 12 $Tx -Bold
    $rows = @(@('첫 타설일', '2026-10-01', 38), @('타설 사이클', '7 일', 82), @('최소 존치', '3 개층', 110), @('양생 최저', '5 °C', 138))
    foreach ($row in $rows) {
        T $row[0] 14 ($row[2] + 2) 11 $Tx
        $first = ($row[0] -eq '첫 타설일')
        Rect 84 $row[2] 72 19 $Wh $(if ($first) { $B } else { $Frame }) $(if ($first) { 2 } else { 1 })
        T $row[1] 90 ($row[2] + 2) 11 $(if ($first) { $B } else { $Tx }) -Bold:$first
    }
    T '※ 착공일이 아닙니다' 14 59 11 $R -Bold
    # right: floors poured every cycle from the first-pour date
    T '층별 타설 계획' 242 8 12 $Tx 'c' -Bold
    $base = 150; $slabs = @(@('1F', '10-01', 118), @('2F', '10-08', 84), @('3F', '10-15', 50))
    Ln 214 $base 312 $base $O 2
    $below = $base
    foreach ($s in $slabs) {
        foreach ($sx in 230, 262, 294) { Shore $sx ($s[2] + 7) $below 4 }
        Slab 216 $s[2] 94
        T $s[0] 172 ($s[2] - 4) 11 $Tx -Bold
        T $s[1] 172 ($s[2] + 9) 11 $B -Bold
        $below = $s[2]
    }
    T '첫 타설일 + 사이클 × (층 − 1)' 242 158 10 $Sub 'c'
}

$Pics['generate'] = {
    # plan: grid cells become zones, shores on a regular grid
    ZonePlan 14 30 132 100 -Dots
    foreach ($gx in 14, 58, 102, 146) { Ell $gx 17 6 $Wh $O 1 }
    $n = 1; foreach ($gx in 14, 58, 102, 146) { TC "$n" ($gx - 6) 11 12 12 8 $O; $n++ }
    T '평면: 그리드 칸 = 구역' 80 136 11 $Tx 'c' -Bold
    Arrow 154 80 174 80 $B 2.5 8 5
    # section: shores at the given spacing between two slabs
    Slab 182 36 128 8
    Slab 182 116 128 8
    for ($sx = 192; $sx -le 302; $sx += 18) { Shore $sx 44 116 4 }
    Ln 192 28 210 28 $B 1; Ln 192 24 192 32 $B 1; Ln 210 24 210 32 $B 1
    T '간격' 214 20 11 $B -Bold
    T '단면: 간격대로 동바리' 246 136 11 $Tx 'c' -Bold
    T '다시 실행하면 이전 생성분을 지우고 새로 만듭니다' 160 158 11 $R 'c'
}

$Pics['install_check'] = {
    # left: one bay of pipe supports with the checked items
    Slab 30 14 134 8
    Slab 30 156 134 8
    Shore 64 22 156 6; Shore 132 22 156 6
    Ln 64 92 132 92 $O 2.5
    T '수평연결재' 98 76 11 $Tx 'c' -Bold
    Ln 16 22 16 156 $B 1; Ln 12 22 20 22 $B 1; Ln 12 156 20 156 $B 1
    T '높이' 20 112 11 $B -Bold
    # right: automatic checks and the two results
    T '자동 검사' 180 8 12 $Tx -Bold
    CheckBadge 190 40; T '이음 수 ≤ 1' 202 31 12 $Tx
    CheckBadge 190 68; T '3.5 m 초과 →' 202 59 12 $Tx; T '수평연결재' 202 75 12 $Tx
    CheckBadge 190 106; T '1본 하중 ≤ 허용' 202 97 12 $Tx
    Chip 180 128 62 24 $St[1] '합격' $Tx
    Chip 250 128 62 24 $St[2] '결함' $Wh
    T '상태 1' 211 156 10 $Sub 'c'; T '상태 2' 281 156 10 $Sub 'c'
}

$Pics['pour'] = {
    T '① 구역 고르기' 80 6 11 $Tx 'c' -Bold
    ZonePlan 14 32 132 96 'B1'
    # mouse pointer on the chosen zone
    PolyF @(88, 62, 88, 80, 92.5, 75.5, 96, 83, 98.5, 82, 95, 74.5, 101, 74.5) $Wh $O 1.2
    Arrow 150 80 166 80 $B 2.5 8 5
    # record card
    Rect 170 6 142 112 (Hex '#F7F7F7') $Frame 1
    T '② 타설 기록' 178 11 12 $Tx -Bold
    $rows = @(@('타설일', '2026-11-04', 36), @('배합', '보통', 62), @('fck', '24 MPa', 88))
    foreach ($row in $rows) { T $row[0] 178 ($row[2] + 2) 11 $Sub; Rect 222 $row[2] 82 19 $Wh $Frame 1; T $row[1] 228 ($row[2] + 2) 11 $Tx -Bold }
    Arrow 241 120 241 134 $B 2 7 4
    Chip 178 136 126 26 $St[3] '③ 양생 중 (상태 3)' $Wh 12
    T '타설 후 상태가 바뀝니다' 80 140 11 $Sub 'c'
}

$Pics['evaluate'] = {
    # left: pour floor + 2 floors below still shored = 3 floors kept (KCS 14 20 12 3.3.2(2))
    T '타설층' 97 4 10 $Sub 'c'
    $ys = 22, 62, 102, 142
    for ($i = 0; $i -lt 3; $i++) { foreach ($sx in 58, 84, 110, 136) { Shore $sx ($ys[$i] + 7) $ys[$i + 1] 4 } }
    foreach ($sy in $ys) { Slab 44 $sy 106 }
    Ln 38 26 38 142 $O 1.5; Ln 34 26 38 26 $O 1.5; Ln 34 142 38 142 $O 1.5
    T '3개층' 4 70 11 $Tx -Bold; T '존치' 8 86 11 $Tx -Bold
    T '지지층' 97 152 10 $Sub 'c'
    # middle: strength gauge, needle past the fck mark
    $cx = 192; $cy = 72; $rad = 30
    $p = Pn $Lt 7; $script:gr.DrawArc($p, [float]($cx - $rad), [float]($cy - $rad), [float](2 * $rad), [float](2 * $rad), 180, 180); $p.Dispose()
    $p = Pn $Gn 7; $script:gr.DrawArc($p, [float]($cx - $rad), [float]($cy - $rad), [float](2 * $rad), [float](2 * $rad), 180, 142); $p.Dispose()
    $a = 300 * [Math]::PI / 180
    Ln ($cx + ($rad - 6) * [Math]::Cos($a)) ($cy + ($rad - 6) * [Math]::Sin($a)) ($cx + ($rad + 6) * [Math]::Cos($a)) ($cy + ($rad + 6) * [Math]::Sin($a)) $O 2
    T 'fck' ($cx + ($rad + 8) * [Math]::Cos($a) + 2) ($cy + ($rad + 8) * [Math]::Sin($a) - 12) 11 $Tx -Bold
    $a = 316 * [Math]::PI / 180
    Ln $cx $cy ($cx + ($rad - 8) * [Math]::Cos($a)) ($cy + ($rad - 8) * [Math]::Sin($a)) $O 2
    Ell $cx $cy 3 $O $null
    T '공시체 강도' $cx 78 11 $Tx 'c' -Bold
    T '≥ fck ?' $cx 93 11 $Tx 'c'
    # right: the only two conclusions the program writes
    Arrow 225 60 238 46 $Gn 2 7 4
    Arrow 227 90 238 116 $St[3] 2 7 4
    Chip 240 22 74 42 $St[5] "해체 승인`n요청 가능" $Tx 11
    Chip 240 104 74 30 $St[3] '존치' $Wh 13
    T '충족' 242 6 11 $Gn -Bold
    T '미달' 242 88 11 $St[3] -Bold
    Rect 162 146 8 8 $St[4] $O 1
    T '공시체 없음 → 시험 필요' 174 140 11 $Sub
    T '조건: 3개층 · 지지층 14 MPa' 162 158 11 $Sub
}

$Pics['dashboard'] = {
    Rect 6 4 308 172 $Wh $Frame 1
    Rect 6 4 308 18 (Hex '#F0F0F0') $Frame 1
    T 'Revit On-Site' 12 6 11 $Tx -Bold
    T '기준일' 12 30 11 $Tx -Bold
    Rect 54 28 84 18 $Wh $Frame 1; T '2026-11-04' 60 30 11 $Tx
    RRect 144 28 40 18 3 (Hex '#F0F0F0') $O 1; TC '판정' 144 28 40 18 11 $Tx -Bold
    Chip 192 28 60 18 $St[5] '요청 가능' $Tx 10
    Chip 256 28 52 18 $St[3] '존치' $Wh 10
    # floor x zone grid, colours = ui_spec 6 (sample data pattern: 9F A2/B2 kept)
    $cols = 'A1', 'A2', 'B1', 'B2', 'C1', 'C2'; $x0 = 46; $cw = 42
    for ($i = 0; $i -lt 6; $i++) { TC $cols[$i] ($x0 + $i * $cw) 52 $cw 14 10 $Sub -Bold }
    $rows = [ordered]@{ '11F' = 3, 3, 3, 3, 3, 3; '10F' = 3, 3, 3, 3, 3, 3; '9F' = 5, 3, 5, 3, 5, 5; '8F' = 6, 6, 6, 6, 6, 6 }
    $yy = 68
    foreach ($k in $rows.Keys) {
        T $k 14 ($yy + 1) 10 $Tx -Bold
        $v = $rows[$k]
        for ($i = 0; $i -lt 6; $i++) { Rect ($x0 + $i * $cw + 1) $yy ($cw - 2) 16 $St[$v[$i]] $Wh 1 }
        $yy += 18
    }
    # timeline bar
    Ell 22 158 8 $B $null; PolyF @(19, 153, 19, 163, 27, 158) $Wh $null
    Ln 40 158 300 158 $C 3; Ln 40 158 200 158 $B 3; Ell 200 158 6 $Wh $B 2
}

$Pics['approve'] = {
    $cy = 44; $xs = 40, 120, 200, 280
    RRect 18 22 44 44 6 $St[5] $O 1; PLine @(29, 45, 37, 53, 52, 36) $O 3
    # person (supervisor) + paper with stamp
    Ell 112 34 7 $Wh $O 1.5
    $p = Pn $O 1.5; $script:gr.DrawArc($p, 98, 44, 28, 28, 180, 180); $p.Dispose()
    Rect 126 40 14 18 $Wh $O 1.2; Ell 133 52 3.5 $R $null
    RRect 178 22 44 44 6 $St[6] $O 1; TC '6' 178 22 44 44 22 $Wh -Bold
    RRect 258 22 44 44 6 $St[7] $O 1; TC '7' 258 22 44 44 22 $Wh -Bold
    Arrow 66 $cy 94 $cy $O 1.5; Arrow 146 $cy 174 $cy $O 1.5; Arrow 226 $cy 254 $cy $O 1.5
    T '요청서' 80 28 10 $Sub 'c'; T '결과' 160 28 10 $Sub 'c'
    T "해체 승인`n요청 가능" 40 72 11 $Tx 'c' -Bold
    T "감리 승인`n(사람)" 120 72 11 $Tx 'c' -Bold
    T "승인 기록`n(상태 6)" 200 72 11 $Tx 'c' -Bold
    T "해체 완료`n(상태 7)" 280 72 11 $Tx 'c' -Bold
    RRect 12 116 296 52 6 (Hex '#FFF6DA') $Y 1.5
    T '프로그램은 승인·해체 지시를 하지 않습니다' 160 122 12 $Tx 'c' -Bold
    T '감리가 승인한 결과를 기록만 합니다' 160 144 11 $Sub 'c'
}

$Pics['timeline'] = {
    # three snapshots of the same floors at three dates (ticks below line up with the slider)
    $snaps = @(@(55, '10-15', @(3, 3, 3)), @(160, '10-22', @(5, 3, 3, 3)), @(265, '10-29', @(7, 5, 3, 3, 3)))
    $ground = 116
    foreach ($s in $snaps) {
        $cx = $s[0]; $x = $cx - 34; $prev = $ground
        Ln ($x - 4) $ground ($x + 72) $ground $O 2
        for ($i = 0; $i -lt $s[2].Count; $i++) {
            $top = $ground - ($i + 1) * 18
            if ($s[2][$i] -ne 7) { foreach ($sx in ($x + 12), ($x + 34), ($x + 56)) { Ln $sx ($top + 6) $sx $prev $O 1.2 } }
            Rect $x $top 68 6 $St[$s[2][$i]] $O 1
            $prev = $top
        }
        T $s[1] $cx 120 11 $Tx 'c' -Bold
    }
    Ell 18 146 9 $B $null; PolyF @(15, 141, 15, 151, 23, 146) $Wh $null
    Ln 36 146 304 146 $C 3; Ln 36 146 265 146 $B 3
    foreach ($tk in 55, 160) { Ln $tk 141 $tk 151 $O 1.2 }
    Ell 265 146 6 $Wh $B 2
    $leg = @(@(36, 3, '양생 중'), @(118, 5, '요청 가능'), @(212, 7, '해체 완료'))
    foreach ($l in $leg) { Rect $l[0] 163 10 10 $St[$l[1]] $O 1; T $l[2] ($l[0] + 14) 160 11 $Tx }
}

# ---------------------------------------------------------------- render
New-Item -ItemType Directory -Force $OutDir | Out-Null
foreach ($name in $Pics.Keys) {
    $bmp = New-Object System.Drawing.Bitmap 320, 180, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $script:gr = [System.Drawing.Graphics]::FromImage($bmp)
    $script:gr.SmoothingMode = 'AntiAlias'
    $script:gr.TextRenderingHint = 'AntiAliasGridFit'
    $script:gr.Clear($Bg)
    & $Pics[$name]
    $script:gr.Dispose()
    $path = Join-Path $OutDir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
# self-check: every large button of ui_spec 2 has a 320x180 picture
foreach ($name in 'project_setup', 'generate', 'install_check', 'pour', 'evaluate', 'dashboard', 'approve', 'timeline') {
    $img = [System.Drawing.Image]::FromFile((Join-Path $OutDir "$name.png"))
    if ($img.Width -ne 320 -or $img.Height -ne 180) { throw "$name.png is $($img.Width)x$($img.Height), expected 320x180" }
    $img.Dispose()
}
"tooltips OK: $($Pics.Count) files in $OutDir"
