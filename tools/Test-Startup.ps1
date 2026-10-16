param([string]$Executable='dist/DIPlayer-win-x64/DIPlayer.exe')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$exe=(Resolve-Path -LiteralPath $Executable).Path
$evidence=Join-Path $root ('.tools/startup-smoke/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$process=Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -ArgumentList @('--selftest','--visual-only','--check-icon','--evidence',('"'+$evidence+'"'),'--test-library',('"'+(Join-Path $evidence 'library')+'"')) -WindowStyle Hidden -PassThru
if(!$process.WaitForExit(20000)) {
    $process.Kill()
    throw 'Startup smoke check timed out'
}
$process.Refresh()
if($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $evidence 'icon-check.txt')) -or (Test-Path -LiteralPath (Join-Path $evidence 'app-selftest-error.txt'))) {
    throw ('Published app failed to load its window or icon. Diagnostic files: '+$evidence)
}
Write-Output 'PASS published application starts and loads its embedded window and tray icons'
