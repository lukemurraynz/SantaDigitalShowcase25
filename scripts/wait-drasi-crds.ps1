Param(
    [int]$TimeoutSeconds = 300,
    [int]$IntervalSeconds = 5
)

$crds = @(
    'reactionproviders.drasi.dev',
    'reactions.drasi.dev',
    'continuousqueries.drasi.dev',
    'querycontainers.drasi.dev'
)

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
Write-Host "⏳ Waiting for Drasi CRDs (timeout=$TimeoutSeconds s)" -ForegroundColor Cyan

while ((Get-Date) -lt $deadline) {
    $missing = @()
    foreach ($c in $crds) {
        $exists = kubectl get crd $c 2>$null | Select-String $c
        if (-not $exists) { $missing += $c }
    }
    if ($missing.Count -eq 0) {
        Write-Host "✅ All Drasi CRDs present" -ForegroundColor Green
        exit 0
    }
    Write-Host "Still missing: $($missing -join ', ')" -ForegroundColor Yellow
    Start-Sleep -Seconds $IntervalSeconds
}

Write-Warning "Timed out waiting for Drasi CRDs after $TimeoutSeconds seconds"
exit 1
