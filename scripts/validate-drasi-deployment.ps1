<#
.SYNOPSIS
    Validates Drasi deployment readiness and configuration correctness.

.DESCRIPTION
    Comprehensive validation script that checks:
    - Kubernetes secrets for placeholder credentials
    - Pod health and initialization status
    - CRD installation completeness
    - Resource name consistency with Bicep conventions
    - Dapr component configuration
    - Cosmos DB database/container existence

    Fails early if critical issues are detected, preventing cascading failures.

.PARAMETER ResourceGroup
    Azure resource group name where resources are deployed.

.PARAMETER Project
    Project name used in resource naming (e.g., 'santaworkshop').

.PARAMETER Env
    Environment name used in resource naming (e.g., 'dev', 'prod').

.PARAMETER Namespace
    Kubernetes namespace where Drasi is deployed (default: drasi-system).

.EXAMPLE
    .\validate-drasi-deployment.ps1 -ResourceGroup "rg-dev" -Project "santaworkshop" -Env "dev"

.EXAMPLE
    # Get values from azd environment
    $rg = azd env get-value AZURE_RESOURCE_GROUP
    $env = azd env get-value AZURE_ENV_NAME
    .\validate-drasi-deployment.ps1 -ResourceGroup $rg -Project "santaworkshop" -Env $env
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory = $true)]
    [string]$Project,

    [Parameter(Mandatory = $true)]
    [string]$Env,

    [string]$Namespace = 'drasi-system'
)

$ErrorActionPreference = 'Stop'
$WarningPreference = 'Continue'
$prefix = "$Project-$Env".ToLower()

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  🔍 Drasi Deployment Validation" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Resource Group: $ResourceGroup" -ForegroundColor DarkGray
Write-Host "  Project: $Project" -ForegroundColor DarkGray
Write-Host "  Environment: $Env" -ForegroundColor DarkGray
Write-Host "  Prefix: $prefix" -ForegroundColor DarkGray
Write-Host "  Namespace: $Namespace" -ForegroundColor DarkGray
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

$validationErrors = @()
$validationWarnings = @()

# ============================================================================
# 1. KUBERNETES CLUSTER CONNECTIVITY
# ============================================================================
Write-Host "1️⃣  Checking Kubernetes cluster connectivity..." -ForegroundColor Yellow

try {
    $clusterInfo = kubectl cluster-info 2>&1
    if ($LASTEXITCODE -ne 0) {
        $validationErrors += "❌ Cannot connect to Kubernetes cluster. Run: az aks get-credentials -n $prefix-aks -g $ResourceGroup"
    }
    else {
        Write-Host "   ✅ Kubernetes cluster accessible" -ForegroundColor Green
    }
}
catch {
    $validationErrors += "❌ kubectl not installed or not in PATH"
}

# ============================================================================
# 2. COSMOS DB SECRET VALIDATION
# ============================================================================
Write-Host ""
Write-Host "2️⃣  Validating Cosmos DB secrets..." -ForegroundColor Yellow

