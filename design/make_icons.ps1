# make_icons.ps1 - Revit On-Site ribbon icons (ui_spec.md section 8).
# Windows PowerShell 5.1 + System.Drawing only. Run:  powershell -ExecutionPolicy Bypass -File design\make_icons.ps1
# Every icon is drawn from vector primitives in a 32-unit design grid (stroke centres).
# 32 px: anti-aliased, 2 px outline.  16 px: re-rendered (not downscaled) with every
# coordinate snapped so 1 px strokes land on pixel centres; icons simplify details via ($s -eq 16).
param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'icons'),
    [string]$ReviewDir = ''   # optional: also write 4x-zoomed sheets here for pixel review
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Hex([string]$h) { [System.Drawing.ColorTranslator]::FromHtml($h) }

$Themes = @{
    light = @{ O = (Hex '#3C3C3C'); C = (Hex '#BFBFBF'); P = (Hex '#FFFFFF'); W = (Hex '#FFFFFF')
               B = (Hex '#2B78C5'); Y = (Hex '#F2B705'); G = (Hex '#3AA757'); R = (Hex '#D7302A')
               Bg = (Hex '#F0F0F0'); Tx = (Hex '#1E1E1E') }
    dark  = @{ O = (Hex '#D6D6D6'); C = (Hex '#8A8A8A'); P = (Hex '#4E535A'); W = (Hex '#FFFFFF')
               B = (Hex '#2B78C5'); Y = (Hex '#F2B705'); G = (Hex '#3AA757'); R = (Hex '#D7302A')
               Bg = (Hex '#3B3F45'); Tx = (Hex '#E8E8E8') }
}

# ---------------------------------------------------------------- primitives
$script:G = $null   # current Graphics
$script:S = 32      # current pixel size

# stroke width for current size: 32 -> as given, 16 -> half, at least 1
function W([double]$w) { if ($script:S -eq 16) { [Math]::Max(1, [Math]::Floor($w / 2 + 0.5)) } else { $w } }

# design unit -> pixel, snapped so a stroke of width $w is crisp (w=0: snap to pixel edge, for fills)
function Sn([double]$d, [double]$w) {
    $v = $d * $script:S / 32.0
    if ([Math]::Abs($w - [Math]::Round($w)) -gt 0.01) { return [float]$v }
    if ([Math]::Round($w) % 2 -eq 1) { return [float]([Math]::Floor($v) + 0.5) }
    return [float][Math]::Floor($v + 0.5)
}
function Raw([double]$d) { [float]($d * $script:S / 32.0) }

function NewPen($col, [double]$w, [string]$cap = 'Square') {
    $pen = New-Object System.Drawing.Pen $col, ([float]$w)
    $pen.StartCap = $cap; $pen.EndCap = $cap
    $pen.LineJoin = $(if ($cap -eq 'Round') { 'Round' } else { 'Miter' })
    $pen
}
function Fill($col, [scriptblock]$draw) { $br = New-Object System.Drawing.SolidBrush $col; & $draw $br; $br.Dispose() }

