param([switch]$WithServer,[ValidateSet('linux-arm64','linux-x64')][string]$ServerRid='linux-arm64',[ValidateSet('win-x86','win-x64')][string]$WindowsRid='win-x64')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $dotnet=if(Test-Path '.tools/dotnet/dotnet.exe'){Join-Path $root '.tools/dotnet/dotnet.exe'}else{'dotnet'}
    & $dotnet run --project src/DIPlayer.Tests -c Release --artifacts-path .tools/check-artifacts
    if($LASTEXITCODE -ne 0){throw 'Integration checks failed'}
    $destination="dist/DIPlayer-$WindowsRid"
    & $dotnet publish src/DIPlayer -c Release -r $WindowsRid --self-contained true --artifacts-path .tools/release-artifacts -o $destination -p:DebugType=None -p:DebugSymbols=false
    if($LASTEXITCODE -ne 0){throw 'Windows publish failed'}
    & (Join-Path $PSScriptRoot 'Test-Startup.ps1') -Executable (Join-Path $destination 'DIPlayer.exe')
    Copy-Item README.md,THIRD_PARTY_NOTICES.md -Destination $destination
    Copy-Item docs -Destination $destination -Recurse -Force
    Compress-Archive -Path $destination -DestinationPath "$destination.zip" -Force
    if($WithServer) {
        & $dotnet publish src/DIPlayer.Server -c Release -r $ServerRid --self-contained true --artifacts-path .tools/release-artifacts -o "dist/DIPlayer-server-$ServerRid" -p:DebugType=None -p:DebugSymbols=false
        if($LASTEXITCODE -ne 0){throw 'Server publish failed'}
    }
    Get-FileHash "$destination.zip" -Algorithm SHA256 | Select-Object Hash,Path
}finally{Pop-Location}
