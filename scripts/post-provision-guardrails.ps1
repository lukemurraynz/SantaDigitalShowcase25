Param(
    [string]$EnvName,
    [string]$ResourceGroup,
    [string]$ApiAppName,
    [string]$DrasiNamespace = "drasi-system",
    [string]$SignalROverridePath = "drasi/manifests/signalr-reaction-override.yaml",
    [string]$WebHost,
    [switch]$SeedDemoData
)

Write-Host "Applying environment guardrails for '$EnvName'..."

if (-not $ResourceGroup) {
    $ResourceGroup = az config get --query "defaults[?name=='group'].value" -o tsv 2>$null
}
if (-not $ApiAppName) {
    $ApiAppName = az containerapp list --query "[0].name" -o tsv 2>$null
}

if (-not $ResourceGroup -or -not $ApiAppName) {
    Write-Error "Resource group or API app name not resolved. Provide -ResourceGroup and -ApiAppName."
    exit 1
}

function Resolve-OverridePath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    if ([System.IO.Path]::IsPathRooted($Path)) { return $Path }
    $scriptRoot = $PSScriptRoot
    $repoRoot = Split-Path -Parent $scriptRoot  # scripts/ -> repo root
    $full = Join-Path $repoRoot $Path
    return $full
}

$overridePath = Resolve-OverridePath -Path $SignalROverridePath

# 1) Apply Drasi SignalR reaction override to avoid provider image mutation
if ($overridePath -and (Test-Path $overridePath)) {
    Write-Host "Applying SignalR reaction override manifest at $overridePath..."
    kubectl apply -n $DrasiNamespace -f $overridePath | Out-Host
    kubectl rollout status deployment/wishlist-signalr-reaction-override -n $DrasiNamespace --timeout=180s | Out-Host
}
else {
    Write-Warning "Override manifest not found (path resolved to '$overridePath'). Skipping override apply."
}

# NOTE: SignalR override deployment skipped - using in-process SignalR hub instead
# The in-process hub (DrasiEventsHub) provides better reliability and eliminates the need
# for an external SignalR gateway service. The proxy code path in Program.cs is only
# activated when DRASI_SIGNALR_INTERNAL_BASE_URL environment variable is set.
# Write-Host "Applying SignalR reaction override manifest..."
# kubectl apply -n $DrasiNamespace -f $SignalROverridePath | Out-Host
# kubectl rollout status deployment/wishlist-signalr-reaction-override -n $DrasiNamespace --timeout=180s | Out-Host
#
# $signalrIp = kubectl get svc wishlist-signalr-reaction-override -n $DrasiNamespace -o jsonpath='{.status.loadBalancer.ingress[0].ip}'
# if (-not $signalrIp) {
#     Write-Error "Failed to resolve SignalR override service IP."
#     exit 1
# }
# Write-Host "SignalR override service IP: $signalrIp"

# 2) Set API env vars for View Service base URL if available
$apiFqdn = az containerapp show -n $ApiAppName -g $ResourceGroup --query 'properties.configuration.ingress.fqdn' -o tsv
Write-Host "API FQDN: $apiFqdn"

# Try to detect Drasi View Service public endpoint via known query container service
# Try to detect Drasi View Service public endpoint via Kubernetes Service in the namespace
try {
    $servicesJson = kubectl get svc -n $DrasiNamespace -o json | ConvertFrom-Json
    $viewService = $null
    foreach ($svc in $servicesJson.items) {
        $name = $svc.metadata.name
        if ($name -match 'view') {
            # Prefer services with LoadBalancer ingress IP
            $ing = $svc.status.loadBalancer.ingress
            if ($ing -and $ing.Count -gt 0 -and $ing[0].ip) {
                $viewService = $svc
                break
            }
        }
    }
    $viewSvcIp = if ($viewService) { $viewService.status.loadBalancer.ingress[0].ip } else { $null }
}
catch {
    $viewSvcIp = $null
}
if (-not $viewSvcIp) {
    # Fallback to Azure public IP lookup if Kubernetes service not found
    $viewSvcIp = az network public-ip list --query "[?contains(dnsSettings.fqdn,'drasi')].ipAddress | [0]" -o tsv 2>$null
}
if (-not $viewSvcIp) { $viewSvcIp = "" }

$envVars = @()
# NOTE: DRASI_SIGNALR_INTERNAL_BASE_URL is intentionally NOT set to use in-process SignalR hub
# instead of proxying to external Drasi service. The in-process hub provides better reliability
# and eliminates dependency on external SignalR gateway service.
# $envVars += "DRASI_SIGNALR_INTERNAL_BASE_URL=http://$signalrIp"
if ($viewSvcIp) { $envVars += "DRASI_VIEW_SERVICE_BASE_URL=http://$viewSvcIp" }
if ($WebHost) { $envVars += "WEB_HOST=$WebHost" }

