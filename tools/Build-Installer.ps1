param([string]$Compiler)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(!$Compiler) {
    $candidates=@((Join-Path $root '.tools/inno/ISCC.exe'),"${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe","$env:ProgramFiles/Inno Setup 7/ISCC.exe")
    $Compiler=$candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if(!$Compiler){throw 'Install Inno Setup 6 or 7, or pass -Compiler with the path to ISCC.exe.'}
if(!(Test-Path (Join-Path $root 'dist/DIPlayer-win-x64/DIPlayer.exe'))){throw 'Run tools/Build.ps1 first.'}
& $Compiler /Q (Join-Path $root 'installer/DIPlayer.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
Get-FileHash (Join-Path $root 'dist/DIPlayer-Setup-0.2.0-alpha-x64.exe') -Algorithm SHA256
