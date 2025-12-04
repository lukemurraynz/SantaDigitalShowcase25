Param(
    [string]$Output = 'environment-validation.json'
)

$apiHost = azd env get-value apiHost 2>$null
if (-not $apiHost) { Write-Warning 'apiHost not found in azd env'; }

function Invoke-Endpoint($method, $url) {
    try {
        $resp = curl -s -o /dev/null -w '%{http_code}' -X $method $url
        return [int]$resp
    }
    catch { return -1 }
}

$results = [ordered]@{}
$results.apiHost = $apiHost
$base = "https://$apiHost"
$checks = @(
    @{ name = 'plain-health'; url = "$base/plain-health" },
    @{ name = 'healthz'; url = "$base/healthz" },
    @{ name = 'readyz'; url = "$base/readyz" },
    @{ name = 'hub-negotiate'; url = "$base/api/hub/negotiate?negotiateVersion=1" }
)

foreach ($c in $checks) {
    $code = Invoke-Endpoint 'GET' $c.url
    $results[$c.name] = $code
}

# emit JSON
($results | ConvertTo-Json -Depth 5) | Set-Content $Output
Write-Host "✅ Validation complete. Results saved to $Output" -ForegroundColor Green
Write-Host ($results | ConvertTo-Json -Depth 5)
