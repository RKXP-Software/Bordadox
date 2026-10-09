# Gera o ícone do Bordadox: um bastidor com um X sendo bordado (a agulha completa o último braço do X).
# Uso: powershell -File build\gerar-icone.ps1 [-Previa caminho.png]
#   -> escreve src\Bordadox.App\Assets\bordadox.ico (e, com -Previa, um PNG de 512 px para conferir)
param([string]$Previa = '', [string]$Montagem = '')
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'src\Bordadox.App\Assets\bordadox.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256

# Paleta
$cTile = [System.Drawing.Color]::FromArgb(255, 24, 62, 84)      # azul petróleo (tecido)
$cLine = [System.Drawing.Color]::FromArgb(255, 244, 241, 234)   # branco quente (bastidor e agulha)
$cGold = [System.Drawing.Color]::FromArgb(255, 245, 190, 60)    # linha dourada (o X)
$cRed  = [System.Drawing.Color]::FromArgb(255, 235, 85, 80)     # linha vermelha (fio da agulha)

function New-Pen($color, [float]$w, $cap = 'Round') {
    $p = New-Object System.Drawing.Pen $color, $w
    $p.StartCap = $cap; $p.EndCap = $cap; $p.LineJoin = 'Round'
    return $p
}
function Circle($g, $pen, [float]$cx, [float]$cy, [float]$r) { $g.DrawEllipse($pen, $cx - $r, $cy - $r, 2 * $r, 2 * $r) }

# Desenha o ícone num espaço de 256 x 256. $nivel: 1 = simples (16-32 px), 2 = médio (48 px), 3 = completo.
function Draw-Icon($g, [int]$nivel) {
    # Fundo: quadrado arredondado
    $r = 46; $d = 2 * $r
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc(256 - $d, 0, $d, $d, 270, 90)
    $path.AddArc(256 - $d, 256 - $d, $d, $d, 0, 90); $path.AddArc(0, 256 - $d, $d, $d, 90, 90); $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $cTile), $path)

    $cx = 112; $cy = 150

    if ($nivel -eq 1) {
        # Versão pequena: aro grosso e X inteiro, sem detalhes finos.
        $g.DrawEllipse((New-Pen $cLine 20), 24, 38, 208, 208)
        $g.DrawLine((New-Pen $cGold 26), 84, 122, 172, 210)
        $g.DrawLine((New-Pen $cGold 26), 172, 122, 84, 210)
        return
    }

    # Aro do bastidor (e o aro interno, só na versão completa)
    $g.DrawEllipse((New-Pen $cLine 12), $cx - 82, $cy - 82, 164, 164)
    if ($nivel -ge 3) { $g.DrawEllipse((New-Pen $cLine 4), $cx - 64, $cy - 64, 128, 128) }

    # Parafuso do bastidor: barra no alto com um botão em cada ponta
    $g.DrawLine((New-Pen $cLine 9), $cx, 40, $cx, $cy - 82)
    $g.DrawLine((New-Pen $cLine 9), $cx - 42, 40, $cx + 42, 40)
    $bf = New-Object System.Drawing.SolidBrush $cLine
    foreach ($sx in @(-44, 44)) { $g.FillEllipse($bf, $cx + $sx - 9, 31, 18, 18) }

    # O X: três braços já bordados (o quarto, para cima e à direita, é a agulha)
    $arm = 34
    $g.DrawLine((New-Pen $cGold 15), $cx - $arm, $cy - $arm, $cx + $arm, $cy + $arm)          # \ inteiro
    $g.DrawLine((New-Pen $cGold 15), $cx - $arm, $cy + $arm, $cx, $cy)                         # / de baixo (já costurado)

    # Agulha: do centro do X para cima e à direita, saindo do bastidor
    $dx = 0.7071; $dy = -0.7071; $nx = 0.7071; $ny = 0.7071
    $len = 126
    $bx = $cx + $dx * $len; $by = $cy + $dy * $len
    $w = 9
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([float]$cx, [float]$cy)),
        (New-Object System.Drawing.PointF([float]($bx + $nx * $w), [float]($by + $ny * $w))),
        (New-Object System.Drawing.PointF([float]($bx - $nx * $w), [float]($by - $ny * $w))))
    $g.FillPolygon($bf, $pts)
    # Buraco da agulha (cor do fundo)
    $ex = $bx - $dx * 26; $ey = $by - $dy * 26
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $cTile), $ex - 4.5, $ey - 4.5, 9, 9)

    if ($nivel -ge 3) {
        # Fio vermelho: sai do buraco da agulha e cai em curva
        $thread = New-Pen $cRed 7
        $g.DrawBezier($thread, [float]$ex, [float]$ey, [float]($ex + 40), [float]($ey - 34), [float]($ex + 52), [float]($ey + 40), [float]($ex + 22), [float]($ey + 70))
    }
}

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($s / 256.0, $s / 256.0)
    $nivel = if ($s -le 32) { 1 } elseif ($s -le 48) { 2 } else { 3 }
    Draw-Icon $g $nivel
    $g.Dispose()
    return $bmp
}

if ($Previa) {
    $p = New-Frame 512
    $p.Save($Previa, [System.Drawing.Imaging.ImageFormat]::Png); $p.Dispose()
    Write-Host "Prévia: $Previa"
}

if ($Montagem) {
    $ms = 16, 24, 32, 48, 64, 128
    $sheet = New-Object System.Drawing.Bitmap 520, 300
    $sg = [System.Drawing.Graphics]::FromImage($sheet)
    $sg.Clear([System.Drawing.Color]::White)
    $sg.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 32, 32, 32))), 0, 150, 520, 150)
    $x = 10
    foreach ($s in $ms) { $f = New-Frame $s; $sg.DrawImage($f, $x, 10); $sg.DrawImage($f, $x, 160); $x += $s + 12; $f.Dispose() }
    $sheet.Save($Montagem, [System.Drawing.Imaging.ImageFormat]::Png); $sg.Dispose(); $sheet.Dispose()
}

$frames = foreach ($s in $sizes) {
    $bmp = New-Frame $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $frames[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Close(); $fs.Close()
Write-Host "Ícone gerado: $out"
