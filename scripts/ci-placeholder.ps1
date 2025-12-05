param()
Write-Host "CI Placeholder: build, test, lint, security scan" -ForegroundColor Cyan
Write-Host "dotnet build santadigitalshowcase.sln" -ForegroundColor Yellow
Write-Host "dotnet test --no-build" -ForegroundColor Yellow
Write-Host "npm --prefix frontend run build" -ForegroundColor Yellow
Write-Host "(Security scan placeholder)" -ForegroundColor Yellow
