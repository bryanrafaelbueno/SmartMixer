$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$imageDirectory = Join-Path $PSScriptRoot "..\src\SmartMixer\Images"
$imageDirectory = [System.IO.Path]::GetFullPath($imageDirectory)

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

function New-SmartMixerBitmap([int]$Width, [int]$Height) {
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $size = [single][Math]::Min($Width, $Height)
    $left = [single](($Width - $size) / 2)
    $top = [single](($Height - $size) / 2)
    $tile = New-RoundedRectanglePath ($left + $size * 0.04) ($top + $size * 0.04) ($size * 0.92) ($size * 0.92) ($size * 0.2)
    $background = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new($left, $top, $size, $size),
        [System.Drawing.Color]::FromArgb(255, 35, 43, 56),
        [System.Drawing.Color]::FromArgb(255, 18, 24, 34),
        [single]45)
    $graphics.FillPath($background, $tile)

    $centerX = $left + $size * 0.5
    $centerY = $top + $size * 0.51
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
    $knobBounds = [System.Drawing.RectangleF]::new(
        $centerX - $knobRadius,
        $centerY - $knobRadius,
        $knobRadius * 2,
        $knobRadius * 2)
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

function Save-SmartMixerIcon([System.Drawing.Bitmap]$Source, [int]$Width, [int]$Height, [string]$Path) {
    $output = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($output)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.DrawImage($Source, 0, 0, $Width, $Height)
    $output.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $output.Dispose()
}

$master = New-SmartMixerBitmap 1024 1024
foreach ($name in @("Square44x44Logo.png", "Square150x150Logo.png", "Square310x310Logo.png", "StoreLogo.png", "SplashScreen.png")) {
    $path = Join-Path $imageDirectory $name
    $existing = [System.Drawing.Image]::FromFile($path)
    $width = $existing.Width
    $height = $existing.Height
    $existing.Dispose()
    Save-SmartMixerIcon $master $width $height $path
}

$widePath = Join-Path $imageDirectory "Wide310x150Logo.png"
$wideExisting = [System.Drawing.Image]::FromFile($widePath)
$wideWidth = $wideExisting.Width
$wideHeight = $wideExisting.Height
$wideExisting.Dispose()
$wide = [System.Drawing.Bitmap]::new($wideWidth, $wideHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$wideGraphics = [System.Drawing.Graphics]::FromImage($wide)
$wideGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$wideGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$wideGraphics.Clear([System.Drawing.Color]::Transparent)
$logoSize = [int]([Math]::Min($wide.Width, $wide.Height) * 0.86)
$small = [System.Drawing.Bitmap]::new($logoSize, $logoSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$smallGraphics = [System.Drawing.Graphics]::FromImage($small)
$smallGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$smallGraphics.DrawImage($master, 0, 0, $logoSize, $logoSize)
$smallGraphics.Dispose()
$wideGraphics.DrawImage($small, 8, [single](($wide.Height - $logoSize) / 2), $logoSize, $logoSize)
$font = [System.Drawing.Font]::new("Segoe UI", [single]24, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$textBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 236, 243, 250))
$format = [System.Drawing.StringFormat]::new()
$format.Alignment = [System.Drawing.StringAlignment]::Near
$format.LineAlignment = [System.Drawing.StringAlignment]::Center
$textBounds = [System.Drawing.RectangleF]::new($logoSize + 20, 0, $wide.Width - $logoSize - 24, $wide.Height)
$wideGraphics.DrawString("SmartMixer", $font, $textBrush, $textBounds, $format)
$wide.Save($widePath, [System.Drawing.Imaging.ImageFormat]::Png)
$format.Dispose()
$textBrush.Dispose()
$font.Dispose()
$small.Dispose()
$wideGraphics.Dispose()
$wide.Dispose()

$iconSizes = @(16, 24, 32, 48, 64, 128, 256)
$iconImages = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $iconSizes) {
    $iconBitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($iconBitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.DrawImage($master, 0, 0, $size, $size)
    $stream = [System.IO.MemoryStream]::new()
    $iconBitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $iconImages.Add($stream.ToArray())
    $stream.Dispose()
    $graphics.Dispose()
    $iconBitmap.Dispose()
}

$iconStream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($iconStream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$iconSizes.Count)
$offset = 6 + (16 * $iconSizes.Count)
for ($index = 0; $index -lt $iconSizes.Count; $index++) {
    $size = $iconSizes[$index]
    $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
    $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$iconImages[$index].Length)
    $writer.Write([UInt32]$offset)
    $offset += $iconImages[$index].Length
}
foreach ($imageBytes in $iconImages) {
    $writer.Write($imageBytes)
}
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $imageDirectory "SmartMixer.ico"), $iconStream.ToArray())
$writer.Dispose()
$iconStream.Dispose()
$master.Dispose()
