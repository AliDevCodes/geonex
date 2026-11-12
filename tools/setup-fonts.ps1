# Downloads the Vazirmatn font (OFL license) into src\FastDbExplorer.Wpf\Assets\Fonts before publishing.
# Run by publish.cmd on the build PC. Fonts are optional: if the download fails, the app falls back to Segoe UI.
param([string]$Root)

$dest = Join-Path $Root 'src\FastDbExplorer.Wpf\Assets\Fonts'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$files = @('Vazirmatn-Regular.ttf', 'Vazirmatn-Bold.ttf')
# Two sources: GitHub first, then jsDelivr (a CDN mirror of the same repository) if GitHub is blocked or slow.
$bases = @('https://github.com/rastikerdar/vazirmatn/raw/master/fonts/ttf/', 'https://cdn.jsdelivr.net/gh/rastikerdar/vazirmatn@master/fonts/ttf/')

$ProgressPreference = 'SilentlyContinue' # no progress bar: it floods the console on slow links

$missing = $files | Where-Object { -not (Test-Path (Join-Path $dest $_)) }
if ($missing.Count -eq 0) { Write-Host 'Fonts already present.'; exit 0 }

foreach ($f in $missing) {
    $target = Join-Path $dest $f
    $ok = $false
    foreach ($base in $bases) {
        try {
            # -TimeoutSec: a stalled connection must not hang the whole publish (the font is optional).
            Invoke-WebRequest -Uri ($base + $f) -OutFile $target -UseBasicParsing -TimeoutSec 120
            $size = (Get-Item $target).Length
            if ($size -lt 50000) { throw "file too small ($size bytes)" }
            Write-Host "Downloaded $f ($size bytes) from $base"
            $ok = $true
            break
        } catch {
            Remove-Item $target -ErrorAction SilentlyContinue
            Write-Warning "Could not download $f from $base : $($_.Exception.Message)"
        }
    }
    if (-not $ok) { Write-Warning "Copy $f into $dest by hand (download it from github.com/rastikerdar/vazirmatn)." }
}
exit 0