Write-Host "Updating Container App env vars: $($envVars -join ', ')"
az containerapp update -n $ApiAppName -g $ResourceGroup --set-env-vars $envVars | Out-Host

# 3) Verify endpoints
Write-Host "Verifying Container App negotiate and insights endpoints..."
try {
    # Test in-process SignalR hub negotiate endpoint
    $negResp = Invoke-WebRequest -Method Post -Uri "https://$apiFqdn/api/hub/negotiate" -Headers @{"X-Role" = "admin" }
    $neg = $negResp.Content | ConvertFrom-Json
    Write-Host "✅ Negotiate endpoint working - Connection ID: $($neg.connectionId)" -ForegroundColor Green
    Write-Host "   Available transports: $($neg.availableTransports | ForEach-Object { $_.transport } | Sort-Object -Unique -join ',')"
}
catch {
    Write-Warning "Negotiate endpoint test failed: $_"
}

try {
    $insResp = Invoke-WebRequest -Uri "https://$apiFqdn/api/v1/diagnostics/insights"
    $ins = $insResp.Content | ConvertFrom-Json
    Write-Host "Diagnostics insights fetched."
}
catch {}

# If WebHost provided, also verify through SWA forwarding gateway to catch CORS/preflight issues
if ($WebHost) {
    Write-Host "Verifying via Static Web App proxy at '$WebHost'..."
    try {
        $swaNegResp = Invoke-WebRequest -Method Post -Uri "https://$WebHost/api/hub/negotiate"
        $swaNeg = $swaNegResp.Content | ConvertFrom-Json
        Write-Host "SWA negotiate transports: $($swaNeg.availableTransports | ForEach-Object { $_.transport } | Sort-Object -Unique -join ',')"
    }
    catch {
        Write-Warning "SWA negotiate failed: $_"
    }
    try {
        $swaTrendingResp = Invoke-WebRequest -Uri "https://$WebHost/api/v1/drasi/debug/wishlist-trending-1h"
        $swaTrending = $swaTrendingResp.Content | ConvertFrom-Json
        $count = if ($swaTrending) { $swaTrending.resultCount } else { 'n/a' }
        Write-Host "SWA debug trending resultCount: $count"
    }
    catch {
        Write-Warning "SWA debug trending failed: $_"
    }
}

Write-Host "Guardrails applied successfully."

# 4) Optional demo data seeding (uses API rather than direct Event Hub send to avoid missing connection string)
if ($SeedDemoData) {
    Write-Host "Seeding demo data via API endpoints..."
    try {
        $baseApi = "https://$apiFqdn"
        $children = @('c1', 'c2', 'c3')
        foreach ($c in $children) {
            $createUrl = "$baseApi/api/v1/children"
            $body = @{ childId = $c } | ConvertTo-Json
            $resp = Invoke-WebRequest -Method Post -Uri $createUrl -Body $body -Headers @{ 'Content-Type' = 'application/json' } -ErrorAction Stop
        }
        $items = @(
            @{ child = 'c1'; text = 'Lego Starfighter'; category = 'toys' },
            @{ child = 'c2'; text = 'Nintendo Switch'; category = 'gaming' },
            @{ child = 'c1'; text = 'Nintendo Switch'; category = 'gaming' },
            @{ child = 'c3'; text = 'Stuffed Bear'; category = 'toys' }
        )
        foreach ($it in $items) {
            $wishlistUrl = "$baseApi/api/v1/children/$($it.child)/wishlist-items"
            $wBody = @{ text = $it.text; category = $it.category } | ConvertTo-Json
            Invoke-WebRequest -Method Post -Uri $wishlistUrl -Body $wBody -Headers @{ 'Content-Type' = 'application/json' } -ErrorAction SilentlyContinue | Out-Null
        }
        # Re-check trending debug
        if ($WebHost) {
            Start-Sleep -Seconds 5
            try {
                $trendingAfter = Invoke-WebRequest -Uri "https://$WebHost/api/v1/drasi/debug/wishlist-trending-1h" -ErrorAction Stop
                $jsonAfter = $trendingAfter.Content | ConvertFrom-Json
                Write-Host "Trending resultCount after seed: $($jsonAfter.resultCount)"
            }
            catch {
                Write-Warning "Trending debug fetch failed after seed: $_"
            }
        }
        Write-Host "Demo data seeding complete."
    }
    catch {
        Write-Warning "Demo data seeding encountered an error: $_"
    }
}
