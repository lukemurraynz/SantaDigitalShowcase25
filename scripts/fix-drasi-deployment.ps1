<#
.SYNOPSIS
    Automatically fixes common Drasi deployment issues.

.DESCRIPTION
    Detects and repairs:
    - Placeholder credentials in Kubernetes secrets
    - Wrong Cosmos DB endpoints (e.g., incorrect region suffixes)
    - Database/container name mismatches in Dapr components
    - Pod initialization failures due to configuration errors

    After applying fixes, restarts affected pods and verifies health.

.PARAMETER ResourceGroup
    Azure resource group name where resources are deployed.

.PARAMETER Project
    Project name used in resource naming (e.g., 'santaworkshop').

.PARAMETER Env
    Environment name used in resource naming (e.g., 'dev', 'prod').

.PARAMETER Namespace
    Kubernetes namespace where Drasi is deployed (default: drasi-system).

.PARAMETER SkipRestart
    If set, skips automatic pod restart after fixes are applied.

.EXAMPLE
    .\fix-drasi-deployment.ps1 -ResourceGroup "rg-dev" -Project "santaworkshop" -Env "dev"

.EXAMPLE
    # Get values from azd environment
    $rg = azd env get-value AZURE_RESOURCE_GROUP
    $env = azd env get-value AZURE_ENV_NAME
    .\fix-drasi-deployment.ps1 -ResourceGroup $rg -Project "santaworkshop" -Env $env
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory=$true)]
    [string]$Project,

    [Parameter(Mandatory=$true)]
    [string]$Env,

    [string]$Namespace = 'drasi-system',

    [switch]$SkipRestart
)

$ErrorActionPreference = 'Stop'
$WarningPreference = 'Continue'
$prefix = "$Project-$Env".ToLower()
$cosmosAccount = "$prefix-cosmos"

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  🔧 Drasi Deployment Auto-Fix" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Resource Group: $ResourceGroup" -ForegroundColor DarkGray
Write-Host "  Project: $Project" -ForegroundColor DarkGray
Write-Host "  Environment: $Env" -ForegroundColor DarkGray
Write-Host "  Prefix: $prefix" -ForegroundColor DarkGray
Write-Host "  Cosmos Account: $cosmosAccount" -ForegroundColor DarkGray
Write-Host "  Namespace: $Namespace" -ForegroundColor DarkGray
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

$fixesApplied = @()
$fixesFailed = @()

# ============================================================================
# 1. FIX COSMOS DB CREDENTIALS
# ============================================================================
Write-Host "1️⃣  Fixing Cosmos DB credentials..." -ForegroundColor Yellow

try {
    # Retrieve actual Cosmos DB credentials
    Write-Host "   Retrieving Cosmos DB endpoint..." -ForegroundColor DarkGray
    $cosmosEndpoint = az cosmosdb show -n $cosmosAccount -g $ResourceGroup --query documentEndpoint -o tsv 2>&1

    if ($LASTEXITCODE -ne 0 -or -not $cosmosEndpoint) {
        $fixesFailed += "❌ Failed to retrieve Cosmos DB endpoint. Account '$cosmosAccount' may not exist."
        Write-Error "Cannot retrieve Cosmos DB endpoint"
        return
    }

    Write-Host "   Retrieved endpoint: $cosmosEndpoint" -ForegroundColor DarkGray

    Write-Host "   Retrieving Cosmos DB primary key..." -ForegroundColor DarkGray
    $cosmosKey = az cosmosdb keys list -n $cosmosAccount -g $ResourceGroup --query primaryMasterKey -o tsv 2>&1

    if ($LASTEXITCODE -ne 0 -or -not $cosmosKey) {
        $fixesFailed += "❌ Failed to retrieve Cosmos DB primary key"
        Write-Error "Cannot retrieve Cosmos DB key"
        return
    }

    Write-Host "   Retrieved key (length: $($cosmosKey.Length) chars)" -ForegroundColor DarkGray

    # Check if secret exists and contains placeholders
    $needsSecretRecreation = $false
    $secretExists = $false

    try {
        $secretJson = kubectl get secret cosmos-statestore -n $Namespace -o json 2>&1
        if ($LASTEXITCODE -eq 0) {
            $secretExists = $true
            $secret = $secretJson | ConvertFrom-Json

            # Decode current values - key may not exist for Managed Identity secrets
            $currentEndpoint = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($secret.data.endpoint))
            $currentKey = if ($secret.data.key) { [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($secret.data.key)) } else { '' }

            # Check for placeholders
            $placeholderPatterns = @('REPLACE_', 'TODO', 'CHANGEME', 'PLACEHOLDER', 'YOUR_', '<', '>')
            $hasPlaceholder = $false

            foreach ($pattern in $placeholderPatterns) {
                if ($currentEndpoint -match $pattern -or ($currentKey -and $currentKey -match $pattern)) {
                    $hasPlaceholder = $true
                    Write-Host "   ⚠️  Detected placeholder pattern: $pattern" -ForegroundColor Yellow
                    break
                }
            }

            # Check if values match expected
            if ($currentEndpoint -ne $cosmosEndpoint -or $currentKey -ne $cosmosKey -or $hasPlaceholder) {
                $needsSecretRecreation = $true
                Write-Host "   📝 Secret needs update (mismatch or placeholder detected)" -ForegroundColor Yellow
            } else {
                Write-Host "   ✅ Secret already contains correct credentials" -ForegroundColor Green
            }
        }
    } catch {
        Write-Host "   ℹ️  Secret does not exist yet" -ForegroundColor DarkGray
        $needsSecretRecreation = $true
    }

    # Recreate secret if needed
    if ($needsSecretRecreation) {
        Write-Host "   Recreating cosmos-statestore secret with actual credentials..." -ForegroundColor Yellow

        if ($secretExists) {
            kubectl delete secret cosmos-statestore -n $Namespace 2>$null | Out-Null
            Write-Host "   Deleted old secret" -ForegroundColor DarkGray
        }

        kubectl create secret generic cosmos-statestore -n $Namespace `
            --from-literal=endpoint="$cosmosEndpoint" `
            --from-literal=key="$cosmosKey" 2>&1 | Out-Null

        if ($LASTEXITCODE -eq 0) {
            $fixesApplied += "✅ Created cosmos-statestore secret with actual credentials"
            Write-Host "   ✅ Secret created successfully" -ForegroundColor Green
        } else {
            $fixesFailed += "❌ Failed to create cosmos-statestore secret"
        }
    }

} catch {
    $fixesFailed += "❌ Failed to fix Cosmos DB credentials: $($_.Exception.Message)"
}