try {
    $secretJson = kubectl get secret cosmos-statestore -n $Namespace -o json 2>&1
    if ($LASTEXITCODE -eq 0) {
        $secret = $secretJson | ConvertFrom-Json

        # Decode base64 values - key is optional when using Managed Identity authentication
        $endpoint = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($secret.data.endpoint))
        $key = if ($secret.data.key) { [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($secret.data.key)) } else { '' }

        # Check for placeholder patterns
        $placeholderPatterns = @('REPLACE_', 'TODO', 'CHANGEME', 'PLACEHOLDER', 'YOUR_', '<', '>')
        $endpointHasPlaceholder = $placeholderPatterns | Where-Object { $endpoint -match $_ }
        $keyHasPlaceholder = if ($key) { $placeholderPatterns | Where-Object { $key -match $_ } } else { $null }

        if ($endpointHasPlaceholder -or $keyHasPlaceholder) {
            $validationErrors += "❌ CRITICAL: Placeholder credentials detected in cosmos-statestore secret!"
            if ($endpointHasPlaceholder) {
                $validationErrors += "   Endpoint contains: $($endpointHasPlaceholder -join ', ')"
            }
            if ($keyHasPlaceholder) {
                $validationErrors += "   Key contains: $($keyHasPlaceholder -join ', ')"
            }
            $validationErrors += "   FIX: Run scripts/fix-drasi-secrets.ps1 to replace with actual credentials"
        }
        else {
            # Check if using Managed Identity (no key) or key-based auth
            if (-not $key) {
                Write-Host "   ✅ Cosmos DB secret configured for Managed Identity (endpoint only)" -ForegroundColor Green
            }
            else {
                Write-Host "   ✅ Cosmos DB secret contains valid credentials" -ForegroundColor Green
            }

            # Verify endpoint format
            if ($endpoint -match '^https://[a-z0-9-]+\.documents\.azure\.com:443/?$') {
                Write-Host "   ✅ Endpoint format valid: $endpoint" -ForegroundColor Green
            }
            else {
                $validationWarnings += "⚠️  Endpoint format unusual: $endpoint"
            }

            # Check for common naming mistakes
            $expectedAccount = "$prefix-cosmos"
            if ($endpoint -notmatch $expectedAccount) {
                $validationWarnings += "⚠️  Endpoint doesn't match expected account name '$expectedAccount'"
                $validationWarnings += "   Actual endpoint: $endpoint"
            }
        }
    }
    else {
        $validationWarnings += "⚠️  cosmos-statestore secret not found in namespace $Namespace"
        $validationWarnings += "   Secret will be created by apply-drasi-resources.ps1"
    }
}
catch {
    $validationErrors += "❌ Failed to check Cosmos DB secret: $($_.Exception.Message)"
}
# 2. COSMOS IDENTITY/SECRET VALIDATION
Write-Host ""
Write-Host "2️⃣  Validating Cosmos DB authentication (Managed Identity preferred)..." -ForegroundColor Yellow

try {
    # If Dapr component has azureClientId, prefer MI path and skip secret checks
    $compJson = kubectl get component cosmos-state -n $Namespace -o json 2>&1
    $usingMi = $false
    if ($LASTEXITCODE -eq 0) {
        $comp = $compJson | ConvertFrom-Json
        $azureClientIdMeta = $comp.spec.metadata | Where-Object { $_.name -eq 'azureClientId' }
        if ($azureClientIdMeta) {
            $usingMi = $true
            Write-Host "   ✅ Using Managed Identity (azureClientId present)" -ForegroundColor Green
        }
    }

    if ($usingMi) {
        # Nothing else to do for secrets
        # Optional: verify endpoint metadata exists
        $endpointMeta = $comp.spec.metadata | Where-Object { $_.name -eq 'endpoint' }
        if ($endpointMeta) {
            $endpoint = $endpointMeta.value
            if ($endpoint -match '^https://[a-z0-9-]+\.documents\.azure\.com:443/?$') {
                Write-Host "   ✅ Endpoint format valid: $endpoint" -ForegroundColor Green
            } else {
                $validationWarnings += "⚠️  Endpoint format unusual: $endpoint"
            }
        }
    }
    else {
        Write-Host "   ℹ️ Managed Identity not detected; validating secret (fallback)" -ForegroundColor Yellow
        $secretJson = kubectl get secret cosmos-statestore -n $Namespace -o json 2>&1
        if ($LASTEXITCODE -eq 0) {
            $secret = $secretJson | ConvertFrom-Json
            $endpoint = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($secret.data.endpoint))
            $key = if ($secret.data.key) { [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($secret.data.key)) } else { '' }
            $placeholderPatterns = @('REPLACE_', 'TODO', 'CHANGEME', 'PLACEHOLDER', 'YOUR_', '<', '>')
            $endpointHasPlaceholder = $placeholderPatterns | Where-Object { $endpoint -match $_ }
            $keyHasPlaceholder = if ($key) { $placeholderPatterns | Where-Object { $key -match $_ } } else { $null }
            if ($endpointHasPlaceholder -or $keyHasPlaceholder) {
                $validationErrors += "❌ CRITICAL: Placeholder credentials detected in cosmos-statestore secret!"
            } else {
                if (-not $key) {
                    Write-Host "   ✅ Cosmos DB secret configured for Managed Identity (endpoint only)" -ForegroundColor Green
                } else {
                    Write-Host "   ✅ Cosmos DB secret contains valid credentials" -ForegroundColor Green
                }
            }
        } else {
            $validationWarnings += "⚠️  cosmos-statestore secret not found in namespace $Namespace"
        }
    }
}
catch {
    $validationErrors += "❌ Failed to check Cosmos DB authentication: $($_.Exception.Message)"
}

