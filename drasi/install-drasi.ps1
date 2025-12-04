<#
.SYNOPSIS
    Installs Drasi using the Drasi CLI (recommended) with fallback to manifest-based installation.
.DESCRIPTION
    The Drasi CLI is the single source of truth for Drasi installation. This script:
    1. Checks if Drasi CLI is available
    2. Uses 'drasi init' for installation (recommended)
    3. Falls back to manifest-based installation if CLI is not available

    The CLI handles Dapr installation, CRD creation, and control plane deployment automatically.
.NOTES
    Environment variables:
    - DRASI_VERSION: Specify Drasi version (default: latest)
    - DRASI_REGISTRY: Container registry (default: ghcr.io)
    - DRASI_NAMESPACE: Kubernetes namespace (default: drasi-system)
    - DRASI_USE_MANIFESTS: Set to '1' to force manifest-based installation
#>

Write-Host "[Drasi] Predeploy starting" -ForegroundColor Cyan

$ErrorActionPreference = "Stop"
$drasiNs = if ($Env:DRASI_NAMESPACE) { $Env:DRASI_NAMESPACE } else { "drasi-system" }
$drasiVersion = $Env:DRASI_VERSION
$drasiRegistry = if ($Env:DRASI_REGISTRY) { $Env:DRASI_REGISTRY } else { "ghcr.io" }

# Check if Drasi CLI is available
function Test-DrasiCli {
    try {
        $version = drasi version 2>$null
        return ($LASTEXITCODE -eq 0)
    }
    catch {
        return $false
    }
}

# Ensure core Kubernetes infra (Mongo/Redis/Dapr config/components) exists and is ready
function Ensure-DrasiKubeInfra {
    param([string]$Namespace = "drasi-system")

    try {
        $k8sResourcesPath = Join-Path $PSScriptRoot "manifests" "kubernetes-resources.yaml"
        if (-not (Test-Path $k8sResourcesPath)) { return }

        Write-Host "📦 Applying Drasi Kubernetes infrastructure (Mongo/Redis/Dapr components)..." -ForegroundColor Cyan
        kubectl apply -f $k8sResourcesPath | Out-Null

        # Wait for Mongo statefulset pod to be Ready
        Write-Host "   ⏳ Waiting for Mongo pod to be Ready..." -ForegroundColor Yellow
        kubectl rollout status statefulset/drasi-mongo -n $Namespace --timeout=180s 2>$null | Out-Null

        # Validate that the Mongo replica set is initialized (rs0)
        Wait-MongoReplicaSetReady -Namespace $Namespace -TimeoutSeconds 120 | Out-Null

        # Apply Dapr configuration if present
        $daprConfigPath = Join-Path $PSScriptRoot "dapr-config.yaml"
        if (Test-Path $daprConfigPath) {
            Write-Host "   Applying Dapr configuration..." -ForegroundColor DarkGray
            kubectl apply -f $daprConfigPath 2>$null | Out-Null
        }
    }
    catch {
        Write-Warning "Failed to ensure Kubernetes infra: $($_.Exception.Message)"
    }
}

