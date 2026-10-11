# Generates the theme-aware README wordmark (assets/SmartMixerWhite.png for the
# dark GitHub theme and assets/SmartMixerDark.png for the light theme) using
# the same knob mark as the application icon.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$assetsDirectory = Join-Path $repositoryRoot "assets"
New-Item -ItemType Directory -Force -Path $assetsDirectory | Out-Null

function New-RoundedRectanglePath([single]$X, [single]$Y, [single]$Width, [single]$Height, [single]$Radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-KnobIcon([int]$Size) {
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $size = [single]$Size
    $tile = New-RoundedRectanglePath ($size * 0.04) ($size * 0.04) ($size * 0.92) ($size * 0.92) ($size * 0.2)
    $background = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new(0, 0, $size, $size),
        [System.Drawing.Color]::FromArgb(255, 35, 43, 56),
        [System.Drawing.Color]::FromArgb(255, 18, 24, 34),
        [single]45)
    $graphics.FillPath($background, $tile)
    $centerX = $size * 0.5
    $centerY = $size * 0.51
    $radius = $size * 0.295
    $ringPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 83, 202, 246), [single]($size * 0.045))
    $ringPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $ringPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawArc($ringPen, $centerX - $radius, $centerY - $radius, $radius * 2, $radius * 2, 135, 270)
    $tickPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 226, 236, 245), [single]($size * 0.018))
    $tickPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $tickPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    foreach ($angle in @(135, 180, 225, 270, 315, 360, 405)) {
        $radians = $angle * [Math]::PI / 180
        $inner = $radius * 1.16
        $outer = $radius * 1.31
        $graphics.DrawLine(
            $tickPen,
            [single]($centerX + [Math]::Cos($radians) * $inner),
            [single]($centerY + [Math]::Sin($radians) * $inner),
            [single]($centerX + [Math]::Cos($radians) * $outer),
            [single]($centerY + [Math]::Sin($radians) * $outer))
    }
    $knobRadius = $size * 0.17
    $knobBounds = [System.Drawing.RectangleF]::new($centerX - $knobRadius, $centerY - $knobRadius, $knobRadius * 2, $knobRadius * 2)
    $knob = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $knobBounds,
        [System.Drawing.Color]::FromArgb(255, 106, 215, 250),
        [System.Drawing.Color]::FromArgb(255, 32, 137, 192),
        [single]45)
    $graphics.FillEllipse($knob, $knobBounds)
    $pointerPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 245, 251, 255), [single]($size * 0.024))
    $pointerPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pointerPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($pointerPen, $centerX, $centerY, $centerX, $centerY - $knobRadius * 0.58)
    $pointerPen.Dispose()
    $knob.Dispose()
    $tickPen.Dispose()
    $ringPen.Dispose()
    $background.Dispose()
    $tile.Dispose()
    $graphics.Dispose()
    return $bitmap
}

function New-Wordmark {
    param(
        [string]$Path,
        [System.Drawing.Color]$NameColor,
        [System.Drawing.Color]$AccentColor
    )

    $width = 1600
    $height = 420
    $iconSize = 330
    $gap = 78
    $fontSize = 168
    $tracking = $fontSize * 0.012

    $bitmap = [System.Drawing.Bitmap]::new($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    $font = [System.Drawing.Font]::new('Segoe UI Variable Display Semib', [single]$fontSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $format = [System.Drawing.StringFormat]::GenericTypographic
    $nameBrush = [System.Drawing.SolidBrush]::new($NameColor)
    $accentBrush = [System.Drawing.SolidBrush]::new($AccentColor)

    $characters = 'SmartMixer'.ToCharArray()
    $advances = [System.Collections.Generic.List[single]]::new()
    foreach ($character in $characters) {
        $advances.Add($graphics.MeasureString([string]$character, $font, 10000, $format).Width)
    }
    $textWidth = 0
    foreach ($advance in $advances) { $textWidth += $advance + $tracking }
    $textWidth -= $tracking
    $textHeight = $graphics.MeasureString('SmartMixer', $font, 10000, $format).Height

    $totalWidth = $iconSize + $gap + $textWidth
    $startX = ($width - $totalWidth) / 2
    $textX = $startX + $iconSize + $gap
    $textY = ($height - $textHeight) / 2

    $icon = New-KnobIcon 990
    $graphics.DrawImage($icon, [single]$startX, [single](($height - $iconSize) / 2), [single]$iconSize, [single]$iconSize)

    $x = $textX
    for ($index = 0; $index -lt $characters.Length; $index++) {
        $brush = if ($index -ge 'Smart'.Length) { $accentBrush } else { $nameBrush }
        $graphics.DrawString([string]$characters[$index], $font, $brush, [single]$x, [single]$textY, $format)
        $x += $advances[$index] + $tracking
    }

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $icon.Dispose()
    $nameBrush.Dispose()
    $accentBrush.Dispose()
    $font.Dispose()
    $format.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

# Dark GitHub theme: bright name, accent blue.
New-Wordmark -Path (Join-Path $assetsDirectory 'SmartMixerWhite.png') `
    -NameColor ([System.Drawing.Color]::FromArgb(255, 242, 247, 252)) `
    -AccentColor ([System.Drawing.Color]::FromArgb(255, 91, 200, 245))

# Light GitHub theme: deep navy name, denser accent blue.
New-Wordmark -Path (Join-Path $assetsDirectory 'SmartMixerDark.png') `
    -NameColor ([System.Drawing.Color]::FromArgb(255, 27, 36, 49)) `
    -AccentColor ([System.Drawing.Color]::FromArgb(255, 18, 137, 199))

Write-Output "Wordmarks written to $assetsDirectory"
