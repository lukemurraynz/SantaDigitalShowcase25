# Frontend Prebuild Hook
# NOTE: This script is no longer used for production deployments.
# The frontend is now built via Dockerfile.multi and served from the API Container App.
# Frontend and API are same-origin, so no CORS or special SignalR configuration is needed.
#
# For local development:
# - Frontend: npm run dev (Vite dev server with proxy to localhost:8080)
# - Backend: dotnet run (API on localhost:8080)

Write-Host "=== Frontend Prebuild Hook ===" -ForegroundColor Cyan
Write-Host "NOTE: Frontend is now served from the API Container App (same-origin)." -ForegroundColor Yellow
Write-Host "No special configuration needed - API and frontend are on the same host." -ForegroundColor Yellow

# For local development, create a simple .env.production with empty/relative URLs
$lines = @(
    "# Frontend is served from Container App - use relative URLs",
    "VITE_API_URL=",
    "VITE_SIGNALR_URL="
)
$envFile = Join-Path $PSScriptRoot '.env.production'
$lines -join "`n" | Out-File -FilePath $envFile -Encoding utf8
Write-Host "Created .env.production with relative URL configuration" -ForegroundColor Green

exit 0