# ============================================================================
# 2. FIX DAPR COMPONENT CONFIGURATION
# ============================================================================
Write-Host ""
Write-Host "2️⃣  Fixing Dapr component configuration..." -ForegroundColor Yellow

$expectedDb = "elves_demo"
$expectedContainer = "wishlists"
$componentNeedsPatch = $false
$patchOperations = @()

try {
    $componentJson = kubectl get component cosmos-state -n $Namespace -o json 2>&1

    if ($LASTEXITCODE -eq 0) {
        $component = $componentJson | ConvertFrom-Json

        # Build metadata map
        $metadataMap = @{}
        $metadataIndex = 0
        foreach ($item in $component.spec.metadata) {
            $metadataMap[$item.name] = @{
                'value' = $item.value
                'index' = $metadataIndex
            }
            $metadataIndex++
        }

        # Check database name
        if ($metadataMap.ContainsKey('database')) {
            $currentDb = $metadataMap['database'].value
            $dbIndex = $metadataMap['database'].index

            if ($currentDb -ne $expectedDb) {
                Write-Host "   ⚠️  Database mismatch: '$currentDb' should be '$expectedDb'" -ForegroundColor Yellow
                $patchOperations += @{
                    op = "replace"
                    path = "/spec/metadata/$dbIndex/value"
                    value = $expectedDb
                }
                $componentNeedsPatch = $true
            } else {
                Write-Host "   ✅ Database name correct: $expectedDb" -ForegroundColor Green
            }
        }

        # Check container name (Cosmos uses 'collection' in Dapr component)
        if ($metadataMap.ContainsKey('collection')) {
            $currentContainer = $metadataMap['collection'].value
            $containerIndex = $metadataMap['collection'].index

            if ($currentContainer -ne $expectedContainer) {
                Write-Host "   ⚠️  Container mismatch: '$currentContainer' should be '$expectedContainer'" -ForegroundColor Yellow
                $patchOperations += @{
                    op = "replace"
                    path = "/spec/metadata/$containerIndex/value"
                    value = $expectedContainer
                }
                $componentNeedsPatch = $true
            } else {
                Write-Host "   ✅ Container name correct: $expectedContainer" -ForegroundColor Green
            }
        }

        # Apply patches if needed
        if ($componentNeedsPatch -and $patchOperations.Count -gt 0) {
            Write-Host "   Patching component with correct database/container names..." -ForegroundColor Yellow

            $patchJson = $patchOperations | ConvertTo-Json -Compress -Depth 10
            kubectl patch component cosmos-state -n $Namespace --type='json' -p $patchJson 2>&1 | Out-Null

            if ($LASTEXITCODE -eq 0) {
                $fixesApplied += "✅ Patched cosmos-state component (database: $expectedDb, container: $expectedContainer)"
                Write-Host "   ✅ Component patched successfully" -ForegroundColor Green
            } else {
                $fixesFailed += "❌ Failed to patch cosmos-state component"
            }
        }

    } else {
        Write-Host "   ℹ️  Component does not exist yet (will be created by deployment)" -ForegroundColor DarkGray
    }

} catch {
    $fixesFailed += "❌ Failed to fix Dapr component: $($_.Exception.Message)"
}

# ============================================================================
# 3. VERIFY COSMOS DB DATABASE/CONTAINER EXIST
# ============================================================================
Write-Host ""
Write-Host "3️⃣  Verifying Cosmos DB database and container..." -ForegroundColor Yellow