# Wait until the single-node Mongo replicaset reports ok==1
function Wait-MongoReplicaSetReady {
    param(
        [string]$Namespace = "drasi-system",
        [int]$TimeoutSeconds = 120
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $podName = "drasi-mongo-0"
    Write-Host "   ⏳ Ensuring Mongo replica set (rs0) is initialized..." -ForegroundColor Yellow
    while ((Get-Date) -lt $deadline) {
        try {
            # Use mongosh if available; fallback to mongo
            $cmd = @('mongosh', '--quiet', '--eval', 'JSON.stringify(rs.status().ok)')
            $res = kubectl exec -n $Namespace $podName -- $cmd 2>$null
            if (-not $res) {
                $cmd = @('mongo', '--quiet', '--eval', 'JSON.stringify(rs.status().ok)')
                $res = kubectl exec -n $Namespace $podName -- $cmd 2>$null
            }
            if ($LASTEXITCODE -eq 0 -and $res -match '1') {
                Write-Host "   ✅ Mongo replica set is ready (rs0)" -ForegroundColor Green
                return $true
            }
        }
        catch {}
        Start-Sleep -Seconds 5
    }

    # Best-effort attempt to initiate RS if not already
    try {
        Write-Host "   ⚠️ Attempting to initiate replica set (best-effort)..." -ForegroundColor Yellow
        $host = "drasi-mongo-0.drasi-mongo.$Namespace.svc.cluster.local:27017"
        $initJs = "rs.initiate({_id: 'rs0', members: [{ _id: 0, host: '$host'}]})"
        kubectl exec -n $Namespace $podName -- mongosh --quiet --eval "$initJs" 2>$null | Out-Null
    }
    catch {}

    return $false
}

# Install Drasi using CLI (recommended method)
function Install-DrasiWithCli {
    Write-Host "🚀 Installing Drasi using CLI (recommended method)..." -ForegroundColor Cyan

    $initArgs = @("-n", $drasiNs, "--registry", $drasiRegistry)

    if (-not [string]::IsNullOrWhiteSpace($drasiVersion)) {
        $initArgs += @("--version", $drasiVersion)
        Write-Host "   Version: $drasiVersion" -ForegroundColor DarkGray
    }

    Write-Host "   Namespace: $drasiNs" -ForegroundColor DarkGray
    Write-Host "   Registry: $drasiRegistry" -ForegroundColor DarkGray

    # Ensure Kubernetes infra (Mongo/Redis/Dapr components) is present and healthy BEFORE drasi init
    # This guarantees Dapr sidecars can load 'drasi-state' without timing out on first boot
    Ensure-DrasiKubeInfra -Namespace $drasiNs

    # Run drasi init - this handles Dapr, CRDs, and control plane
    Write-Host "   Running: drasi init $($initArgs -join ' ')" -ForegroundColor DarkGray
    drasi init @initArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Warning "drasi init failed with exit code $LASTEXITCODE"
        return $false
    }

    # Configure CLI environment for subsequent commands
    drasi env kube -n $drasiNs 2>$null

    # Wait for Drasi API deployment to be available before proceeding
    # Note: This is a simpler check than Wait-DrasiApiReady in apply-drasi-resources.ps1
    # Here we just wait for the deployment to be "available" which is sufficient after drasi init.
    # The more thorough container-level readiness check runs before drasi apply.
    Write-Host "   ⏳ Waiting for Drasi API deployment to be available..." -ForegroundColor Yellow
    $maxWait = 180
    $elapsed = 0
    $apiReady = $false
    while ($elapsed -lt $maxWait) {
        try {
            $deployStatus = kubectl get deployment drasi-api -n $drasiNs -o json 2>$null
            if ($LASTEXITCODE -eq 0 -and $deployStatus) {
                $deploy = $deployStatus | ConvertFrom-Json
                $availableReplicas = if ($deploy.status.availableReplicas) { $deploy.status.availableReplicas } else { 0 }
                $desiredReplicas = if ($deploy.spec.replicas) { $deploy.spec.replicas } else { 1 }
                if ($availableReplicas -ge $desiredReplicas) {
                    Write-Host "   ✅ Drasi API deployment is available ($availableReplicas replica(s))" -ForegroundColor Green
                    $apiReady = $true
                    break
                }
            }
        }
        catch {}
        Start-Sleep -Seconds 10
        $elapsed += 10
        $remaining = $maxWait - $elapsed
        Write-Host "   Waiting for Drasi API... (${remaining}s remaining)" -ForegroundColor DarkGray
    }
    if (-not $apiReady) {
        Write-Warning "   ⚠️ Drasi API not ready after ${maxWait}s, but continuing"
    }

    # Ensure drasi-sa ServiceAccount exists (drasi init may not create it)
    $saExists = kubectl get sa drasi-sa -n $drasiNs -o name 2>$null
    if (-not $saExists) {
        Write-Host "   Creating drasi-sa ServiceAccount..." -ForegroundColor Yellow
        kubectl create sa drasi-sa -n $drasiNs 2>$null
        kubectl label sa drasi-sa -n $drasiNs app=drasi 2>$null
        kubectl annotate sa drasi-sa -n $drasiNs azure.workload.identity/use=true 2>$null
    }

    Write-Host "✅ Drasi installed successfully via CLI" -ForegroundColor Green
    return $true
}

