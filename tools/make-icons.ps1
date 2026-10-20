<#
.SYNOPSIS
    Генерирует иконки кнопок ленты плагина Navis Elevation (16 и 32 px, PNG, прозрачный фон)
    и логотип для README (128 px).

.DESCRIPTION
    Иконки рисуются программно (System.Drawing) в единой гамме: синий #0078D4 + серый + белый.
    Все координаты задаются в пространстве 32x32 и масштабируются для 16 px.

    Скрипт перенесён из плагина для Revit (assembling-manager) без изменений подхода:
    та же палитра, те же вспомогательные функции, та же шестерёнка для настроек.
    Значки других модулей Revit не перенесены - они там были нужны для других кнопок.

    Запуск:  powershell -File tools\make-icons.ps1
    Параметр -PreviewPath позволяет дополнительно сохранить контактный лист для визуальной проверки.
    Параметр -LogoPath задаёт, куда положить логотип для README (по умолчанию docs\logo.png).
#>
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\src\NavisElevation.Navis\Resources"),
    [string]$PreviewPath = "",
    [string]$LogoPath = (Join-Path $PSScriptRoot "..\docs\logo.png")
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# --- Единая палитра ---
$script:Blue      = [System.Drawing.Color]::FromArgb(255, 0, 120, 212)    # #0078D4
$script:DarkBlue  = [System.Drawing.Color]::FromArgb(255, 0, 90, 158)     # #005A9E
$script:LightBlue = [System.Drawing.Color]::FromArgb(255, 179, 215, 242)  # #B3D7F2
$script:Gray      = [System.Drawing.Color]::FromArgb(255, 138, 150, 160)  # #8A96A0
$script:DarkGray  = [System.Drawing.Color]::FromArgb(255, 90, 107, 123)   # #5A6B7B
$script:White     = [System.Drawing.Color]::White

function New-Brush([System.Drawing.Color]$color) {
    return New-Object System.Drawing.SolidBrush($color)
}

function New-Pen([System.Drawing.Color]$color, [single]$width) {
    $pen = New-Object System.Drawing.Pen($color, $width)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    return $pen
}

function Fill-Polygon([System.Drawing.Graphics]$g, [System.Drawing.Color]$color, [object[]]$points) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pts = New-Object 'System.Drawing.PointF[]' $points.Count
    for ($i = 0; $i -lt $points.Count; $i++) {
        $pts[$i] = New-Object System.Drawing.PointF([single]$points[$i][0], [single]$points[$i][1])
    }
    $path.AddPolygon($pts)
    $g.FillPath((New-Brush $color), $path)
    $path.Dispose()
}

function Punch-Hole([System.Drawing.Graphics]$g, [single]$cx, [single]$cy, [single]$r) {
    $old = $g.CompositingMode
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $transparent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Transparent)
    $g.FillEllipse($transparent, $cx - $r, $cy - $r, $r * 2, $r * 2)
    $g.CompositingMode = $old
}

# --- Настройки: шестерёнка ---
function Draw-Gear([System.Drawing.Graphics]$g) {
    $cx = 16.0; $cy = 16.0
    $bodyR = 7.5
    $toothW = 5.0
    $toothH = 4.0
    $blue = New-Brush $script:Blue

    for ($i = 0; $i -lt 8; $i++) {
        $state = $g.Save()
        $g.TranslateTransform($cx, $cy)
        $g.RotateTransform($i * 45)
        $toothX = [single](-($toothW / 2))
        $toothY = [single](-($bodyR + $toothH - 0.5))
        $g.FillRectangle($blue, $toothX, $toothY, [single]$toothW, [single]($toothH + 0.5))
        $g.Restore($state)
    }

    $g.FillEllipse($blue, $cx - $bodyR, $cy - $bodyR, $bodyR * 2, $bodyR * 2)
    Punch-Hole $g $cx $cy 3.5
}

# --- Отметка элемента: стрелка высоты у вертикальной линейки ---
#
# Смысл: вертикальная стрелка с двумя наконечниками рядом с линейкой - стандартный
# знак высоты на чертежах. Внизу горизонтальная базовая линия - то, от чего
# измеряется. В 16 px детали упрощаются: стрелка становится сплошной.
function Draw-Elevation([System.Drawing.Graphics]$g, [int]$size) {
    $blue = New-Brush $script:Blue
    $gray = New-Brush $script:Gray

    # Базовая линия - то, от чего отсчитывается высота.
    $g.FillRectangle($gray, [single]4, [single]26.5, [single]24, [single]2)

    # Вертикальная линейка со штрихами.
    $g.FillRectangle($blue, [single]5, [single]4, [single]2.5, [single]22.5)
    for ($i = 0; $i -lt 4; $i++) {
        $y = 7 + $i * 5
        $g.FillRectangle($blue, [single]7.5, [single]$y, [single]3.5, [single]1.2)
    }

    # Двусторонняя стрелка высоты.
    $pen = New-Pen $script:Blue 2.2
    $g.DrawLine($pen, [single]17, [single]6, [single]17, [single]25)

    # Наконечники стрелки треугольниками.
    Fill-Polygon $g $script:Blue @(@(17, 2.5), @(14, 8), @(20, 8))
    Fill-Polygon $g $script:Blue @(@(17, 28.5), @(14, 23), @(20, 23))
}