# ============================================================================
# 3. DAPR COMPONENT CONFIGURATION
# ============================================================================
Write-Host ""
Write-Host "3️⃣  Validating Dapr component configuration..." -ForegroundColor Yellow

try {
    $componentJson = kubectl get component cosmos-state -n $Namespace -o json 2>&1
    if ($LASTEXITCODE -eq 0) {
        $component = $componentJson | ConvertFrom-Json

        # Extract metadata values
        $metadataMap = @{}
        foreach ($item in $component.spec.metadata) {
            $metadataMap[$item.name] = $item.value
        }

        # Check database name
        $expectedDb = "elves_demo"
        if ($metadataMap.ContainsKey('database')) {
            if ($metadataMap['database'] -eq $expectedDb) {
                Write-Host "   ✅ Database name correct: $expectedDb" -ForegroundColor Green
            }
            else {
                $validationErrors += "❌ Database name mismatch! Expected: $expectedDb, Found: $($metadataMap['database'])"
                $validationErrors += "   FIX: kubectl patch component cosmos-state -n $Namespace --type='json' -p='[{`"op`": `"replace`", `"path`": `"/spec/metadata/2/value`", `"value`": `"$expectedDb`"}]'"
            }
        }
        else {
            $validationWarnings += "⚠️  Database name not configured in component"
        }

        # Check container name
        $expectedContainer = "wishlists"
        if ($metadataMap.ContainsKey('collection')) {
            if ($metadataMap['collection'] -eq $expectedContainer) {
                Write-Host "   ✅ Container name correct: $expectedContainer" -ForegroundColor Green
            }
            else {
                $validationErrors += "❌ Container name mismatch! Expected: $expectedContainer, Found: $($metadataMap['collection'])"
                $validationErrors += "   FIX: kubectl patch component cosmos-state -n $Namespace --type='json' -p='[{`"op`": `"replace`", `"path`": `"/spec/metadata/3/value`", `"value`": `"$expectedContainer`"}]'"
            }
        }
        else {
            $validationWarnings += "⚠️  Container (collection) name not configured in component"
        }

    }
    else {
        $validationWarnings += "⚠️  cosmos-state component not found in namespace $Namespace"
        $validationWarnings += "   Component will be created by apply-drasi-resources.ps1"
    }
}
catch {
    $validationErrors += "❌ Failed to check Dapr component: $($_.Exception.Message)"
}

# ============================================================================
# 4. POD HEALTH STATUS
# ============================================================================
Write-Host ""
Write-Host "4️⃣  Checking Drasi pod health..." -ForegroundColor Yellow

try {
    $podsJson = kubectl get pods -n $Namespace -o json 2>&1
    if ($LASTEXITCODE -eq 0) {
        $pods = ($podsJson | ConvertFrom-Json).items

        if ($pods.Count -eq 0) {
            $validationWarnings += "⚠️  No pods found in namespace $Namespace"
            $validationWarnings += "   Pods will be created by azd deploy drasi"
        }
        else {
            foreach ($pod in $pods) {
                $podName = $pod.metadata.name
                $phase = $pod.status.phase

                # Check container statuses
                foreach ($containerStatus in $pod.status.containerStatuses) {
                    $containerName = $containerStatus.name
                    $ready = $containerStatus.ready
                    $restartCount = $containerStatus.restartCount

                    # Check for crash loop
                    if ($containerStatus.state.waiting) {
                        $reason = $containerStatus.state.waiting.reason
                        if ($reason -match 'CrashLoopBackOff|Error|ImagePullBackOff') {
                            $validationErrors += "❌ Pod $podName container $containerName in $reason state!"
                            $validationErrors += "   Check logs: kubectl logs -n $Namespace $podName -c $containerName"

                            # Try to get recent logs for context
                            try {
                                $recentLogs = kubectl logs -n $Namespace $podName -c $containerName --tail 5 2>&1
                                if ($recentLogs) {
                                    $validationErrors += "   Recent logs: $($recentLogs -join ' | ')"
                                }
                            }
                            catch {}
                        }
                    }

                    # Check restart count
                    if ($restartCount -gt 5) {
                        $validationWarnings += "⚠️  Pod $podName container $containerName has restarted $restartCount times"
                        $validationWarnings += "   May indicate recurring initialization issues"
                    }

                    # Check readiness
                    if ($ready) {
                        Write-Host "   ✅ Pod $podName container $containerName ready" -ForegroundColor Green
                    }
                    else {
                        $validationWarnings += "⚠️  Pod $podName container $containerName not ready"
                    }
                }
            }
        }
    }
    else {
        $validationWarnings += "⚠️  Cannot list pods in namespace $Namespace"
    }
}
catch {
    $validationErrors += "❌ Failed to check pod health: $($_.Exception.Message)"
}

