# Gera o ícone PROVISÓRIO (um X feito de pontos de bordado). A identidade visual final virá depois.
# Uso: powershell -File build\gerar-icone.ps1   -> escreve src\Bordadox.App\Assets\bordadox.ico
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'src\Bordadox.App\Assets\bordadox.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    # Fundo: tecido (quadrado arredondado azul-petróleo)
    $r = [int]($s * 0.18); $d = 2 * $r
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($s - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d - 1, $s - $d - 1, $d, $d, 0, 90); $path.AddArc(0, $s - $d - 1, $d, $d, 90, 90); $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 24, 62, 84))), $path)
    # X de pontos: duas diagonais tracejadas, uma dourada e outra vermelha
    $m = $s * 0.24; $w = [Math]::Max(1.5, $s * 0.11)
    foreach ($c in @(@{ a = [System.Drawing.Color]::FromArgb(255, 245, 190, 60); x1 = $m; y1 = $m; x2 = $s - $m; y2 = $s - $m },
                     @{ a = [System.Drawing.Color]::FromArgb(255, 235, 85, 80);  x1 = $s - $m; y1 = $m; x2 = $m; y2 = $s - $m })) {
        $pen = New-Object System.Drawing.Pen $c.a, $w
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        if ($s -ge 48) { $pen.DashStyle = 'Custom'; $pen.DashPattern = [single[]](1.6, 1.0) }
        $g.DrawLine($pen, [single]$c.x1, [single]$c.y1, [single]$c.x2, [single]$c.y2)
        $pen.Dispose()
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    return , $ms.ToArray()
}

$frames = foreach ($s in $sizes) { , (New-Frame $s) }
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
