param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$folder=Join-Path $root '.tools/browser-extension'
New-Item -ItemType Directory -Force $folder | Out-Null
$file=Join-Path $folder 'uBOLite-2026.1012.1545.edge.zip'
$expected='f2ff2b53deb4fba68e9f2898ac8d21d0c26b7a169655647ee34b0a59783c628e'
if(!(Test-Path -LiteralPath $file)) {
    Invoke-WebRequest -Uri 'https://github.com/uBlockOrigin/uBOL-home/releases/download/2026.1012.1545/uBOLite_2026.1012.1545.edge.zip' -OutFile $file
}
if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'uBO Lite package checksum mismatch'}
Write-Output 'PASS pinned official uBO Lite package checksum'