# ============================================================================
# 5. CRD INSTALLATION
# ============================================================================
Write-Host ""
Write-Host "5️⃣  Checking Drasi CRD installation..." -ForegroundColor Yellow

$requiredCrds = @(
    'continuousqueries.drasi.io',
    'sources.drasi.io',
    'reactions.drasi.io'
)

try {
    $installedCrds = kubectl get crd -o json 2>&1 | ConvertFrom-Json
    $installedCrdNames = $installedCrds.items | ForEach-Object { $_.metadata.name }

    $missingCrds = @()
    foreach ($crd in $requiredCrds) {
        if ($installedCrdNames -contains $crd) {
            Write-Host "   ✅ CRD $crd installed" -ForegroundColor Green
        }
        else {
            $missingCrds += $crd
        }
    }

    if ($missingCrds.Count -gt 0) {
        $validationWarnings += "⚠️  Missing Drasi CRDs: $($missingCrds -join ', ')"
        $validationWarnings += "   CRDs will be installed by Drasi control plane initialization"
        $validationWarnings += "   If pods are running but CRDs missing, check: kubectl logs -n $Namespace <drasi-api-pod>"
    }
}
catch {
    $validationErrors += "❌ Failed to check CRD installation: $($_.Exception.Message)"
}
# 5. DRASI CONTROL-PLANE CHECK (NO CRDs REQUIRED)
Write-Host ""
Write-Host "5️⃣  Checking Drasi control plane (no CRDs expected)..." -ForegroundColor Yellow

# Drasi resources are managed via the Drasi Management API, not Kubernetes CRDs.
# See .github/copilot-instructions.md: "They have NO CRDs."
try {
    # Basic liveness check: ensure drasi-api and drasi-resource-provider are running
    $deploys = kubectl get deploy -n $Namespace -o json 2>$null | ConvertFrom-Json
    if ($deploys) {
        $dpMap = @{}
        foreach ($d in $deploys.items) { $dpMap[$d.metadata.name] = $d.status.readyReplicas }
        $apiReady = $dpMap['drasi-api'] -ge 1
        $rpReady  = $dpMap['drasi-resource-provider'] -ge 1
        if ($apiReady -and $rpReady) {
            Write-Host "   ✅ Drasi control plane deployments are ready" -ForegroundColor Green
        } else {
            $validationWarnings += "⚠️  Drasi control plane not fully ready (apiReady=$apiReady, resourceProviderReady=$rpReady)"
        }
    } else {
        $validationWarnings += "⚠️  Unable to read control-plane deployment status"
    }
}
catch {
    $validationWarnings += "⚠️  Skipped control-plane readiness check: $($_.Exception.Message)"
}

# ============================================================================
# 6. COSMOS DB RESOURCE VERIFICATION
# ============================================================================
Write-Host ""
Write-Host "6️⃣  Verifying Cosmos DB resources in Azure..." -ForegroundColor Yellow

$expectedCosmosAccount = "$prefix-cosmos"

