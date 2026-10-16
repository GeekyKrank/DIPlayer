param([string]$Query = 'ambient', [uri]$PlaylistUrl)
$ErrorActionPreference = 'Stop'
$headers = @{ Accept = 'application/json' }
if ($env:SC_ACCESS_TOKEN) { $headers.Authorization = 'OAuth ' + $env:SC_ACCESS_TOKEN }
function Test-Endpoint([string]$Path) {
    $response = Invoke-WebRequest -Uri ('https://api.soundcloud.com' + $Path) -Headers $headers -SkipHttpErrorCheck -TimeoutSec 15
    Write-Host ('HTTP {0}' -f [int]$response.StatusCode)
    if (-not $response.IsSuccessStatusCode -and [int]$response.StatusCode -ge 400) { return $null }
    return ($response.Content | ConvertFrom-Json)
}
$search = Test-Endpoint ('/tracks?q=' + [uri]::EscapeDataString($Query) + '&limit=1&linked_partitioning=true')
if ($search) {
    $tracks = if ($search.collection) { @($search.collection) } else { @($search) }
    $track = $tracks | Select-Object -First 1
    if ($track) {
        Write-Host ('Track: {0}; access: {1}' -f $track.title, $track.access)
        if ($track.id) {
            $streams = Test-Endpoint ('/tracks/' + [uri]::EscapeDataString([string]$track.id) + '/streams')
            if ($streams) { Write-Host ('Stream response fields: ' + (($streams.PSObject.Properties.Name) -join ', ')) }
        }
    }
}
if ($PlaylistUrl) {
    if ($PlaylistUrl.Scheme -ne 'https' -or $PlaylistUrl.Host -ne 'soundcloud.com' -or $PlaylistUrl.AbsolutePath -notmatch '^/[^/]+/sets/[^/]+/?$' -or $PlaylistUrl.Query) {
        throw 'Нужна HTTPS-ссылка на публичный soundcloud.com/author/sets/name без параметров.'
    }
    $playlist = Test-Endpoint ('/resolve?url=' + [uri]::EscapeDataString($PlaylistUrl.AbsoluteUri))
    if ($playlist) { Write-Host ('Resolved kind: {0}; title: {1}' -f $playlist.kind, $playlist.title) }
}
Write-Host 'Это проверка HTTP-доступа, не подтверждение воспроизведения.'