# Fallback: Install using manifest-based approach
function Install-DrasiWithManifests {
    Write-Host "📦 Installing Drasi using manifests (fallback method)..." -ForegroundColor Yellow
    Write-Host "   ⚠️  Consider installing the Drasi CLI for the recommended installation experience" -ForegroundColor Yellow
    Write-Host "   See: https://drasi.io/docs/getting-started/installation/" -ForegroundColor DarkGray

    # Install Dapr first (required by manifests)
    # IMPORTANT: Use Dapr 1.14.5 to match Drasi's default sidecar version
    Write-Host "   Validating Dapr installation..." -ForegroundColor DarkGray
    helm repo add dapr https://dapr.github.io/helm-charts 2>$null | Out-Null
    helm repo update 2>$null | Out-Null

    if (-not (kubectl get ns dapr-system -o name 2>$null)) {
        Write-Host "   Installing Dapr 1.14.5 via Helm (matches Drasi requirements)..." -ForegroundColor Cyan
        helm install dapr dapr/dapr --version 1.14.5 --namespace dapr-system --create-namespace `
            --set global.registry=docker.io/daprio --set dapr_operator.watchInterval=10s
    }
    else {
        Write-Host "   ✅ Dapr already installed" -ForegroundColor Green
    }

    # Apply Dapr configuration (disables mTLS for compatibility)
    $daprConfigPath = Join-Path $PSScriptRoot "dapr-config.yaml"
    if (Test-Path $daprConfigPath) {
        Write-Host "   Applying Dapr configuration..." -ForegroundColor DarkGray
        kubectl apply -f $daprConfigPath 2>$null | Out-Null
    }

    # Apply Drasi infrastructure manifests
    $infraManifestPath = Join-Path $PSScriptRoot "manifests" "02-drasi-infra.yaml"
    if (Test-Path $infraManifestPath) {
        Write-Host "   Applying Drasi infrastructure manifests..." -ForegroundColor Cyan
        kubectl apply -f $infraManifestPath
    }

    # Apply kubernetes-resources.yaml from drasi/manifests (canonical location)
    $k8sResourcesPath = Join-Path $PSScriptRoot "manifests" "kubernetes-resources.yaml"
    if (Test-Path $k8sResourcesPath) {
        Write-Host "   Applying Drasi Kubernetes resources..." -ForegroundColor Cyan
        kubectl apply -f $k8sResourcesPath

        # Ensure drasi-sa ServiceAccount exists (critical for view-service)
        $saExists = kubectl get sa drasi-sa -n $drasiNs -o name 2>$null
        if (-not $saExists) {
            Write-Host "   Creating drasi-sa ServiceAccount..." -ForegroundColor Yellow
            kubectl create sa drasi-sa -n $drasiNs 2>$null
            kubectl label sa drasi-sa -n $drasiNs app=drasi 2>$null
            kubectl annotate sa drasi-sa -n $drasiNs azure.workload.identity/use=true 2>$null
        }

        # Wait for Drasi API to be ready
        Write-Host "   Waiting for Drasi API deployment..." -ForegroundColor DarkGray
        kubectl wait --for=condition=available --timeout=300s deployment/drasi-api -n $drasiNs 2>$null
    }

    Write-Host "✅ Drasi infrastructure installed via manifests" -ForegroundColor Green
    return $true
}

# Main installation logic
$useCli = -not ($Env:DRASI_USE_MANIFESTS -eq '1')

if ($useCli -and (Test-DrasiCli)) {
    Write-Host "✅ Drasi CLI detected" -ForegroundColor Green

    # Check if Drasi is already installed
    $existingPods = kubectl get pods -n $drasiNs -l "drasi/infra=api" --no-headers 2>$null
    if ($existingPods) {
        Write-Host "   Drasi already installed in namespace $drasiNs" -ForegroundColor DarkGray
        Write-Host "   Skipping full reinstall (use 'drasi init' manually to reinstall)" -ForegroundColor DarkGray

        # Just ensure CLI environment is configured
        drasi env kube -n $drasiNs 2>$null
    }
    else {
        $success = Install-DrasiWithCli
        if (-not $success) {
            Write-Warning "CLI installation failed, falling back to manifests..."
            Install-DrasiWithManifests
        }
    }
}
else {
    if ($Env:DRASI_USE_MANIFESTS -eq '1') {
        Write-Host "ℹ️  DRASI_USE_MANIFESTS=1 set, using manifest-based installation" -ForegroundColor Yellow
    }
    else {
        Write-Host "⚠️  Drasi CLI not found in PATH" -ForegroundColor Yellow
    }
    Install-DrasiWithManifests
}

Write-Host "[Drasi] Predeploy complete" -ForegroundColor Green