try {
    $cosmosAccountJson = az cosmosdb show -n $expectedCosmosAccount -g $ResourceGroup 2>&1
    if ($LASTEXITCODE -eq 0) {
        $cosmosAccount = $cosmosAccountJson | ConvertFrom-Json
        Write-Host "   ✅ Cosmos DB account exists: $expectedCosmosAccount" -ForegroundColor Green

        # Check database
        $databasesJson = az cosmosdb sql database list -n $expectedCosmosAccount -g $ResourceGroup 2>&1
        if ($LASTEXITCODE -eq 0) {
            $databases = $databasesJson | ConvertFrom-Json
            $expectedDb = "elves_demo"

            if ($databases | Where-Object { $_.name -eq $expectedDb }) {
                Write-Host "   ✅ Database '$expectedDb' exists" -ForegroundColor Green

                # Check container
                $containersJson = az cosmosdb sql container list -n $expectedCosmosAccount -g $ResourceGroup -d $expectedDb 2>&1
                if ($LASTEXITCODE -eq 0) {
                    $containers = $containersJson | ConvertFrom-Json
                    $expectedContainer = "wishlists"

                    if ($containers | Where-Object { $_.name -eq $expectedContainer }) {
                        Write-Host "   ✅ Container '$expectedContainer' exists" -ForegroundColor Green
                    }
                    else {
                        $validationErrors += "❌ Container '$expectedContainer' not found in database '$expectedDb'"
                        $validationErrors += "   Available containers: $($containers.name -join ', ')"
                    }
                }
            }
            else {
                $validationErrors += "❌ Database '$expectedDb' not found"
                $validationErrors += "   Available databases: $($databases.name -join ', ')"
            }
        }
    }
    else {
        $validationErrors += "❌ Cosmos DB account '$expectedCosmosAccount' not found in resource group '$ResourceGroup'"
    }
}
catch {
    $validationErrors += "❌ Failed to verify Cosmos DB resources: $($_.Exception.Message)"
}

# ============================================================================
# 7. RESOURCE NAME CONSISTENCY
# ============================================================================
Write-Host ""
Write-Host "7️⃣  Verifying resource name consistency..." -ForegroundColor Yellow

$expectedResources = @{
    'ContainerApp' = "$prefix-api"
    'CosmosDB'     = "$prefix-cosmos"
    'EventHub'     = "$prefix-eh"
    'AKS'          = "$prefix-aks"
}

try {
    $resourcesJson = az resource list -g $ResourceGroup --query "[].{name:name, type:type}" -o json 2>&1
    if ($LASTEXITCODE -eq 0) {
        $resources = $resourcesJson | ConvertFrom-Json
        $resourceNames = $resources | ForEach-Object { $_.name }

        foreach ($resourceType in $expectedResources.Keys) {
            $expectedName = $expectedResources[$resourceType]
            if ($resourceNames -contains $expectedName) {
                Write-Host "   ✅ $resourceType found: $expectedName" -ForegroundColor Green
            }
            else {
                $validationWarnings += "⚠️  $resourceType not found with expected name: $expectedName"
                $validationWarnings += "   Available resources: $($resourceNames -join ', ')"
            }
        }
    }
}
catch {
    $validationErrors += "❌ Failed to verify resource names: $($_.Exception.Message)"
}

# ============================================================================
# SUMMARY
# ============================================================================
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  📊 Validation Summary" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan

if ($validationErrors.Count -eq 0 -and $validationWarnings.Count -eq 0) {
    Write-Host ""
    Write-Host "  ✅ All validation checks passed!" -ForegroundColor Green
    Write-Host "  🚀 Deployment is ready for operation" -ForegroundColor Green
    Write-Host ""
    exit 0
}

if ($validationWarnings.Count -gt 0) {
    Write-Host ""
    Write-Host "  ⚠️  WARNINGS ($($validationWarnings.Count)):" -ForegroundColor Yellow
    foreach ($warning in $validationWarnings) {
        Write-Host "     $warning" -ForegroundColor Yellow
    }
}

if ($validationErrors.Count -gt 0) {
    Write-Host ""
    Write-Host "  ❌ ERRORS ($($validationErrors.Count)):" -ForegroundColor Red
    foreach ($errItem in $validationErrors) {
        Write-Host "     $errItem" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "  🔧 To fix issues automatically, run:" -ForegroundColor Cyan
    Write-Host "     .\scripts\fix-drasi-deployment.ps1 -ResourceGroup $ResourceGroup -Project $Project -Env $Env" -ForegroundColor White
    Write-Host ""
    exit 1
}

Write-Host ""