try {
    # Check database
    # Use explicit parameter names to align with latest Azure CLI
    $databasesJson = az cosmosdb sql database show --account-name $cosmosAccount --resource-group $ResourceGroup --name $expectedDb 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Host "   ✅ Database '$expectedDb' exists" -ForegroundColor Green

        # Check container
        $containerJson = az cosmosdb sql container show --account-name $cosmosAccount --resource-group $ResourceGroup --database-name $expectedDb --name $expectedContainer 2>&1

        if ($LASTEXITCODE -eq 0) {
            Write-Host "   ✅ Container '$expectedContainer' exists" -ForegroundColor Green
        } else {
            $fixesFailed += "❌ Container '$expectedContainer' not found. Run Bicep deployment to create infrastructure."
        }
    } else {
        $fixesFailed += "❌ Database '$expectedDb' not found. Run Bicep deployment to create infrastructure."
    }

} catch {
    $fixesFailed += "❌ Failed to verify Cosmos DB resources: $($_.Exception.Message)"
}

# ============================================================================
# 4. RESTART DRASI PODS (IF FIXES APPLIED)
# ============================================================================
if ($fixesApplied.Count -gt 0 -and -not $SkipRestart) {
    Write-Host ""
    Write-Host "4️⃣  Restarting Drasi pods to pick up configuration changes..." -ForegroundColor Yellow

    try {
        # Restart deployments
        Write-Host "   Triggering rollout restart..." -ForegroundColor DarkGray
        kubectl rollout restart deployment -n $Namespace drasi-api drasi-resource-provider 2>&1 | Out-Null

        if ($LASTEXITCODE -eq 0) {
            Write-Host "   Waiting for pods to stabilize (45 seconds)..." -ForegroundColor DarkGray
            Start-Sleep -Seconds 45

            # Check pod status
            Write-Host "   Checking pod status..." -ForegroundColor DarkGray
            $podsJson = kubectl get pods -n $Namespace -o json 2>&1

            if ($LASTEXITCODE -eq 0) {
                $pods = ($podsJson | ConvertFrom-Json).items
                $allHealthy = $true

                foreach ($pod in $pods) {
                    $podName = $pod.metadata.name
                    $containerStatuses = $pod.status.containerStatuses

                    if ($containerStatuses) {
                        $readyContainers = ($containerStatuses | Where-Object { $_.ready -eq $true }).Count
                        $totalContainers = $containerStatuses.Count

                        if ($readyContainers -eq $totalContainers) {
                            Write-Host "   ✅ Pod $podName healthy ($readyContainers/$totalContainers ready)" -ForegroundColor Green
                        } else {
                            Write-Host "   ⚠️  Pod $podName not fully ready ($readyContainers/$totalContainers ready)" -ForegroundColor Yellow
                            $allHealthy = $false

                            # Check for errors
                            foreach ($status in $containerStatuses) {
                                if ($status.state.waiting) {
                                    $reason = $status.state.waiting.reason
                                    Write-Host "      Container $($status.name): $reason" -ForegroundColor Yellow
                                }
                            }
                        }
                    }
                }

                if ($allHealthy) {
                    $fixesApplied += "✅ All pods restarted and healthy"
                } else {
                    $fixesFailed += "⚠️  Some pods not fully ready after restart. Check: kubectl logs -n $Namespace <pod-name>"
                }
            }
        } else {
            $fixesFailed += "❌ Failed to restart deployments"
        }

    } catch {
        $fixesFailed += "❌ Failed to restart pods: $($_.Exception.Message)"
    }
} elseif ($SkipRestart) {
    Write-Host ""
    Write-Host "4️⃣  Skipping pod restart (use -SkipRestart:$false to enable)" -ForegroundColor DarkGray
}

# ============================================================================
# SUMMARY
# ============================================================================
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  📊 Fix Summary" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan

if ($fixesApplied.Count -eq 0 -and $fixesFailed.Count -eq 0) {
    Write-Host ""
    Write-Host "  ℹ️  No fixes needed - configuration already correct" -ForegroundColor Cyan
    Write-Host ""
    exit 0
}

if ($fixesApplied.Count -gt 0) {
    Write-Host ""
    Write-Host "  ✅ FIXES APPLIED ($($fixesApplied.Count)):" -ForegroundColor Green
    foreach ($fix in $fixesApplied) {
        Write-Host "     $fix" -ForegroundColor Green
    }
}

if ($fixesFailed.Count -gt 0) {
    Write-Host ""
    Write-Host "  ❌ FIXES FAILED ($($fixesFailed.Count)):" -ForegroundColor Red
    foreach ($failure in $fixesFailed) {
        Write-Host "     $failure" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "  💡 Manual intervention may be required" -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

Write-Host ""
Write-Host "  🎉 All fixes applied successfully!" -ForegroundColor Green
Write-Host "  🔍 Run validation to confirm: .\scripts\validate-drasi-deployment.ps1 -ResourceGroup $ResourceGroup -Project $Project -Env $Env" -ForegroundColor Cyan
Write-Host ""
exit 0
