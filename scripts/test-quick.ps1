$apiUrl = "https://santadigitalshowcase-uhgsd-api.greenocean-92276740.australiaeast.azurecontainerapps.io"
Write-Host "`nQuick Validation..." -ForegroundColor Cyan
try {
    $h = Invoke-RestMethod "$apiUrl/healthz" -TimeoutSec 5
    Write-Host "Health: OK" -ForegroundColor Green
    exit 0
} catch {
    Write-Host "Health: FAIL - $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
