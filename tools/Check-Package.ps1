param([string]$Archive='dist/DIPlayer-win-x64.zip',[string]$CredentialFile='clientAPI.txt')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$values=@()
if(Test-Path -LiteralPath $CredentialFile) {
    $raw=Get-Content -LiteralPath $CredentialFile -Raw
    foreach($pattern in @('(?im)^\s*client\s*id\s*[:=]\s*(\S+)','(?im)^\s*client\s*secret\s*[:=]\s*(\S+)')) {
        $value=[regex]::Match($raw,$pattern).Groups[1].Value
        if(!$value){throw 'Cannot parse credential fields for scan'}
        $values+=$value
    }
}
$zip=[IO.Compression.ZipFile]::OpenRead((Resolve-Path $Archive))
try {
    foreach($entry in $zip.Entries) {
        if($entry.FullName -match '(?i)(^|/)(clientAPI\.txt|secrets\.env|token\.json|settings\.json)$|\.(key|pem|pfx)$'){throw ('Unexpected private file: '+$entry.FullName)}
        if($entry.FullName -match '/DIPlayer(\.Core|\.Audio)?\.dll$') {
            $stream=$entry.Open();$memory=[IO.MemoryStream]::new()
            try{$stream.CopyTo($memory);$bytes=$memory.ToArray()}finally{$stream.Dispose();$memory.Dispose()}
            foreach($encoding in @([Text.Encoding]::UTF8,[Text.Encoding]::Unicode)) {
                $text=$encoding.GetString($bytes)
                foreach($value in $values){if($text.Contains($value)){throw ('Credential found in '+$entry.FullName)}}
            }
        }
    }
    Write-Output ('PASS package private-file and application-assembly scan: '+$Archive)
}finally{$zip.Dispose()}
