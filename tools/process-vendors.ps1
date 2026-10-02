param(
  [Parameter(Mandatory = $true)][string]$InDir,
  [Parameter(Mandatory = $true)][string]$OutDir
)

Add-Type -AssemblyName System.Drawing

# Fit box: wordmarks (ASUS, MSI, NVIDIA...) become 256 wide; square marks (AMD arrow) 80 tall.
$BoxW = 256
$BoxH = 80

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

Get-ChildItem -Path $InDir -Filter *.png | ForEach-Object {
  $src = New-Object System.Drawing.Bitmap($_.FullName)

  # 1. Whiten every visible pixel (keep alpha) so mixed-colour logos read as one uniform
  #    light treatment on the dark theme, and dark logos (ASRock) stay visible.
  for ($y = 0; $y -lt $src.Height; $y++) {
    for ($x = 0; $x -lt $src.Width; $x++) {
      $p = $src.GetPixel($x, $y)
      if ($p.A -gt 0 -and ($p.R -ne 255 -or $p.G -ne 255 -or $p.B -ne 255)) {
        $src.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($p.A, 255, 255, 255))
      }
    }
  }

  # 2. Trim fully-transparent margins so every logo is tight in its box.
  $minX = $src.Width; $minY = $src.Height; $maxX = -1; $maxY = -1
  for ($y = 0; $y -lt $src.Height; $y++) {
    for ($x = 0; $x -lt $src.Width; $x++) {
      if ($src.GetPixel($x, $y).A -gt 0) {
        if ($x -lt $minX) { $minX = $x }
        if ($x -gt $maxX) { $maxX = $x }
        if ($y -lt $minY) { $minY = $y }
        if ($y -gt $maxY) { $maxY = $y }
      }
    }
  }
  if ($maxX -lt 0) { Write-Output ("SKIP (empty) " + $_.Name); $src.Dispose(); return }
  $cropRect = [System.Drawing.Rectangle]::FromLTRB($minX, $minY, $maxX + 1, $maxY + 1)
  $trimmed = $src.Clone($cropRect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $src.Dispose()

  # 3. Fit inside the box, preserving aspect (never upscale beyond 2x).
  $scale = [Math]::Min($BoxW / [double]$trimmed.Width, $BoxH / [double]$trimmed.Height)
  if ($scale -gt 2) { $scale = 2 }
  $w = [Math]::Max(1, [int][Math]::Round($trimmed.Width * $scale))
  $h = [Math]::Max(1, [int][Math]::Round($trimmed.Height * $scale))

  $final = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($final)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.DrawImage($trimmed, 0, 0, $w, $h)
  $g.Dispose()
  $trimmed.Dispose()

  $outPath = Join-Path $OutDir ($_.BaseName + ".png")
  $final.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
  $final.Dispose()
  Write-Output ("OK " + $_.Name + " -> " + $w + "x" + $h)
}