function Ln([double]$x1, [double]$y1, [double]$x2, [double]$y2, $col, [double]$w = 2, [string]$cap = 'Square') {
    $ww = W $w; $pen = NewPen $col $ww $cap
    # 45-degree 1 px lines at 16 px: draw as crisp pixel stairs instead of grey AA
    $stair = ($script:S -eq 16 -and $ww -eq 1 -and $x1 -ne $x2 -and [Math]::Abs($x2 - $x1) -eq [Math]::Abs($y2 - $y1))
    if ($stair) { $script:G.SmoothingMode = 'None' }
    $script:G.DrawLine($pen, (Sn $x1 $ww), (Sn $y1 $ww), (Sn $x2 $ww), (Sn $y2 $ww)); $pen.Dispose()
    if ($stair) { $script:G.SmoothingMode = 'AntiAlias' }
}
# rectangle whose outline is centred on x1..x2 / y1..y2
function Rect([double]$x1, [double]$y1, [double]$x2, [double]$y2, $fill, $stroke, [double]$w = 2) {
    $ww = W $w; $a = Sn $x1 $ww; $b = Sn $y1 $ww; $c = Sn $x2 $ww; $d = Sn $y2 $ww
    if ($fill) { Fill $fill { param($br) $script:G.FillRectangle($br, $a, $b, $c - $a, $d - $b) } }
    if ($stroke) { $pen = NewPen $stroke $ww; $script:G.DrawRectangle($pen, $a, $b, $c - $a, $d - $b); $pen.Dispose() }
}
# fill-only box with edges on x1..x2 (no outline)
function Box([double]$x1, [double]$y1, [double]$x2, [double]$y2, $fill) {
    $a = Sn $x1 0; $b = Sn $y1 0; $c = Sn $x2 0; $d = Sn $y2 0
    Fill $fill { param($br) $script:G.FillRectangle($br, $a, $b, [Math]::Max(1, $c - $a), [Math]::Max(1, $d - $b)) }
}
function Ell([double]$x1, [double]$y1, [double]$x2, [double]$y2, $fill, $stroke, [double]$w = 2) {
    $ww = W $w; $a = Sn $x1 $ww; $b = Sn $y1 $ww; $c = Sn $x2 $ww; $d = Sn $y2 $ww
    if ($fill) { Fill $fill { param($br) $script:G.FillEllipse($br, $a, $b, $c - $a, $d - $b) } }
    if ($stroke) { $pen = NewPen $stroke $ww; $script:G.DrawEllipse($pen, $a, $b, $c - $a, $d - $b); $pen.Dispose() }
}
function Pts([double[]]$xy, [double]$w) {
    $list = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    for ($i = 0; $i -lt $xy.Count; $i += 2) { $list.Add((New-Object System.Drawing.PointF (Sn $xy[$i] $w), (Sn $xy[$i + 1] $w))) }
    , $list.ToArray()
}
function Poly([double[]]$xy, $fill, $stroke, [double]$w = 2) {
    $ww = $(if ($stroke) { W $w } else { 0 }); $pts = Pts $xy $ww
    if ($fill) { Fill $fill { param($br) $script:G.FillPolygon($br, $pts) } }
    if ($stroke) { $pen = NewPen $stroke $ww; $script:G.DrawPolygon($pen, $pts); $pen.Dispose() }
}
function PLine([double[]]$xy, $col, [double]$w = 2, [string]$cap = 'Round') {
    $ww = W $w; $pen = NewPen $col $ww $cap; $script:G.DrawLines($pen, (Pts $xy $ww)); $pen.Dispose()
}
# triangular arrow head with tip at (x,y) pointing along (dx,dy); len/half-width in design units
function Head([double]$x, [double]$y, [double]$dx, [double]$dy, $col, [double]$len = 6, [double]$hw = 4.5) {
    $m = [Math]::Sqrt($dx * $dx + $dy * $dy); $ux = $dx / $m; $uy = $dy / $m
    $bx = $x - $ux * $len; $by = $y - $uy * $len
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (Raw $x), (Raw $y)),
        (New-Object System.Drawing.PointF (Raw ($bx - $uy * $hw)), (Raw ($by + $ux * $hw))),
        (New-Object System.Drawing.PointF (Raw ($bx + $uy * $hw)), (Raw ($by - $ux * $hw))))
    Fill $col { param($br) $script:G.FillPolygon($br, $pts) }
}
# straight line arrow, head at (x2,y2)
function Arrow([double]$x1, [double]$y1, [double]$x2, [double]$y2, $col, [double]$w = 2, [double]$len = 6, [double]$hw = 4.5) {
    $dx = $x2 - $x1; $dy = $y2 - $y1; $m = [Math]::Sqrt($dx * $dx + $dy * $dy)
    Ln $x1 $y1 ($x2 - $dx / $m * ($len - 1)) ($y2 - $dy / $m * ($len - 1)) $col $w 'Flat'
    Head $x2 $y2 $dx $dy $col $len $hw
}
# block arrow (filled, outlined) from tail (x1,y1) to tip (x2,y2)
function BArrow([double]$x1, [double]$y1, [double]$x2, [double]$y2, [double]$sw, [double]$hw, [double]$hl, $fill, $stroke) {
    $dx = $x2 - $x1; $dy = $y2 - $y1; $m = [Math]::Sqrt($dx * $dx + $dy * $dy); $ux = $dx / $m; $uy = $dy / $m
    $nx = - $uy; $ny = $ux; $bx = $x2 - $ux * $hl; $by = $y2 - $uy * $hl
    Poly @(($x1 + $nx * $sw), ($y1 + $ny * $sw), ($bx + $nx * $sw), ($by + $ny * $sw), ($bx + $nx * $hw), ($by + $ny * $hw),
           $x2, $y2, ($bx - $nx * $hw), ($by - $ny * $hw), ($bx - $nx * $sw), ($by - $ny * $sw), ($x1 - $nx * $sw), ($y1 - $ny * $sw)) $fill $stroke
}
# arc (GDI angles: 0 = east, clockwise) with an arrow head at the end
function ArcArrow([double]$cx, [double]$cy, [double]$r, [double]$start, [double]$sweep, $col, [double]$w = 2, [double]$len = 6, [double]$hw = 4.5) {
    # head sits on the circle: tip at the end angle, base centre $len further back along the arc
    $ww = W $w; $pen = NewPen $col $ww 'Flat'; $sg = [Math]::Sign($sweep)
    $back = $len / $r * 180 / [Math]::PI * $sg
    $script:G.DrawArc($pen, (Raw ($cx - $r)), (Raw ($cy - $r)), (Raw (2 * $r)), (Raw (2 * $r)), [float]$start, [float]($sweep - $back * 0.8)); $pen.Dispose()
    $te = ($start + $sweep) * [Math]::PI / 180; $tb = ($start + $sweep - $back) * [Math]::PI / 180
    $tx = $cx + $r * [Math]::Cos($te); $ty = $cy + $r * [Math]::Sin($te)
    Head $tx $ty ($tx - ($cx + $r * [Math]::Cos($tb))) ($ty - ($cy + $r * [Math]::Sin($tb))) $col $len $hw
}
# punch a transparent halo (badge separation, as Revit does with overlapping glyphs)
function Knock([scriptblock]$shape, [double]$pad = 2) {
    $old = $script:G.CompositingMode; $script:G.CompositingMode = 'SourceCopy'
    $br = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Transparent)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::Transparent), ([float](2 * (Raw $pad)))
    $pen.LineJoin = 'Round'; $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    & $shape $path; $script:G.FillPath($br, $path); $script:G.DrawPath($pen, $path)
    $path.Dispose(); $pen.Dispose(); $br.Dispose(); $script:G.CompositingMode = $old
}
function KnockEll($x1, $y1, $x2, $y2, [double]$pad = 2) { Knock { param($pa) $pa.AddEllipse((Raw $x1), (Raw $y1), (Raw ($x2 - $x1)), (Raw ($y2 - $y1))) } $pad }
function KnockRect($x1, $y1, $x2, $y2, [double]$pad = 2) { Knock { param($pa) $pa.AddRectangle((New-Object System.Drawing.RectangleF (Raw $x1), (Raw $y1), (Raw ($x2 - $x1)), (Raw ($y2 - $y1)))) } $pad }
function KnockPoly([double[]]$xy, [double]$pad = 2) { Knock { param($pa) $pa.AddPolygon((Pts $xy 0)) } $pad }