# --- Сборка иконки ---
function New-Icon([string]$kind, [int]$size) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $g.Clear([System.Drawing.Color]::Transparent)

        $s = $size / 32.0
        $g.ScaleTransform([single]$s, [single]$s)

        switch ($kind) {
            'Settings'  { Draw-Gear $g }
            'Elevation' { Draw-Elevation $g $size }
            default     { throw "Unknown icon kind: $kind" }
        }
    }
    finally {
        $g.Dispose()
    }
    return $bmp
}

# --- Генерация ---
$kinds = @('Elevation', 'Settings')
$sizes = @(16, 32)

if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$generated = @()
foreach ($kind in $kinds) {
    foreach ($size in $sizes) {
        $bmp = New-Icon $kind $size
        $file = Join-Path $OutputDir ("{0}{1}.png" -f $kind, $size)
        $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()

        # контроль: перечитать и проверить размер
        $check = [System.Drawing.Image]::FromFile($file)
        if ($check.Width -ne $size -or $check.Height -ne $size) {
            $check.Dispose()
            throw "Size mismatch for $file"
        }
        $check.Dispose()
        $generated += $file
        Write-Host ("OK  {0}" -f $file)
    }
}

Write-Host ("Generated {0} icons in {1}" -f $generated.Count, $OutputDir)

# --- Логотип для README ---
#
# Тот же знак отметки, но вчетверо крупнее: рисунок задан в координатах 32x32 и
# масштабируется, поэтому лишних движений не нужно. Прозрачный фон, только синий
# и серый - читается и на светлой, и на тёмной теме GitHub.
if ($LogoPath) {
    $logoDir = Split-Path -Parent $LogoPath
    if ($logoDir -and -not (Test-Path -LiteralPath $logoDir)) {
        New-Item -ItemType Directory -Path $logoDir -Force | Out-Null
    }

    $logo = New-Icon 'Elevation' 128
    $logo.Save($LogoPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $logo.Dispose()
    Write-Host ("Logo saved: {0}" -f $LogoPath)
}

# --- Контактный лист для визуальной проверки ---
if ($PreviewPath) {
    $cell = 250
    $scale = 7
    $preview = [System.Drawing.Bitmap]::new(($cell * $kinds.Count + 40), ($cell * 2 + 60))
    $pg = [System.Drawing.Graphics]::FromImage($preview)
    try {
        $pg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
        $pg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $pg.Clear([System.Drawing.Color]::FromArgb(255, 240, 240, 240))
        $labelFont = New-Object System.Drawing.Font('Segoe UI', 11)
        $labelBrush = New-Brush $script:DarkGray
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center

        for ($row = 0; $row -lt 2; $row++) {
            for ($col = 0; $col -lt $kinds.Count; $col++) {
                $kind = $kinds[$col]
                $srcSize = 32
                $iconPath = Join-Path $OutputDir ("{0}32.png" -f $kind)
                if ($row -eq 1) {
                    $srcSize = 16
                    $iconPath = Join-Path $OutputDir ("{0}16.png" -f $kind)
                }

                $icon = [System.Drawing.Image]::FromFile($iconPath)
                $drawn = $srcSize * $scale
                $cx = 20 + $col * $cell + ($cell - $drawn) / 2
                $cy = 20 + $row * $cell + ($cell - $drawn) / 2
                $pg.DrawImage($icon,
                    (New-Object System.Drawing.Rectangle([int]$cx, [int]$cy, $drawn, $drawn)),
                    0, 0, $srcSize, $srcSize,
                    [System.Drawing.GraphicsUnit]::Pixel)
                $icon.Dispose()

                $lx = 20 + $col * $cell
                $ly = 20 + $row * $cell + $cell - 30
                $labelRect = New-Object System.Drawing.RectangleF([single]$lx, [single]$ly, [single]$cell, [single]24)
                $pg.DrawString($kind, $labelFont, $labelBrush, $labelRect, $format)
            }
        }

        $preview.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host ("Preview saved: {0}" -f $PreviewPath)
    }
    finally {
        $pg.Dispose()
        $preview.Dispose()
    }
}
