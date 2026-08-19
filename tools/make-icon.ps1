# 產生 Llama Vulkan 啟動器的應用程式圖示（羊駝頭像）。
# 以 GDI+ 向量方式繪製，輸出含多種尺寸的 .ico，於高 DPI 與工作列都清晰。
Add-Type -AssemblyName System.Drawing

$OutPath = Join-Path $PSScriptRoot '..\llama.ico'
$OutPath = [System.IO.Path]::GetFullPath($OutPath)

# 配色：深色圓角底 + 奶油色羊駝，與應用程式標題列色系一致。
$BackTop    = [System.Drawing.Color]::FromArgb(255, 38, 46, 58)
$BackBottom = [System.Drawing.Color]::FromArgb(255, 22, 27, 34)
$Accent     = [System.Drawing.Color]::FromArgb(255, 46, 160, 67)
$Fur        = [System.Drawing.Color]::FromArgb(255, 244, 232, 214)
$FurShade   = [System.Drawing.Color]::FromArgb(255, 214, 197, 174)
$Muzzle     = [System.Drawing.Color]::FromArgb(255, 201, 180, 154)
$InnerEar   = [System.Drawing.Color]::FromArgb(255, 176, 132, 118)
$Dark       = [System.Drawing.Color]::FromArgb(255, 38, 32, 30)

function New-RoundedPath {
    param([single]$X, [single]$Y, [single]$W, [single]$H, [single]$R)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $R * 2
    $p.AddArc($X, $Y, $d, $d, 180, 90)
    $p.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $p.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $p.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-LlamaBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # 以 256 為基準設計，再依實際尺寸等比縮放。
    $s = $Size / 256.0
    $g.ScaleTransform($s, $s)

    # 圓角底色
    $bg = New-RoundedPath 6 6 244 244 52
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)),
        (New-Object System.Drawing.Point(0, 256)), $BackTop, $BackBottom)
    $g.FillPath($brush, $bg)
    $brush.Dispose()

    # 底部強調色帶，呼應啟動按鈕的綠色
    $clip = $g.Clip
    $g.SetClip($bg)
    $accentBrush = New-Object System.Drawing.SolidBrush($Accent)
    $g.FillRectangle($accentBrush, 0, 226, 256, 30)
    $accentBrush.Dispose()
    $g.Clip = $clip

    $furBrush   = New-Object System.Drawing.SolidBrush($Fur)
    $shadeBrush = New-Object System.Drawing.SolidBrush($FurShade)
    $earBrush   = New-Object System.Drawing.SolidBrush($InnerEar)
    $muzBrush   = New-Object System.Drawing.SolidBrush($Muzzle)
    $darkBrush  = New-Object System.Drawing.SolidBrush($Dark)

    # 耳朵（羊駝招牌的細長香蕉耳）
    foreach ($ear in @(@(84, 34, -10.0), @(140, 34, 10.0))) {
        $ex = [single]$ear[0]; $ey = [single]$ear[1]; $rot = [single]$ear[2]
        $st = $g.Save()
        $g.TranslateTransform($ex + 16, $ey + 40)
        $g.RotateTransform($rot)
        $g.FillEllipse($furBrush, -16, -40, 32, 80)
        $g.FillEllipse($earBrush, -8, -30, 16, 52)
        $g.Restore($st)
    }

    # 脖子
    $neck = New-RoundedPath 96 150 64 92 26
    $g.FillPath($shadeBrush, $neck)
    $neck.Dispose()

    # 頭部
    $g.FillEllipse($furBrush, 74, 78, 108, 104)

    # 口鼻
    $g.FillEllipse($muzBrush, 100, 138, 56, 46)

    # 眼睛
    $g.FillEllipse($darkBrush, 100, 108, 16, 18)
    $g.FillEllipse($darkBrush, 140, 108, 16, 18)

    # 眼神高光（小尺寸時省略，避免糊成一團）
    if ($Size -ge 48) {
        $hi = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 255, 255, 255))
        $g.FillEllipse($hi, 104, 111, 6, 6)
        $g.FillEllipse($hi, 144, 111, 6, 6)
        $hi.Dispose()
    }

    # 鼻孔與嘴
    $g.FillEllipse($darkBrush, 116, 150, 9, 7)
    $g.FillEllipse($darkBrush, 131, 150, 9, 7)
    if ($Size -ge 32) {
        $pen = New-Object System.Drawing.Pen($Dark, 4)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawArc($pen, 116, 158, 24, 16, 20, 140)
        $pen.Dispose()
    }

    # 額前瀏海，讓輪廓更像羊駝
    $bangs = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bangs.AddEllipse(84, 68, 44, 34)
    $bangs.AddEllipse(112, 62, 48, 36)
    $bangs.AddEllipse(140, 70, 40, 32)
    $g.FillPath($furBrush, $bangs)
    $bangs.Dispose()

    $furBrush.Dispose(); $shadeBrush.Dispose(); $earBrush.Dispose()
    $muzBrush.Dispose(); $darkBrush.Dispose()
    $bg.Dispose()
    $g.Dispose()
    return $bmp
}

# 各尺寸以 PNG 壓縮後打包成 ICO 容器。
# 用 ArrayList 存放，避免 PowerShell 陣列相加時把巢狀陣列攤平造成資料遺失。
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$iconSizes = New-Object System.Collections.ArrayList
$iconData = New-Object System.Collections.ArrayList
foreach ($sz in $sizes) {
    $bmp = New-LlamaBitmap -Size $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    if ($bytes.Length -le 0) {
        throw "產生 $sz px 影像失敗"
    }
    [void]$iconSizes.Add([int]$sz)
    [void]$iconData.Add([byte[]]$bytes)
    $ms.Dispose()
    $bmp.Dispose()
}

$fs = New-Object System.IO.FileStream($OutPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$count = $iconData.Count
$bw.Write([UInt16]0)                 # reserved
$bw.Write([UInt16]1)                 # type = icon
$bw.Write([UInt16]$count)

$offset = 6 + (16 * $count)
for ($i = 0; $i -lt $count; $i++) {
    $sz = [int]$iconSizes[$i]
    $len = ([byte[]]$iconData[$i]).Length
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim)            # width
    $bw.Write([byte]$dim)            # height
    $bw.Write([byte]0)               # palette
    $bw.Write([byte]0)               # reserved
    $bw.Write([UInt16]1)             # color planes
    $bw.Write([UInt16]32)            # bits per pixel
    $bw.Write([UInt32]$len)
    $bw.Write([UInt32]$offset)
    $offset += $len
}
for ($i = 0; $i -lt $count; $i++) {
    $bw.Write([byte[]]$iconData[$i])
}
$bw.Flush(); $bw.Close(); $fs.Close()

Write-Output ("Icon written: " + $OutPath + " (" + (Get-Item $OutPath).Length + " bytes, " + $count + " sizes)")