# ---------------------------------------------------------------- shared motifs
function Slab($x1, $y1, $x2, $y2, $p) { Rect $x1 $y1 $x2 $y2 $p.C $p.O }
# telescopic steel pipe support: narrow inner tube on top, wide outer tube below, pin + collar at the
# joint, head plate (U-head) and base plate. plate = plate half width in design units (<= 3: no plates).
# 16 px keeps only inner rod + outer tube + plates.
function Post([double]$cx, [double]$top, [double]$bot, $p, [double]$plate = 5, [double]$joint = 0.45, [switch]$UHead) {
    $j = [Math]::Round(($top + ($bot - $top) * $joint) / 2) * 2
    Ln $cx $top $cx $j $p.O 3 'Flat'                          # inner tube (16 px: 2 px rod)
    Rect ($cx - 3) $j ($cx + 3) $bot $p.B $p.O                # outer tube
    if ($script:S -eq 32) {
        Ln ($cx - 4) $j ($cx + 4) $j $p.O 2                   # collar
        Ln ($cx - 3) ($j - 4) ($cx + 3) ($j - 4) $p.O 1       # pin through the inner tube
    }
    if ($plate -gt 3) {
        Ln ($cx - $plate) $bot ($cx + $plate) $bot $p.O 2
        Ln ($cx - $plate) $top ($cx + $plate) $top $p.O 2
        if ($UHead) { Ln ($cx - $plate) ($top - 3) ($cx - $plate) $top $p.O 2; Ln ($cx + $plate) ($top - 3) ($cx + $plate) $top $p.O 2 } # U-head lips (no slab above)
    }
}
# solid green "+" badge (create / add), 4 units thick so it stays 2 px at 16
function PlusBadge([double]$cx, [double]$cy, $p, [double]$a = 4, [double]$pad = 1.5) {
    KnockRect ($cx - $a) ($cy - 2) ($cx + $a) ($cy + 2) $pad; KnockRect ($cx - 2) ($cy - $a) ($cx + 2) ($cy + $a) $pad
    Box ($cx - $a) ($cy - 2) ($cx + $a) ($cy + 2) $p.G; Box ($cx - 2) ($cy - $a) ($cx + 2) ($cy + $a) $p.G
}
# sheet with folded corner
function Doc($x1, $y1, $x2, $y2, $p, [double]$fold = 6) {
    Poly @($x1, $y1, ($x2 - $fold), $y1, $x2, ($y1 + $fold), $x2, $y2, $x1, $y2) $p.P $p.O
    PLine @(($x2 - $fold), $y1, ($x2 - $fold), ($y1 + $fold), $x2, ($y1 + $fold)) $p.O 2 'Square'
}
function Cal($x1, $y1, $x2, $y2, $p, [switch]$Mark) {
    Rect $x1 $y1 $x2 $y2 $p.P $p.O
    Rect $x1 $y1 $x2 ($y1 + 6) $p.B $p.O
    Ln ($x1 + 5) ($y1 - 2) ($x1 + 5) ($y1 + 2) $p.O; Ln ($x2 - 5) ($y1 - 2) ($x2 - 5) ($y1 + 2) $p.O
    $cols = [int][Math]::Floor(($x2 - $x1 - 4) / 6); $rows = [int][Math]::Floor(($y2 - $y1 - 9) / 5)
    for ($r = 0; $r -lt $rows; $r++) { for ($c = 0; $c -lt $cols; $c++) {
        $x = $x1 + 4 + $c * 6; $y = $y1 + 10 + $r * 5
        if ($Mark -and $r -eq 1 -and $c -eq 1) { Box ($x - 2) ($y - 2) ($x + 4) ($y + 4) $p.Y } else { Box $x $y ($x + 2) ($y + 2) $p.O }
    } }
}
# two-arrow refresh ring
function Refresh($cx, $cy, $r, $col) {
    if ($script:S -eq 16) { ArcArrow $cx $cy $r 300 300 $col 2 8 6; return } # one ring reads better at 16 px
    ArcArrow $cx $cy $r 205 135 $col 2 5 3.5
    ArcArrow $cx $cy $r 25 135 $col 2 5 3.5
}
# import badge: blue block arrow pointing left, lower right
function InArrow($p) {
    KnockPoly @(29, 21, 22, 21, 22, 19, 15, 24, 22, 29, 22, 27, 29, 27) 2
    BArrow 29 24 15 24 3 5 7 $p.B $p.O
}

