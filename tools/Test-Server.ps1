param([string]$BaseUrl='http://127.0.0.1:5080')
$ErrorActionPreference='Stop'
$report=[ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('o');checks=@()}
function Check([bool]$Condition,[string]$Label) {
    if(!$Condition){throw $Label}
    $report.checks += $Label
    Write-Output ('PASS '+$Label)
}
$health=Invoke-RestMethod "$BaseUrl/health"
Check ($health.status -eq 'ok') 'Health endpoint'
$search=Invoke-RestMethod "$BaseUrl/search?q=ambient"
Check ($search.tracks.Count -gt 0 -and $search.tracks.Count -le 25) 'Paged search through server'
$playlist=Invoke-RestMethod "$BaseUrl/resolve?url=https%3A%2F%2Fsoundcloud.com%2Flaugebabagnohm%2Fsets%2Fambient-soundscapes-selections"
Check ($playlist.public -and $playlist.count -gt 0) 'Public playlist resolution through server'
$stream=Invoke-RestMethod "$BaseUrl/tracks/$($search.tracks[0].id)/stream"
Check (([uri]$stream).Host.EndsWith('.sndcdn.com')) 'Direct signed CDN stream URL'
$bad=Invoke-WebRequest "$BaseUrl/search?q=ambient&next=https%3A%2F%2Fexample.com%2F" -SkipHttpErrorCheck
Check ([int]$bad.StatusCode -eq 400) 'Reject cursor outside official API'
$bad=Invoke-WebRequest "$BaseUrl/resolve?url=http%3A%2F%2F127.0.0.1%2F" -SkipHttpErrorCheck
Check ([int]$bad.StatusCode -eq 400) 'Reject non-SoundCloud import URL'
$limited=$false
for($i=0;$i -lt 40;$i++) {
    $response=Invoke-WebRequest "$BaseUrl/health" -SkipHttpErrorCheck
    if([int]$response.StatusCode -eq 429){$limited=$true;break}
}
Check $limited 'Server budget rejects excess requests with HTTP 429'
$report | ConvertTo-Json -Depth 5 | Set-Content evidence/server-checks.json
