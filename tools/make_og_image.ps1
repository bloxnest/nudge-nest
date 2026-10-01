# Renders tools/og-image.html to docs/assets/og-image.jpg (1200x630) with Microsoft Edge in headless mode.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
$png = Join-Path $env:TEMP "nudgenest-og.png"
$profileDir = Join-Path $env:TEMP "nudgenest-og-profile"
$page = ([System.Uri](Join-Path $PSScriptRoot "og-image.html")).AbsoluteUri   # spaces become %20
Start-Process -FilePath $edge -Wait -WindowStyle Hidden -ArgumentList @(
    "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run", "--allow-file-access-from-files",
    "--force-device-scale-factor=1", "--user-data-dir=`"$profileDir`"", "--window-size=1200,630",
    "--screenshot=`"$png`"", $page)
if (-not (Test-Path $png)) { throw "Edge didn't render the page" }
python -c "from PIL import Image; Image.open(r'$png').convert('RGB').crop((0,0,1200,630)).save(r'$root\docs\assets\og-image.jpg', quality=86, optimize=True, progressive=True)"
if ($LASTEXITCODE -ne 0) { throw "couldn't convert the screenshot" }
Write-Output "wrote docs\assets\og-image.jpg"
