param([Parameter(Mandatory = $true)][string]$Dir)

Add-Type -AssemblyName System.Drawing

Get-ChildItem -Path $Dir -Filter *.png | ForEach-Object {
  $bmp = New-Object System.Drawing.Bitmap($_.FullName)
  $total = $bmp.Width * $bmp.Height
  $opaque = 0
  $transparent = 0
  # sample every pixel for small images
  for ($y = 0; $y -lt $bmp.Height; $y += 2) {
    for ($x = 0; $x -lt $bmp.Width; $x += 2) {
      $a = $bmp.GetPixel($x, $y).A
      if ($a -eq 255) { $opaque++ } elseif ($a -eq 0) { $transparent++ }
    }
  }
  $sampled = [Math]::Ceiling($bmp.Width / 2.0) * [Math]::Ceiling($bmp.Height / 2.0)
  $corner = $bmp.GetPixel(0, 0)
  $centre = $bmp.GetPixel([int]($bmp.Width / 2), [int]($bmp.Height / 2))
  $opaquePct = [Math]::Round(100.0 * $opaque / $sampled, 1)
  Write-Output ("{0,-14} {1}x{2}  opaque={3}%  transparent={4}%  corner=ARGB({5},{6},{7},{8})  centre=ARGB({9},{10},{11},{12})" -f `
    $_.Name, $bmp.Width, $bmp.Height, $opaquePct, [Math]::Round(100.0 * $transparent / $sampled, 1), `
    $corner.A, $corner.R, $corner.G, $corner.B, $centre.A, $centre.R, $centre.G, $centre.B)
  $bmp.Dispose()
}
