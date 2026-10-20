<#
    Генератор иконок кнопок ленты.

    Иконки лежат рядом со сборкой плагина (Contents\2021) и одновременно вшиты в неё
    под именами NavisElevation.<файл>.png: Navisworks ищет иконку и там, и там, а в
    атрибуте команды путь к иконке может быть только один.

    Запуск:
        powershell -ExecutionPolicy Bypass -File tools\make-calibration-icons.ps1

    Скрипт перерисовывает только иконку калибровки. Остальные иконки нарисованы
    вручную и трогать их не нужно.
#>

param(
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\src\NavisElevation.Navis\Resources')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-Pen([float]$width, [System.Drawing.Color]$color) {
    $pen = New-Object System.Drawing.Pen -ArgumentList $color, $width
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    return $pen
}

function New-PointF([float]$x, [float]$y) {
    return New-Object System.Drawing.PointF -ArgumentList $x, $y
}

function New-CalibrationIcon([int]$size, [string]$path) {
    # Рисуем в координатах 32x32 и масштабируем: так иконки 16 и 32 остаются
    # одинаковыми по рисунку.
    $scale = $size / 32.0

    $bitmap = New-Object System.Drawing.Bitmap -ArgumentList $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $blue = [System.Drawing.Color]::FromArgb(255, 0x1E, 0x88, 0xE5)
    $gray = [System.Drawing.Color]::FromArgb(255, 0x9E, 0x9E, 0x9E)

    # Отсчётная шкала слева: четыре коротких деления.
    $tickPen = New-Pen (2.0 * $scale) $blue
    foreach ($y in @(8, 13, 18, 23)) {
        $graphics.DrawLine($tickPen, (3 * $scale), ($y * $scale), (10 * $scale), ($y * $scale))
    }

    # Стрелка вниз: указание на нулевой уровень.
    $arrowPen = New-Pen (2.6 * $scale) $blue
    $graphics.DrawLine($arrowPen, (20 * $scale), (4 * $scale), (20 * $scale), (20 * $scale))

    $head = @(
        (New-PointF (20 * $scale) (26 * $scale)),
        (New-PointF (15 * $scale) (18 * $scale)),
        (New-PointF (25 * $scale) (18 * $scale))
    )

    $brush = New-Object System.Drawing.SolidBrush -ArgumentList $blue
    $graphics.FillPolygon($brush, $head)

    # Нулевой уровень: серая линия-основание.
    $basePen = New-Pen (2.4 * $scale) $gray
    $graphics.DrawLine($basePen, (3 * $scale), (29 * $scale), (29 * $scale), (29 * $scale))

    $graphics.Dispose()
    $brush.Dispose()
    $tickPen.Dispose()
    $arrowPen.Dispose()
    $basePen.Dispose()

    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()

    Write-Host "[OK] $path"
}

$resolved = [System.IO.Path]::GetFullPath($OutputDir)

if (-not (Test-Path -LiteralPath $resolved)) {
    New-Item -ItemType Directory -Path $resolved | Out-Null
}

New-CalibrationIcon 16 (Join-Path $resolved 'Calibrate16.png')
New-CalibrationIcon 32 (Join-Path $resolved 'Calibrate32.png')