# ---------------------------------------------------------------- icons (one function each)
function Icon-project_setup($g, $s, $p) {
    Doc 5 3 24 29 $p
    Ln 9 11 16 11 $p.O 2; Ln 9 16 15 16 $p.O 2
    KnockEll 13 13 29 29 2
    $cx = 21; $cy = 21; $ro = 8; $ri = 5.8; $n = 8
    if ($s -eq 16) { $ro = 8.5; $ri = 6.3; $n = 6 }   # 16 px: 6 chunky teeth + a clear 2 px hole
    $xy = @(); for ($i = 0; $i -lt $n * 4; $i++) {
        $a = ($i * 2 * [Math]::PI / ($n * 4)) - [Math]::PI / ($n * 4) * 1.5
        $rr = $(if (($i % 4) -lt 2) { $ro } else { $ri }); $xy += ($cx + $rr * [Math]::Cos($a)); $xy += ($cy + $rr * [Math]::Sin($a)) }
    Poly $xy $p.B $p.O 2
    if ($s -eq 16) { Box 19 19 23 23 $p.P } else { Ell 18 18 24 24 $p.P $p.O 2 }
}
function Icon-generate($g, $s, $p) {
    Slab 3 10 29 15 $p
    foreach ($x in 6, 16, 26) { Post $x 15 28 $p 3.5 }
    if ($s -eq 16) { Ln 3 28 29 28 $p.O } # base line instead of separate plates
    PlusBadge 24 5 $p
}
function Icon-diagnose($g, $s, $p) {
    # level lines with level heads at the right, magnifier over their left half
    foreach ($y in 6, 13, 20) { Ln 3 $y 29 $y $p.B 2; Poly @(22, ($y - 5), 28, ($y - 5), 25, ($y - 1)) $p.B $null }
    KnockEll 3 10 18 25 2
    Ell 4 11 17 24 $p.P $p.O 2
    Ln 16 23 22 29 $p.O 4 'Flat'
}
function Icon-params($g, $s, $p) {
    foreach ($y in 4, 13, 22) {
        Poly @(3, ($y + 3), 7, $y, 14, $y, 14, ($y + 6), 7, ($y + 6)) $p.Y $p.O
        Ln 18 ($y + 3) 29 ($y + 3) $p.O 2
    }
}
function Icon-views($g, $s, $p) {
    Poly @(13, 4, 23, 9, 13, 14, 3, 9) $p.P $p.O
    Poly @(3, 9, 13, 14, 13, 26, 3, 21) $p.C $p.O
    Poly @(13, 14, 23, 9, 23, 21, 13, 26) $p.B $p.O
    KnockPoly @(17, 16, 29, 16, 25, 22, 25, 29, 21, 29, 21, 22) 2
    Poly @(17, 16, 29, 16, 25, 22, 25, 29, 21, 29, 21, 22) $p.Y $p.O
}
function Icon-install_check($g, $s, $p) {
    Slab 3 3 20 8 $p
    Post 11 8 28 $p 5
    PLine @(18, 20, 22, 25, 29, 13) $p.G 4
}
function Icon-check_height($g, $s, $p) {
    Slab 3 3 19 8 $p
    Post 11 8 28 $p 5
    Ln 21 8 29 8 $p.B 2; Ln 21 28 29 28 $p.B 2
    Ln 25 12 25 24 $p.B 2
    Head 25 9 0 -1 $p.B 5 4; Head 25 27 0 1 $p.B 5 4
}
function Icon-check_brace($g, $s, $p) {
    Ln 8 9 24 25 $p.O 2; Ln 8 25 24 9 $p.O 2
    Post 8 5 29 $p 4 -UHead; Post 24 5 29 $p 4 -UHead
}
function Icon-check_load($g, $s, $p) {
    BArrow 16 2 16 12 3 7 6 $p.Y $p.O
    Post 16 14 28 $p 6
}
function Icon-pour($g, $s, $p) {
    # crane bucket on a bail, concrete drops falling onto the slab
    PLine @(10, 8, 16, 3, 22, 8) $p.O 2 'Square'
    Poly @(8, 8, 24, 8, 24, 11, 19, 16, 13, 16, 8, 11) $p.Y $p.O
    Rect 15 19 17 21 $p.C $p.O
    Slab 3 24 29 29 $p
}
function Icon-import_schedule($g, $s, $p) {
    Ln 3 3 3 29 $p.O 2
    Rect 6 3 16 7 $p.B $p.O
    Rect 11 10 25 14 $p.B $p.O
    Rect 6 17 12 21 $p.B $p.O
    InArrow $p
}
function Icon-import_sensor($g, $s, $p) {
    Rect 7 3 13 20 $p.P $p.O
    Ell 4 17 16 29 $p.R $p.O
    Box 9 9 11 19 $p.R
    InArrow $p
}
function Icon-field_input($g, $s, $p) {
    # phone (left) sending a measured value to a sensor dot on the slab (right)
    Rect 4 3 14 27 $p.P $p.O
    Box 6 6 12 20 $p.B
    Ln 8 23 10 23 $p.O 2
    Arrow 16 10 26 10 $p.G
    Ell 20 15 26 21 $p.R $p.O
    Slab 16 22 29 28 $p
}
function Icon-same_floor($g, $s, $p) {
    # three storeys: the middle one kept by the section box and highlighted, the others faint
    Rect 5 3 27 7 $null $p.C
    Rect 3 13 29 19 $p.B $p.O
    Rect 5 25 27 29 $null $p.C
    Ln 1 10 31 10 $p.R 1
    Ln 1 22 31 22 $p.R 1
}
function Icon-import_cylinder($g, $s, $p) {
    $x1 = 4; $x2 = 18; $y1 = 3; $y2 = 26; $e = 5
    Ell $x1 ($y2 - $e) $x2 $y2 $p.C $p.O
    Box ($x1 + 1) ($y1 + $e / 2) ($x2 - 1) ($y2 - $e / 2) $p.C
    Ln $x1 ($y1 + $e / 2) $x1 ($y2 - $e / 2) $p.O; Ln $x2 ($y1 + $e / 2) $x2 ($y2 - $e / 2) $p.O
    Ell $x1 $y1 $x2 ($y1 + $e) $p.P $p.O
    InArrow $p
}
function Icon-import_weather($g, $s, $p) {
    Ell 14 3 26 15 $p.Y $p.O
    # cloud = union of lobes: stroke every lobe wide first, then fill all on top
    $lobes = @(@(3, 10, 12, 19), @(7, 5, 18, 16), @(13, 8, 22, 17))
    foreach ($l in $lobes) { Ell $l[0] $l[1] $l[2] $l[3] $null $p.O 4 }
    Rect 7 13 18 19 $null $p.O 4
    foreach ($l in $lobes) { Ell $l[0] $l[1] $l[2] $l[3] $p.P $null }
    Rect 7 13 18 19 $p.P $null
    InArrow $p
}
function Icon-replan($g, $s, $p) {
    Cal 3 9 21 28 $p
    KnockEll 15 2 29 16 2
    Refresh 22 9 6 $p.B
}
function Icon-plan_cost($g, $s, $p) {
    # shores needed over time: two stepped curves (season-aware blue above, fixed-3-floor red below) on axes
    Ln 4 4 4 28 $p.O 2
    Ln 4 28 29 28 $p.O 2
    Ln 7 23 12 23 $p.B 2; Ln 12 23 12 13 $p.B 2; Ln 12 13 20 13 $p.B 2; Ln 20 13 20 7 $p.B 2; Ln 20 7 28 7 $p.B 2
    Ln 7 25 14 25 $p.R 2; Ln 14 25 14 18 $p.R 2; Ln 14 18 28 18 $p.R 2
}
function Icon-evaluate($g, $s, $p) {
    Slab 3 5 22 10 $p
    Post 7 10 27 $p 3.5; Post 16 10 27 $p 3.5
    $sh = @(16, 15, 22.5, 13, 29, 15, 29, 21, 22.5, 29, 16, 21)
    KnockPoly $sh 2
    Poly $sh $p.G $p.O
    PLine @(19.5, 20.5, 22, 23, 26, 18) $p.W 2
}
function Icon-dashboard($g, $s, $p) {
    Rect 3 4 29 28 $p.P $p.O
    Rect 3 4 29 9 $p.O $p.O
    $cols = @(6, 14, 22); $rows = @(12, 20)
    $colors = @($p.G, $p.G, $p.Y, $p.B, $p.R, $p.G)
    $i = 0; foreach ($y in $rows) { foreach ($x in $cols) { Box $x $y ($x + 5) ($y + 5) $colors[$i]; $i++ } }
}
function Icon-show_status($g, $s, $p) {
    Rect 4 4 14 14 $p.G $p.O; Rect 18 4 28 14 $p.Y $p.O
    Rect 4 18 14 28 $p.R $p.O; Rect 18 18 28 28 $p.B $p.O
}
function Icon-show_strength($g, $s, $p) {
    $cx = 16; $cy = 21; $r = 11; $ww = W 5
    foreach ($seg in @(@(180, 60, $p.R), @(240, 60, $p.Y), @(300, 60, $p.G))) {
        $pen = NewPen $seg[2] $ww 'Flat'
        $g.DrawArc($pen, (Raw ($cx - $r)), (Raw ($cy - $r)), (Raw (2 * $r)), (Raw (2 * $r)), [float]$seg[0], [float]$seg[1]); $pen.Dispose()
    }
    PLine @($cx, $cy, 23, 13) $p.O 2
    Ell 13 18 19 24 $p.O $null
}
function Icon-show_schedule($g, $s, $p) { Cal 4 5 28 28 $p -Mark }
function Icon-reason($g, $s, $p) {
    Slab 3 22 29 27 $p
    KnockEll 6 1 26 21 2
    Ell 6 1 26 21 $p.B $p.O
    $ix = $(if ($s -eq 16) { 16 } else { 15 })
    Box $ix 5 ($ix + 2) 7 $p.W; Box $ix 9 ($ix + 2) 17 $p.W
}
function Icon-legend($g, $s, $p) {
    Rect 3 3 29 29 $p.P $p.O
    $i = 0; foreach ($c in @($p.G, $p.Y, $p.R)) {
        $y = 7 + $i * 7; Rect 7 $y 11 ($y + 4) $c $p.O; Ln 15 ($y + 2) 25 ($y + 2) $p.O 2; $i++ }
}
function Icon-criteria($g, $s, $p) {
    Doc 3 3 20 29 $p 5
    Ln 7 12 15 12 $p.O 2; Ln 7 17 15 17 $p.O 2; Ln 7 22 12 22 $p.O 2
    KnockRect 22 3 29 29 2
    Rect 22 3 29 29 $p.Y $p.O
    foreach ($y in 7, 11, 15, 19, 23) { Ln 22 $y ($(if ($y % 8 -eq 7) { 26 } else { 24 })) $y $p.O 2 'Flat' }
}
function Icon-approve($g, $s, $p) {
    Ell 11 3 21 11 $p.B $p.O
    Rect 14 11 18 16 $p.B $p.O
    Rect 6 16 26 22 $p.B $p.O
    Rect 6 22 26 25 $p.R $p.O
    Ln 4 29 28 29 $p.R 2
}
function Icon-evidence($g, $s, $p) {
    # ledger page: step boxes down the left (first three filled = stages on file), entry lines to the right
    Doc 5 3 27 29 $p
    $i = 0
    foreach ($y in 9, 14, 19, 24) {
        if ($i -lt 3) { Rect 9 $y 13 ($y + 3) $p.B $p.O 1 } else { Rect 9 $y 13 ($y + 3) $null $p.O 1 }
        Ln 16 ($y + 1.5) 23 ($y + 1.5) $p.O 1; $i++
    }
}
function Icon-timeline($g, $s, $p) {
    # time axis + playhead over two bars, play badge
    Rect 3 7 13 11 $p.B $p.O
    Rect 7 14 19 18 $p.B $p.O
    Ln 3 24 17 24 $p.O 2
    foreach ($x in 7, 13) { Ln $x 22 $x 24 $p.O 2 }
    Ln 10 5 10 27 $p.R 2 'Flat'
    Poly @(6, 2, 14, 2, 10, 6) $p.R $null
    KnockEll 17 16 29 28 2
    Ell 17 16 29 28 $p.G $p.O
    Poly @(21, 19, 21, 25, 26, 22) $p.W $null
}
function Icon-report($g, $s, $p) {
    Doc 5 3 27 29 $p
    Rect 9 11 23 25 $null $p.O 1
    Box 9 11 23 14 $p.B
    Ln 9 18 23 18 $p.O 1; Ln 9 21 23 21 $p.O 1
    Ln 14 14 14 25 $p.O 1
}
function Icon-floor_sign($g, $s, $p) {
    # sign plate on a post: status band (red) over a big level mark and two table lines
    Ln 16 22 16 30 $p.O 2
    Rect 4 3 28 22 $p.P $p.O
    Box 7 6 25 10 $p.R
    Ln 8 14 24 14 $p.O 1; Ln 8 18 24 18 $p.O 1
}
function Icon-slab_record($g, $s, $p) {
    # record page: slab section (grey band) on top, two record lines, green record mark bottom-left
    Doc 5 3 27 29 $p
    Slab 9 9 21 13 $p
    Ln 9 18 23 18 $p.O 1; Ln 9 21 23 21 $p.O 1
    Box 9 24 13 27 $p.G
}
function Icon-quality_ledger($g, $s, $p) {
    # ledger page: 3-row table, result column (green, green, red) — 28-day quality record
    Doc 5 3 27 29 $p
    foreach ($y in 10, 16, 22) { Ln 9 ($y + 1.5) 17 ($y + 1.5) $p.O 1 }
    Box 19 10 23 13 $p.G; Box 19 16 23 19 $p.G; Box 19 22 23 25 $p.R
}
function Icon-package($g, $s, $p) {
    # bundle box: blue lid band, tie lines on top, two label lines — one folder handed over to the supervisor
    Rect 5 11 27 28 $p.P $p.O
    Box 6 12 26 15 $p.B
    Ln 16 4 16 11 $p.O 2; Ln 10 7 16 4 $p.O 2; Ln 22 7 16 4 $p.O 2
    Ln 10 20 22 20 $p.O 1; Ln 10 24 18 24 $p.O 1
}
function Icon-save_views($g, $s, $p) {
    Rect 9 3 29 17 $p.P $p.O
    Rect 3 7 23 21 $p.P $p.O
    Box 4 8 23 10 $p.B
    KnockRect 15 18 29 29 2
    Rect 18 16 22 18 $p.O $p.O
    Rect 15 18 29 28 $p.O $p.O
    Ell 19 20 25 26 $p.B $p.P
}
function Icon-export_web($g, $s, $p) {
    Ell 3 7 25 29 $p.B $p.O
    if ($s -ne 16) { Ell 10 7 18 29 $null $p.W 1 }   # 16 px: one meridian + one parallel only
    Ln 4 18 24 18 $p.W 1; Ln 14 8 14 28 $p.W 1
    KnockPoly @(17, 14, 25, 6, 22, 3, 29, 3, 29, 10, 26, 7, 18, 15) 2
    BArrow 17 15 28 4 2.5 6 7 $p.G $p.O
}
function Icon-export_ifc($g, $s, $p) {
    # IFC model box (3 faces) with the same outward arrow as export_web — [IFC 내보내기] (D3)
    Poly @(3, 11, 13, 6, 23, 11, 23, 25, 13, 30, 3, 25) $p.P $p.O
    Ln 3 11 13 16 $p.O 1; Ln 13 16 23 11 $p.O 1; Ln 13 16 13 30 $p.O 1
    KnockPoly @(17, 14, 25, 6, 22, 3, 29, 3, 29, 10, 26, 7, 18, 15) 2
    BArrow 17 15 28 4 2.5 6 7 $p.G $p.O
}
function Icon-audit($g, $s, $p) {
    Doc 4 3 22 27 $p 5
    Ln 8 10 15 10 $p.O 2; Ln 8 15 13 15 $p.O 2; Ln 8 20 11 20 $p.O 2
    KnockEll 15 15 29 29 2
    Ell 15 15 29 29 $p.P $p.O
    Ln 22 22 22 18 $p.O 2; Ln 22 22 25 22 $p.O 2
}
function Icon-split_zone($g, $s, $p) {
    Slab 3 14 29 28 $p
    for ($y = 12; $y -lt 30; $y += 4) { Box 15 $y 17 ($y + 2) $p.R }
    Arrow 13 7 3 7 $p.B; Arrow 19 7 29 7 $p.B
}
function Icon-merge_zone($g, $s, $p) {
    Slab 3 14 29 28 $p
    Arrow 3 7 13 7 $p.B; Arrow 29 7 19 7 $p.B
}
function Icon-replace_shores($g, $s, $p) {
    Slab 3 3 21 8 $p
    Post 6 8 28 $p 3.5; Post 14 8 28 $p 3.5
    KnockEll 19 17 30 28 2
    Refresh 24.5 22.5 5.5 $p.B
}
function Icon-fit_height($g, $s, $p) {
    # head-only height adjustment: short up/down arrow pairs beside the post head
    Slab 3 3 29 8 $p
    Post 16 8 28 $p 5
    Arrow 7 21 7 10 $p.B; Arrow 25 10 25 21 $p.B
}
function Icon-inspect_record($g, $s, $p) {
    Rect 5 5 27 29 $p.C $p.O
    Box 9 9 24 26 $p.P
    Rect 11 3 21 8 $p.O $p.O
    PLine @(11, 18, 15, 22, 22, 13) $p.G 3
}
function Icon-splice_count($g, $s, $p) {
    Post 9 5 29 $p 4 -UHead
    Ln 3 16 15 16 $p.R 2   # joint (Post puts it at 45 % of the height = 16)
    Ln 20 10 20 27 $p.O 2; Ln 26 10 26 27 $p.O 2
    Ln 17 15 29 15 $p.O 2; Ln 17 22 29 22 $p.O 2
}

