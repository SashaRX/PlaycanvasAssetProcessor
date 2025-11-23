# Создаём простое PNG изображение 512x1024 с альфа-каналом
# Используем System.Drawing для создания изображения

Add-Type -AssemblyName System.Drawing

$width = 512
$height = 1024

# Создаём bitmap с альфа-каналом
$bitmap = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)

# Заливаем прозрачным фоном
$graphics.Clear([System.Drawing.Color]::Transparent)

# Рисуем силуэт человека (простой прямоугольник + круг для головы)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 100, 100, 100))

# Тело (прямоугольник)
$bodyWidth = 200
$bodyHeight = 600
$bodyX = ($width - $bodyWidth) / 2
$bodyY = 300
$graphics.FillRectangle($brush, $bodyX, $bodyY, $bodyWidth, $bodyHeight)

# Голова (круг)
$headRadius = 100
$headX = ($width - $headRadius * 2) / 2
$headY = 100
$graphics.FillEllipse($brush, $headX, $headY, $headRadius * 2, $headRadius * 2)

# Сохраняем
$outputPath = Join-Path $PSScriptRoot "refman.png"
$bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)

$graphics.Dispose()
$bitmap.Dispose()

Write-Host "Created refman.png at: $outputPath"