$Names = @('project_setup', 'generate', 'diagnose', 'params', 'views',
    'install_check', 'check_height', 'check_brace', 'check_load',
    'pour', 'field_input', 'import_schedule', 'import_sensor', 'import_cylinder', 'import_weather', 'replan', 'plan_cost',
    'evaluate', 'dashboard', 'show_status', 'show_strength', 'show_schedule', 'reason', 'legend', 'criteria',
    'approve', 'evidence', 'timeline', 'report', 'floor_sign', 'slab_record', 'quality_ledger', 'package', 'save_views', 'export_web', 'export_ifc', 'audit',
    'same_floor', 'split_zone', 'merge_zone', 'replace_shores', 'fit_height', 'inspect_record', 'splice_count')

# ---------------------------------------------------------------- render
function Render([string]$name, [int]$size, $pal) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'Half'; $g.Clear([System.Drawing.Color]::Transparent)
    $script:G = $g; $script:S = $size
    & "Icon-$name" $g $size $pal
    $g.Dispose(); $bmp
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$cache = @{}
foreach ($t in 'light', 'dark') {
    $sfx = $(if ($t -eq 'dark') { '_dark' } else { '' })
    foreach ($n in $Names) { foreach ($sz in 16, 32) {
        $bmp = Render $n $sz $Themes[$t]
        $bmp.Save((Join-Path $OutDir "${n}_$sz$sfx.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $cache["$t|$n|$sz"] = $bmp
    } }
}

# contact sheets: 32 + 16 + name per cell, on ribbon-coloured background
function Sheet([string]$t, [int]$zoom, [string]$path) {
    $pal = $Themes[$t]; $cols = 5; $cw = 200 * $zoom; $ch = 48 * $zoom; $rows = [Math]::Ceiling($Names.Count / $cols)
    $bmp = New-Object System.Drawing.Bitmap ($cols * $cw + 16), ($rows * $ch + 16)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear($pal.Bg)
    $g.InterpolationMode = 'NearestNeighbor'; $g.PixelOffsetMode = 'Half'; $g.TextRenderingHint = 'ClearTypeGridFit'
    $font = New-Object System.Drawing.Font 'Segoe UI', ([float](8 * $zoom)); $tb = New-Object System.Drawing.SolidBrush $pal.Tx
    for ($i = 0; $i -lt $Names.Count; $i++) {
        $x = 8 + ($i % $cols) * $cw; $y = 8 + [Math]::Floor($i / $cols) * $ch; $n = $Names[$i]
        $g.DrawImage($cache["$t|$n|32"], $x, $y + 6 * $zoom, 32 * $zoom, 32 * $zoom)
        $g.DrawImage($cache["$t|$n|16"], $x + 40 * $zoom, $y + 14 * $zoom, 16 * $zoom, 16 * $zoom)
        $g.DrawString($n, $font, $tb, [float]($x + 62 * $zoom), [float]($y + 15 * $zoom))
    }
    $g.Dispose(); $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
Sheet 'light' 1 (Join-Path $OutDir '_sheet_light.png')
Sheet 'dark' 1 (Join-Path $OutDir '_sheet_dark.png')
if ($ReviewDir) {
    New-Item -ItemType Directory -Force $ReviewDir | Out-Null
    Sheet 'light' 4 (Join-Path $ReviewDir 'review_light.png'); Sheet 'dark' 4 (Join-Path $ReviewDir 'review_dark.png')
}

# self-check: every icon exists in 4 variants with the right size and a transparent corner
$bad = @(); foreach ($n in $Names) { foreach ($t in 'light', 'dark') { foreach ($sz in 16, 32) {
    $b = $cache["$t|$n|$sz"]
    if ($b.Width -ne $sz -or $b.GetPixel(0, 0).A -ne 0) { $bad += "$n/$t/$sz" }
    $b.Dispose() } } }
if ($bad) { throw "icon check failed: $($bad -join ', ')" }
Write-Host "$($Names.Count) icons x 4 variants written to $OutDir"
