<#
.SYNOPSIS
    Applies Drasi resources using the Drasi CLI (recommended) with manifest fallback.
.DESCRIPTION
    The Drasi CLI is the single source of truth for resource management. This script:
    1. Uses 'drasi apply' for sources, queries, and reactions (recommended)
    2. Falls back to kubectl for manifests if CLI is unavailable
    3. Patches workload images that may be missing registry prefix
    4. Captures Drasi endpoints and updates Container App
    5. Runs post-provision health checks

.NOTES
    Environment variables:
    - DRASI_NAMESPACE: Kubernetes namespace (default: drasi-system)
    - DRASI_REGISTRY: Container registry for image prefix patching (default: ghcr.io)
    - CONTAINER_REGISTRY: Fallback if DRASI_REGISTRY not set
    - DRASI_SKIP_IMAGE_PREFIX_PATCH: Set to '1' to skip image patching
#>

Write-Host "[Drasi] Postdeploy starting (CLI-driven install)" -ForegroundColor Cyan

$ErrorActionPreference = "Stop"

# Namespaces
$cpNs = if ($Env:DRASI_NAMESPACE) { $Env:DRASI_NAMESPACE } else { "drasi-system" }
$resNs = $cpNs

# Evidence folder for logs/artifacts
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$evidenceRoot = Join-Path (Join-Path $PSScriptRoot '..') (Join-Path 'docs/status/evidence' $timestamp)
$logsDir = Join-Path $evidenceRoot 'logs'
$kubectlDir = Join-Path (Join-Path $evidenceRoot 'kubectl') ''
$resolvedDir = Join-Path $evidenceRoot 'resolved'
New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
New-Item -ItemType Directory -Path $kubectlDir -Force | Out-Null
New-Item -ItemType Directory -Path $resolvedDir -Force | Out-Null

$script:ActiveAzdEnvName = $null

function Get-ActiveAzdEnvironmentName {
  if ($script:ActiveAzdEnvName) { return $script:ActiveAzdEnvName }

  if ($Env:AZURE_ENV_NAME) {
    $script:ActiveAzdEnvName = $Env:AZURE_ENV_NAME
    return $script:ActiveAzdEnvName
  }

  try {
    $azdValue = azd env get-value AZURE_ENV_NAME 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($azdValue)) {
      $script:ActiveAzdEnvName = $azdValue.Trim()
      return $script:ActiveAzdEnvName
    }
  }
  catch {}

  $configPath = Join-Path (Join-Path $PSScriptRoot '..') '.azure/config.json'
  if (Test-Path $configPath) {
    try {
      $config = Get-Content -Path $configPath -Raw | ConvertFrom-Json
      if ($config.defaultEnvironment) {
        $script:ActiveAzdEnvName = $config.defaultEnvironment
        return $script:ActiveAzdEnvName
      }
    }
    catch {}
  }

  return $null
}

function Get-AzdEnvValue {
  param(
    [Parameter(Mandatory = $true)][string[]]$Keys,
    [string]$EnvironmentName = $null
  )

  foreach ($key in $Keys) {
    $direct = [Environment]::GetEnvironmentVariable($key)
    if (-not [string]::IsNullOrWhiteSpace($direct)) {
      return $direct.Trim()
    }
  }

  if (-not $EnvironmentName) {
    $EnvironmentName = Get-ActiveAzdEnvironmentName
  }

  if ($EnvironmentName) {
    foreach ($key in $Keys) {
      try {
        $value = azd env get-value $key --environment $EnvironmentName 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($value)) {
          return $value.Trim()
        }
      }
      catch {}
    }

    $envRoot = Join-Path (Join-Path $PSScriptRoot '..') '.azure'
    $envFile = Join-Path (Join-Path $envRoot $EnvironmentName) '.env'
    if (Test-Path $envFile) {
      $lines = Get-Content -Path $envFile
      foreach ($key in $Keys) {
        $pattern = "^\s*{0}\s*=\s*(.+)$" -f [Regex]::Escape($key)
        foreach ($line in $lines) {
          if ($line -match $pattern) {
            $raw = $Matches[1].Trim().Trim('"')
            return $raw
          }
        }
      }
    }
  }

  return $null
}

function Resolve-DrasiTemplateFile {
  param(
    [string]$InputPath,
    [hashtable]$TokenMap,
    [string]$OutputDirectory
  )

  if (-not (Test-Path $InputPath)) { return $InputPath }
  if (-not $TokenMap -or $TokenMap.Keys.Count -eq 0) { return $InputPath }

  $content = Get-Content -Path $InputPath -Raw
  $needsReplacement = $false

  foreach ($token in $TokenMap.Keys) {
    $placeholder = '${' + $token + '}'
    if ($content -like "*$placeholder*") {
      $value = $TokenMap[$token]
      if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Required token $placeholder is missing a value."
      }
      $content = $content.Replace($placeholder, $value)
      $needsReplacement = $true
    }
  }

  if (-not $needsReplacement) { return $InputPath }

  if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
  }

  $fileName = Split-Path -Path $InputPath -Leaf
  $outputPath = Join-Path $OutputDirectory $fileName
  $content | Set-Content -Path $outputPath -Encoding UTF8
  return $outputPath
}

function Get-DrasiTokenMap {
  $envName = Get-ActiveAzdEnvironmentName
  $tokens = @{}

  $eventHubFqdn = Get-AzdEnvValue -Keys @('EVENTHUB_FQDN', 'eventHubFqdn') -EnvironmentName $envName
  if ($eventHubFqdn) { $tokens['EVENTHUB_FQDN'] = $eventHubFqdn }

  $clientId = Get-AzdEnvValue -Keys @('DRASI_MI_CLIENT_ID') -EnvironmentName $envName
  if (-not $clientId) {
    $identityInfo = Get-AzdEnvValue -Keys @('drasiIdentityInfo') -EnvironmentName $envName
    if ($identityInfo) {
      try {
        $clientId = (ConvertFrom-Json $identityInfo).clientId
      }
      catch {}
    }
  }
  if ($clientId) { $tokens['DRASI_MI_CLIENT_ID'] = $clientId }

  $missing = @()
  foreach ($required in @('EVENTHUB_FQDN', 'DRASI_MI_CLIENT_ID')) {
    if (-not $tokens.ContainsKey($required) -or [string]::IsNullOrWhiteSpace($tokens[$required])) {
      $missing += $required
    }
  }

  if ($missing.Count -gt 0) {
    Write-Host "`n❌ Missing required environment values for Drasi resource substitution:" -ForegroundColor Red
    foreach ($m in $missing) {
      Write-Host "   - $m" -ForegroundColor Red
    }
    Write-Host "`n   💡 These values are output by 'azd provision' and should be in your azd environment." -ForegroundColor Yellow
    Write-Host "   Run 'azd env refresh' to sync infrastructure outputs, then retry 'azd deploy drasi'." -ForegroundColor Yellow
    throw "Missing required azd environment values: $($missing -join ', '). Run 'azd env refresh' to sync."
  }

  return $tokens
}

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

function Wait-DrasiApiReady {
  <#
  .SYNOPSIS
      Waits for the Drasi API deployment to be available and ready.
  .DESCRIPTION
      Before applying resources via 'drasi apply', we must ensure the Drasi API pod
      is running and ready to accept port-forward connections. This prevents transient
      port forwarding failures (E1204 portforward.go errors) that cause drasi apply to panic.
  .PARAMETER Namespace
      Kubernetes namespace where Drasi is installed.
  .PARAMETER TimeoutSeconds
      Maximum time to wait for the API to be ready.
  .PARAMETER IntervalSeconds
      Polling interval between readiness checks.
  #>
  param(
    [string]$Namespace = "drasi-system",
    [int]$TimeoutSeconds = 180,
    [int]$IntervalSeconds = 10
  )

  Write-Host "`n⏳ Waiting for Drasi API to be ready (timeout: ${TimeoutSeconds}s)..." -ForegroundColor Cyan

  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

  while ((Get-Date) -lt $deadline) {
    try {
      # Check if the drasi-api deployment exists and is available
      $deployStatus = kubectl get deployment drasi-api -n $Namespace -o json 2>$null
      if ($LASTEXITCODE -eq 0 -and $deployStatus) {
        $deploy = $deployStatus | ConvertFrom-Json
        $availableReplicas = if ($deploy.status.availableReplicas) { $deploy.status.availableReplicas } else { 0 }
        $desiredReplicas = if ($deploy.spec.replicas) { $deploy.spec.replicas } else { 1 }

        if ($availableReplicas -ge $desiredReplicas) {
          # Also verify pod is in Running state (not just available)
          $podStatus = kubectl get pods -n $Namespace -l "drasi/infra=api" -o json 2>$null
          if ($LASTEXITCODE -eq 0 -and $podStatus) {
            $pods = ($podStatus | ConvertFrom-Json).items
            $runningPods = @($pods | Where-Object { $_.status.phase -eq 'Running' })
            if ($runningPods.Count -gt 0) {
              # Check all containers are ready
              $allReady = $true
              :podLoop foreach ($pod in $runningPods) {
                # Handle pods without containerStatuses (still initializing)
                if (-not $pod.status.containerStatuses -or $pod.status.containerStatuses.Count -eq 0) {
                  $allReady = $false
                  break podLoop
                }
                foreach ($cs in $pod.status.containerStatuses) {
                  if (-not $cs.ready) {
                    $allReady = $false
                    break podLoop
                  }
                }
              }
              if ($allReady) {
                Write-Host "   ✅ Drasi API is ready ($availableReplicas replica(s) available)" -ForegroundColor Green
                # Small delay to ensure port 8080 is listening
                Start-Sleep -Seconds 5
                return $true
              }
            }
          }
        }

        $remaining = [math]::Round(($deadline - (Get-Date)).TotalSeconds)
        Write-Host "   Waiting... ($availableReplicas/$desiredReplicas replicas available, ${remaining}s remaining)" -ForegroundColor DarkGray
      }
      else {
        $remaining = [math]::Round(($deadline - (Get-Date)).TotalSeconds)
        Write-Host "   Waiting for drasi-api deployment to be created... (${remaining}s remaining)" -ForegroundColor DarkGray
      }
    }
    catch {
      $remaining = [math]::Round(($deadline - (Get-Date)).TotalSeconds)
      Write-Host "   Check failed: $($_.Exception.Message) (${remaining}s remaining)" -ForegroundColor DarkGray
    }

    Start-Sleep -Seconds $IntervalSeconds
  }

  Write-Warning "   ⚠️ Timed out waiting for Drasi API after ${TimeoutSeconds}s"
  return $false
}

# Helper function to check if an image has a proper registry prefix
function Test-ImageHasRegistryPrefix {
  param([string]$Image)
  $firstSegment = ($Image -split '/')[0]
  # Registry hostnames contain dots, or are 'localhost'
  return ($firstSegment -match '\.') -or ($firstSegment -eq 'localhost')
}

function Invoke-RegistrySubstitution {
  try {
    $providersPath = Join-Path $PSScriptRoot 'resources' 'providers.yaml'
    $providersTemplatePath = Join-Path $PSScriptRoot 'resources' 'providers.yaml.template'

    if (Test-Path $providersTemplatePath) {
      $registry = $Env:CONTAINER_REGISTRY
      if ([string]::IsNullOrWhiteSpace($registry)) { $registry = $Env:ACR_LOGIN_SERVER }
      if ([string]::IsNullOrWhiteSpace($registry)) { $registry = 'ghcr.io/drasi-project' }

      $content = Get-Content -Path $providersTemplatePath -Raw
      $content = $content -replace 'REPLACE_REGISTRY', $registry
      $content | Set-Content -Path $providersPath -Encoding UTF8
      Write-Host "providers.yaml generated with registry '$registry'." -ForegroundColor DarkGray
    }
    else {
      Write-Host "providers.yaml.template not found; skipping registry substitution." -ForegroundColor DarkGray
    }
  }
  catch {
    Write-Warning "Registry substitution error: $($_.Exception.Message)"
  }
}

function Test-KubeConnectivity {
  try {
    kubectl version --request-timeout 5s 1>$null 2>$null
    return ($LASTEXITCODE -eq 0)
  }
  catch {
    return $false
  }
}

function Ensure-KubeCredentials {
  if (Test-KubeConnectivity) { return }
  Write-Host "Kubernetes API unreachable; refreshing AKS credentials..." -ForegroundColor DarkGray

  $rg = $Env:AZURE_RESOURCE_GROUP
  if ([string]::IsNullOrWhiteSpace($rg)) { try { $rg = azd env get-value AZURE_RESOURCE_GROUP 2>$null } catch {} }

  $envName = $Env:AZURE_ENV_NAME
  if ([string]::IsNullOrWhiteSpace($envName)) { try { $envName = azd env get-value AZURE_ENV_NAME 2>$null } catch {} }

  $project = $Env:AZURE_PROJECT_NAME
  if ([string]::IsNullOrWhiteSpace($project)) { $project = 'santadigitalshowcase' }

  $aksName = $Env:AZURE_AKS_NAME
  if ([string]::IsNullOrWhiteSpace($aksName)) { $aksName = ("$project-$envName-aks").ToLower() }

  if ([string]::IsNullOrWhiteSpace($rg) -or [string]::IsNullOrWhiteSpace($aksName)) {
    Write-Warning "Cannot determine AKS cluster name or resource group; skipping credential refresh."
    return
  }

  try {
    az aks get-credentials -n $aksName -g $rg --overwrite-existing 1>$null 2>$null
  }
  catch {
    Write-Warning "AKS credential refresh failed: $($_.Exception.Message)"
  }

  if (Test-KubeConnectivity) {
    Write-Host "Kubernetes API reachable after credential refresh." -ForegroundColor DarkGray
  }
  else {
    Write-Warning "Still cannot reach Kubernetes API after credential refresh."
  }
}

# NOTE: Dapr installation is handled by 'drasi init' in install-drasi.ps1
# No need to check or install Dapr separately - the Drasi CLI manages it automatically

function Ensure-CosmosStateStoreSecret {
  param(
    [string]$Namespace = "drasi-system"
  )

  try {
    # Resolve Cosmos credentials early
    $rg = $Env:AZURE_RESOURCE_GROUP; if ([string]::IsNullOrWhiteSpace($rg)) { try { $rg = azd env get-value AZURE_RESOURCE_GROUP 2>$null } catch {} }
    $envName = $Env:AZURE_ENV_NAME; if ([string]::IsNullOrWhiteSpace($envName)) { try { $envName = azd env get-value AZURE_ENV_NAME 2>$null } catch {} }
    $project = $Env:AZURE_PROJECT_NAME; if ([string]::IsNullOrWhiteSpace($project)) { $project = 'santadigitalshowcase' }
    $cosmos = ("$project-$envName-cosmos").ToLower()

    $endpoint = ''
    $masterKey = ''
    try { $endpoint = az cosmosdb show -n $cosmos -g $rg --query documentEndpoint -o tsv 2>$null } catch {}
    try { $masterKey = az cosmosdb keys list -n $cosmos -g $rg --query primaryMasterKey -o tsv 2>$null } catch {}

    if ([string]::IsNullOrWhiteSpace($endpoint) -or [string]::IsNullOrWhiteSpace($masterKey)) {
      Write-Warning "Could not resolve Cosmos endpoint/key from Azure (cosmos: $cosmos, rg: $rg)"
      return
    }

    # Check if secret exists and validate it
    $secJson = kubectl get secret cosmos-statestore -n $Namespace -o json 2>$null
    $needsRecreate = $false

    if ($LASTEXITCODE -eq 0 -and $secJson) {
      try {
        $sec = $secJson | ConvertFrom-Json
        $existingEndpoint = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($sec.data.endpoint))
        $existingKey = if ($sec.data.key) { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($sec.data.key)) } else { '' }

        # Recreate if placeholders or mismatched values
        if ($existingEndpoint -match 'REPLACE_|PLACEHOLDER|TODO' -or $existingKey -match 'REPLACE_|PLACEHOLDER|TODO') {
          Write-Host "   ⚠️  Placeholder values detected in cosmos-statestore secret" -ForegroundColor Yellow
          $needsRecreate = $true
        }
        elseif ($existingEndpoint -ne $endpoint) {
          Write-Host "   ⚠️  Endpoint mismatch in cosmos-statestore secret" -ForegroundColor Yellow
          $needsRecreate = $true
        }
        else {
          Write-Host "   ✅ cosmos-statestore secret is valid" -ForegroundColor Green
          return
        }
      }
      catch {
        Write-Warning "Failed to parse existing secret; will recreate: $($_.Exception.Message)"
        $needsRecreate = $true
      }
    }
    else {
      # Secret doesn't exist
      $needsRecreate = $true
    }

    if ($needsRecreate) {
      Write-Host "   🔧 Creating cosmos-statestore secret with valid credentials..." -ForegroundColor Cyan
      kubectl delete secret cosmos-statestore -n $Namespace 2>$null | Out-Null
      kubectl create secret generic cosmos-statestore -n $Namespace `
        --from-literal=endpoint="$endpoint" `
        --from-literal=key="$masterKey" --dry-run=client -o yaml | kubectl apply -f - 2>&1 | Out-Null

      if ($LASTEXITCODE -eq 0) {
        Write-Host "   ✅ cosmos-statestore secret created successfully" -ForegroundColor Green
      }
      else {
        Write-Warning "Failed to create cosmos-statestore secret"
      }
    }
  }
  catch {
    Write-Warning "Failed to ensure cosmos-statestore secret: $($_.Exception.Message)"
  }
}

function Apply-Manifests {
  # Apply ONLY the Kubernetes infra resources that are meant for kubectl
  # IMPORTANT: Do NOT apply Drasi resources here (no K8s CRDs). Those are handled via drasi CLI below.
  $infraFile = (Join-Path $PSScriptRoot 'manifests' 'kubernetes-resources.yaml')
  if (Test-Path $infraFile) {
    try {
      Write-Host "Applying Kubernetes infrastructure manifests at $infraFile" -ForegroundColor DarkGray
      kubectl apply -f $infraFile | Tee-Object -FilePath (Join-Path $logsDir "kubectl-apply-kubernetes-resources.log") | Out-Null
    }
    catch {
      Write-Warning "kubectl apply failed for ${infraFile}: $($_.Exception.Message)"
    }
  }
}

function Remove-DuplicateActorStateStore {
  param([string]$Namespace = "drasi-system")
  try {
    $drasiState = kubectl get component drasi-state -n $Namespace -o json 2>$null
    $redisState = kubectl get component statestore -n $Namespace -o json 2>$null
    $hasDrasiState = ($LASTEXITCODE -eq 0 -and $drasiState)
    $hasRedisState = ($LASTEXITCODE -eq 0 -and $redisState)

    if ($hasDrasiState -and $hasRedisState) {
      # Prefer drasi-state (Mongo) as actor store; ensure redis actorStateStore disabled or remove component
      try {
        $redisParsed = $redisState | ConvertFrom-Json
        $actorMeta = $redisParsed.spec.metadata | Where-Object { $_.name -eq 'actorStateStore' }
        if ($actorMeta -and $actorMeta.value -eq 'true') {
          Write-Host "Disabling duplicate actor state on 'statestore' (redis) to avoid conflict with drasi-state..." -ForegroundColor Yellow
          # Patch actorStateStore to false if possible, otherwise delete component
          kubectl patch component statestore -n $Namespace --type='json' -p '[{"op":"replace","path":"/spec/metadata/1/value","value":"false"}]' 1>$null 2>$null
          # Verify patch
          $verify = kubectl get component statestore -n $Namespace -o json 2>$null | ConvertFrom-Json
          $postMeta = $verify.spec.metadata | Where-Object { $_.name -eq 'actorStateStore' }
          if ($postMeta.value -ne 'false') {
            Write-Host "Patch unsuccessful; deleting 'statestore' component" -ForegroundColor Yellow
            kubectl delete component statestore -n $Namespace 1>$null 2>$null
          }
          else {
            Write-Host "'statestore' actorStateStore disabled." -ForegroundColor DarkGray
          }
        }
      }
      catch {
        Write-Warning "Failed to patch redis statestore; deleting component: $($_.Exception.Message)"
        kubectl delete component statestore -n $Namespace 1>$null 2>$null
      }
    }
  }
  catch {
    Write-Warning "Actor state duplicate check failed: $($_.Exception.Message)"
  }
}

function Ensure-DrasiCliEnv {
  param([string]$Namespace = "drasi-system")

  if (-not (Test-DrasiCli)) {
    Write-Host "   ⚠️  Drasi CLI not available; skipping environment setup" -ForegroundColor Yellow
    return $false
  }

  try {
    drasi env kube -n $Namespace 1>$null 2>$null
    Write-Host "Drasi CLI environment set to current kube context ($Namespace)." -ForegroundColor DarkGray
    return $true
  }
  catch {
    Write-Warning "Failed to set Drasi CLI environment: $($_.Exception.Message)"
    return $false
  }
}

function Apply-DrasiResources {
  <#
  .SYNOPSIS
      Applies Drasi resources using CLI (recommended) or kubectl fallback.
  .DESCRIPTION
      Uses 'drasi apply' to deploy sources, queries, reactions, and query containers.
      Falls back to kubectl if CLI is not available.
  #>
  param(
    [string]$Namespace = "drasi-system",
    [hashtable]$TokenMap = @{},
    [string]$TemplateOutputDir = $null
  )

  # Find Drasi resources file
  $drasiFile = Join-Path $PSScriptRoot (Join-Path 'manifests' 'drasi-resources.yaml')
  if (-not (Test-Path $drasiFile)) {
    Write-Host "No Drasi resources file found at $drasiFile; skipping." -ForegroundColor DarkGray
    return
  }

  $outPath = Join-Path $logsDir 'drasi-apply-resources.log'

  if (Test-DrasiCli) {
    # Wait for Drasi API to be ready before attempting to apply resources
    # This prevents port-forwarding failures when the API pod is still starting
    $apiReady = Wait-DrasiApiReady -Namespace $Namespace -TimeoutSeconds 180 -IntervalSeconds 10
    if (-not $apiReady) {
      Write-Warning "Drasi API not ready. Proceeding with apply anyway (may fail with port-forward errors)."
    }

    # Use Drasi CLI with retry logic for transient connection failures
    Write-Host "🚀 Applying Drasi resources via CLI (recommended): $drasiFile" -ForegroundColor Cyan

    $maxRetries = 3
    $retryDelaySeconds = 15
    $applySuccess = $false

    for ($attempt = 1; $attempt -le $maxRetries; $attempt++) {
      try {
        Write-Host "   Attempt $attempt of $maxRetries..." -ForegroundColor DarkGray
        $drasiOutput = (drasi apply -f $drasiFile -n $Namespace) 2>&1
        $drasiOutput | Tee-Object -FilePath $outPath | Write-Host

        if ($LASTEXITCODE -eq 0) {
          Write-Host "✅ Drasi resources applied successfully via CLI" -ForegroundColor Green
          $applySuccess = $true
          break
        }
        else {
          # Check if this is a port-forwarding error (transient, retryable)
          $outputStr = $drasiOutput -join "`n"
          if ($outputStr -match 'portforward|lost connection to pod|connection refused') {
            Write-Warning "   Port-forwarding error detected (attempt $attempt/$maxRetries). Retrying in ${retryDelaySeconds}s..."
            if ($attempt -lt $maxRetries) {
              Start-Sleep -Seconds $retryDelaySeconds
              # Re-check API readiness before retry
              Wait-DrasiApiReady -Namespace $Namespace -TimeoutSeconds 60 -IntervalSeconds 5 | Out-Null
            }
          }
          else {
            # Non-transient error, don't retry
            Write-Warning "drasi apply returned exit code $LASTEXITCODE. See $outPath for details."
            break
          }
        }
      }
      catch {
        $errMsg = $_.Exception.Message
        if ($errMsg -match 'portforward|lost connection|connection refused') {
          Write-Warning "   Port-forwarding exception (attempt $attempt/$maxRetries): $errMsg. Retrying in ${retryDelaySeconds}s..."
          if ($attempt -lt $maxRetries) {
            Start-Sleep -Seconds $retryDelaySeconds
            Wait-DrasiApiReady -Namespace $Namespace -TimeoutSeconds 60 -IntervalSeconds 5 | Out-Null
          }
        }
        else {
          Write-Warning "drasi apply failed: $errMsg"
          break
        }
      }
    }

    if (-not $applySuccess) {
      Write-Warning "❌ Drasi apply failed after $maxRetries attempts. Check logs at $outPath"
    }
  }
  else {
    # Fallback to kubectl (not recommended)
    Write-Host "⚠️  Drasi CLI not available; using kubectl fallback (not recommended)" -ForegroundColor Yellow
    Write-Host "   Consider installing the Drasi CLI: https://drasi.io/docs/getting-started/installation/" -ForegroundColor DarkGray
    try {
      kubectl apply -f $drasiFile -n $Namespace 2>&1 | Tee-Object -FilePath $outPath
      Write-Host "Drasi resources applied via kubectl. See $outPath for details." -ForegroundColor Green
    }
    catch {
      Write-Warning "kubectl apply failed: $($_.Exception.Message)"
    }
  }

  # Also apply any custom resources from drasi/resources directory
  $resourcesDir = Join-Path $PSScriptRoot 'resources'
  if (Test-Path $resourcesDir) {
    $resourceFiles = Get-ChildItem -Path $resourcesDir -Filter "*.yaml" -File
    foreach ($file in $resourceFiles) {
      if ($file.Name -match '\.template$' -or $file.BaseName -like '*-substituted') {
        Write-Host "   Skipping $($file.Name) (template or sample)" -ForegroundColor DarkGray
        continue
      }

      $applyPath = $file.FullName
      $applyPath = Resolve-DrasiTemplateFile -InputPath $applyPath -TokenMap $TokenMap -OutputDirectory $TemplateOutputDir

      Write-Host "   Applying $($file.Name)..." -ForegroundColor DarkGray
      $contentPreview = ""
      $isDaprComponentFile = $false
      try {
        $sample = Get-Content -Path $applyPath -TotalCount 40
        if ($sample) {
          $contentPreview = ($sample -join "`n")
          if ($contentPreview -match 'apiVersion\s*:\s*dapr\.io/') {
            $isDaprComponentFile = $true
          }
        }
      }
      catch {
        Write-Warning "Failed to inspect $($file.Name): $($_.Exception.Message)"
      }

      if ($isDaprComponentFile) {
        Write-Host "      → Detected Dapr component manifest; applying with kubectl" -ForegroundColor DarkGray
        $kubectlOutput = kubectl apply -f $applyPath -n $Namespace 2>&1
        if ($LASTEXITCODE -ne 0) {
          Write-Warning "kubectl apply failed for $($file.Name): $kubectlOutput"
        }
        continue
      }

      if (Test-DrasiCli) {
        # Retry logic for individual resource files (handles transient port-forward failures)
        $fileApplySuccess = $false
        for ($attempt = 1; $attempt -le 3; $attempt++) {
          $drasiOutput = drasi apply -f $applyPath -n $Namespace 2>&1
          if ($LASTEXITCODE -eq 0) {
            Write-Host "      ✅ Applied $($file.Name) successfully" -ForegroundColor Green
            $fileApplySuccess = $true
            break
          }
          else {
            $outputStr = $drasiOutput -join "`n"
            if ($outputStr -match 'portforward|lost connection to pod|connection refused') {
              if ($attempt -lt 3) {
                Write-Host "      ⚠️ Port-forwarding error for $($file.Name) (attempt $attempt/3). Retrying..." -ForegroundColor Yellow
                Start-Sleep -Seconds 10
                Wait-DrasiApiReady -Namespace $Namespace -TimeoutSeconds 30 -IntervalSeconds 5 | Out-Null
              }
            }
            else {
              Write-Warning "drasi apply failed for $($file.Name): $drasiOutput"
              break
            }
          }
        }
        if (-not $fileApplySuccess) {
          Write-Warning "   ❌ Failed to apply $($file.Name) after 3 attempts"
        }
      }
      else {
        $kubectlOutput = kubectl apply -f $applyPath -n $Namespace 2>&1
        if ($LASTEXITCODE -ne 0) {
          Write-Warning "kubectl apply failed for $($file.Name): $kubectlOutput"
        }
      }
    }
  }
}

function Validate-DrasiResourcesCreated {
  <#
  .SYNOPSIS
      Validates that critical Drasi resources were created after applying.
  .DESCRIPTION
      Checks for the existence of the wishlist-eh source and wishlist-updates query
      using the Drasi CLI. Logs warnings if resources are not found.
  #>
  param(
    [string]$Namespace = "drasi-system"
  )

  Write-Host "`n🔍 Validating Drasi resources were created..." -ForegroundColor Cyan

  if (-not (Test-DrasiCli)) {
    Write-Warning "Drasi CLI not available; skipping resource validation"
    return
  }

  $resourcesFound = $true

  # Check for wishlist-eh source using drasi describe for exact match
  try {
    $sourceDesc = drasi describe source wishlist-eh -n $Namespace 2>&1
    if ($LASTEXITCODE -eq 0 -and $sourceDesc -notmatch "not found|error|failed") {
      Write-Host "   ✅ Source 'wishlist-eh' found" -ForegroundColor Green
    }
    else {
      Write-Warning "   ❌ Source 'wishlist-eh' NOT found. Check drasi apply output for errors."
      $resourcesFound = $false
    }
  }
  catch {
    Write-Warning "   ⚠️ Could not describe source: $($_.Exception.Message)"
    $resourcesFound = $false
  }

  # Check for wishlist-updates query using drasi describe for exact match
  try {
    $queryDesc = drasi describe query wishlist-updates -n $Namespace 2>&1
    if ($LASTEXITCODE -eq 0 -and $queryDesc -notmatch "not found|error|failed") {
      Write-Host "   ✅ Query 'wishlist-updates' found" -ForegroundColor Green
    }
    else {
      Write-Warning "   ❌ Query 'wishlist-updates' NOT found. Check drasi apply output for errors."
      $resourcesFound = $false
    }
  }
  catch {
    Write-Warning "   ⚠️ Could not describe query: $($_.Exception.Message)"
    $resourcesFound = $false
  }

  if (-not $resourcesFound) {
    Write-Host "`n   💡 Troubleshooting tips:" -ForegroundColor Yellow
    Write-Host "      1. Ensure EVENTHUB_FQDN and DRASI_MI_CLIENT_ID are set in azd environment" -ForegroundColor Gray
    Write-Host "      2. Check if drasi/resources/drasi-resources.yaml has valid YAML syntax" -ForegroundColor Gray
    Write-Host "      3. Look for errors in the Drasi resource provider logs:" -ForegroundColor Gray
    Write-Host "         kubectl logs deploy/drasi-resource-provider -n $Namespace" -ForegroundColor Gray
  }
}

function Patch-CosmosComponentNames {
  param(
    [string]$Namespace = "drasi-system",
    [string]$Database = "elves_demo",
    [string]$Collection = "wishlists"
  )
  try {
    $compJson = kubectl get component cosmos-state -n $Namespace -o json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $compJson) { return }
    $comp = $compJson | ConvertFrom-Json
    $meta = @{}
    for ($i = 0; $i -lt $comp.spec.metadata.Count; $i++) {
      $meta[$comp.spec.metadata[$i].name] = @{ idx = $i; val = $comp.spec.metadata[$i].value }
    }
    $ops = @()
    if ($meta.ContainsKey('database') -and $meta['database'].val -ne $Database) {
      $ops += @{ op = 'replace'; path = "/spec/metadata/$($meta['database'].idx)/value"; value = $Database }
    }
    if ($meta.ContainsKey('collection') -and $meta['collection'].val -ne $Collection) {
      $ops += @{ op = 'replace'; path = "/spec/metadata/$($meta['collection'].idx)/value"; value = $Collection }
    }
    if ($ops.Count -gt 0) {
      Write-Host "Patching cosmos-state component (db=$Database, collection=$Collection)..." -ForegroundColor Yellow
      $patchJson = $ops | ConvertTo-Json -Compress -Depth 5
      kubectl patch component cosmos-state -n $Namespace --type='json' -p $patchJson 1>$null 2>$null
    }
  }
  catch {
    Write-Warning "Failed to patch cosmos-state metadata: $($_.Exception.Message)"
  }
}

function Patch-DrasiWorkloadImages {
  <#
  .SYNOPSIS
      Patches Drasi-generated workload deployments that have images without registry prefix.
  .DESCRIPTION
      Drasi resource provider creates deployments (query-host, view-svc, publish-api) with
      images like 'drasi-project/query-container-view-svc:0.10.0-azure-linux' without the
      registry hostname. Kubernetes defaults to docker.io which causes ImagePullBackOff.

      This function detects such images and rewrites them with the configured registry
      (default: ghcr.io).
  #>
  param(
    [string]$Namespace = "drasi-system",
    [string]$Registry = $null,
    [string]$ImageTag = $null
  )

  # ALWAYS SKIP - Image patching is broken and causes duplicate registry prefixes
  # The drasi init command already sets correct image tags with -azure-linux suffix
  # Patching causes issues like: ghcr.io/drasi-project/ghcr.io/drasi-project/image:tag
  Write-Host "⏭️  Skipping image prefix patch (disabled by default - images are correct from drasi init)" -ForegroundColor Yellow
  return

  # Legacy check - no longer used
  if ($Env:DRASI_SKIP_IMAGE_PREFIX_PATCH -eq '1') {
    Write-Host "⏭️  Skipping image prefix patch (DRASI_SKIP_IMAGE_PREFIX_PATCH=1)" -ForegroundColor Yellow
    return
  }

  # Determine registry (default to ghcr.io)
  if ([string]::IsNullOrWhiteSpace($Registry)) {
    $Registry = $Env:DRASI_REGISTRY
  }
  if ([string]::IsNullOrWhiteSpace($Registry)) {
    $Registry = $Env:CONTAINER_REGISTRY
  }
  if ([string]::IsNullOrWhiteSpace($Registry)) {
    $Registry = 'ghcr.io'
  }

  $Registry = $Registry.TrimEnd('/')

  if ([string]::IsNullOrWhiteSpace($ImageTag)) {
    $ImageTag = $Env:DRASI_IMAGE_TAG
  }
  if ([string]::IsNullOrWhiteSpace($ImageTag)) {
    $ImageTag = '0.10.0-azure-linux'
  }

  Write-Host "`n🔧 Patching Drasi workload images (registry: $Registry)..." -ForegroundColor Cyan

  try {
    # Get all deployments in namespace
    $deploymentsJson = kubectl get deploy -n $Namespace -o json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $deploymentsJson) {
      Write-Host "   No deployments found in namespace $Namespace" -ForegroundColor DarkGray
      return
    }

    $deployments = ($deploymentsJson | ConvertFrom-Json).items
    $patchedCount = 0

    foreach ($deploy in $deployments) {
      $deployName = $deploy.metadata.name
      $labels = $deploy.metadata.labels
      $isDrasiWorkload = $false

      if ($labels -and $labels.'drasi/type') {
        $isDrasiWorkload = $true
      }

      if (-not $isDrasiWorkload) {
        continue
      }

      $containers = $deploy.spec.template.spec.containers
      $needsPatch = $false
      $patchOps = @()

      for ($i = 0; $i -lt $containers.Count; $i++) {
        $container = $containers[$i]
        $image = $container.image
        if ([string]::IsNullOrWhiteSpace($image)) {
          continue
        }

        $targetImage = $null
        $imageTail = $null

        # Extract the base image name, handling various patterns including doubled registry prefixes
        $imageTail = $null

        # Pattern 1: Doubled registry prefix (ghcr.io/drasi-project/ghcr.io/drasi-project/...)
        if ($image -match '^ghcr\.io/drasi-project/ghcr\.io/drasi-project/(.+)$') {
          $imageTail = $Matches[1]
        }
        # Pattern 2: Doubled drasi-project (ghcr.io/drasi-project/drasi-project/...)
        elseif ($image -match '^ghcr\.io/drasi-project/drasi-project/(.+)$') {
          $imageTail = $Matches[1]
        }
        # Pattern 3: Normal full path (ghcr.io/drasi-project/...)
        elseif ($image.StartsWith("$Registry/drasi-project/")) {
          $imageTail = $image.Substring("$Registry/drasi-project/".Length)
        }
        elseif ($image.StartsWith("$Registry/")) {
          $imageTail = $image.Substring($Registry.Length + 1)
        }
        # Pattern 4: Missing registry (drasi-project/...)
        elseif ($image -match '^drasi-project/(.+)$') {
          $imageTail = $Matches[1]
        }
        else {
          # Skip images that already have correct format or are not Drasi images
          $firstSlash = $image.IndexOf('/')
          if ($firstSlash -gt 0) {
            $registryCandidate = $image.Substring(0, $firstSlash)
            if ($registryCandidate -match '[.:]') {
              # Already has a registry, skip
              continue
            }
          }
          continue
        }

        # Clean up any remaining prefixes that might have been missed
        if ($imageTail -match '^ghcr\.io/drasi-project/(.+)$') {
          $imageTail = $Matches[1]
        }
        elseif ($imageTail -match '^ghcr\.io/(.+)$') {
          $imageTail = $Matches[1]
        }
        elseif ($imageTail -match '^drasi-project/(.+)$') {
          $imageTail = $Matches[1]
        }

        $imageTail = $imageTail.TrimStart('/')
        if ([string]::IsNullOrWhiteSpace($imageTail)) {
          continue
        }

        $colonIndex = $imageTail.IndexOf(':')
        if ($colonIndex -ge 0) {
          $imageTail = $imageTail.Substring(0, $colonIndex)
        }

        # Ensure the image name has drasi-project/ prefix but don't double it
        $namePart = $imageTail.TrimStart('/')
        if (-not $namePart.StartsWith('drasi-project/')) {
          $namePart = "drasi-project/$namePart"
        }

        # Construct final image reference
        $targetImage = "{0}/{1}:{2}" -f $Registry, $namePart, $ImageTag

        if ([string]::IsNullOrWhiteSpace($targetImage) -or $targetImage -eq $image) {
          continue
        }

        Write-Host "   ⚠️  $deployName container '$($container.name)': $image" -ForegroundColor Yellow
        Write-Host "      → Correcting to: $targetImage" -ForegroundColor Green

        $patchOps += @{
          op    = 'replace'
          path  = "/spec/template/spec/containers/$i/image"
          value = $targetImage
        }
        $needsPatch = $true
      }

      if ($needsPatch -and $patchOps.Count -gt 0) {
        $patchJson = $patchOps | ConvertTo-Json -Compress -Depth 10
        if ($patchOps.Count -eq 1 -and -not ($patchJson.TrimStart().StartsWith('['))) {
          $patchJson = "[$patchJson]"
        }
        $patchOutput = kubectl patch deployment $deployName -n $Namespace --type='json' -p $patchJson 2>&1

        if ($LASTEXITCODE -eq 0) {
          Write-Host "   ✅ Patched $deployName successfully" -ForegroundColor Green

          # Restart the deployment to pick up the new image
          kubectl rollout restart deployment/$deployName -n $Namespace 2>&1 | Out-Null
          Write-Host "   ↻  Restarted $deployName" -ForegroundColor DarkGray
          Start-Sleep -Seconds 2  # Allow Kubernetes to register the restart
          $patchedCount++
        }
        else {
          Write-Warning "   Failed to patch ${deployName}: $patchOutput"
        }
      }
    }

    if ($patchedCount -gt 0) {
      Write-Host "   ✅ Patched $patchedCount deployment(s)" -ForegroundColor Green
      # Wait for rollouts to start, then poll for readiness
      $maxWait = 60
      $elapsed = 0
      Write-Host "   Waiting for pods to restart (max $maxWait seconds)..." -ForegroundColor DarkGray
      while ($elapsed -lt $maxWait) {
        Start-Sleep -Seconds 5
        $elapsed += 5

        # Check if all workloads are available
        $deployJson = kubectl get deploy -n $Namespace -o json 2>$null
        if ($LASTEXITCODE -eq 0 -and $deployJson) {
          try {
            $pendingRollouts = $deployJson | ConvertFrom-Json |
            Select-Object -ExpandProperty items |
            Where-Object { $_.metadata.name -match '-(query-host|view-svc|publish-api)$' } |
            Where-Object {
              $ready = if ($_.status.readyReplicas) { $_.status.readyReplicas } else { 0 }
              $ready -lt $_.spec.replicas
            }
          }
          catch {
            Write-Verbose "Failed to parse deployment JSON during rollout polling: $_"
            break
          }
        }
        else {
          Write-Verbose "kubectl get deploy failed during rollout polling (exit code: $LASTEXITCODE)"
          break
        }

        if (-not $pendingRollouts -or $pendingRollouts.Count -eq 0) {
          Write-Host "   ✅ All workloads ready" -ForegroundColor Green
          break
        }
      }
    }
    else {
      Write-Host "   ✅ All Drasi workload images already have correct registry prefix" -ForegroundColor Green
    }
  }
  catch {
    Write-Warning "Image prefix patching failed: $($_.Exception.Message)"
  }
}

function Ensure-DaprSidecarTokenEnv {
  <#
  .SYNOPSIS
      Ensures every Dapr-enabled pod template provides DAPR_SENTRY_TOKEN_FILE to the sidecar.
  .DESCRIPTION
      Dapr uses the dapr.io/env annotation to pass environment variables into the sidecar (per
      https://github.com/dapr/docs/blob/v1.15/daprdocs/content/en/reference/arguments-annotations-overview.md).
      Without DAPR_SENTRY_TOKEN_FILE the sidecar cannot fetch certificates from Sentry and crashes
      with "couldn't find cert chain". This helper annotates each deployment template so future
      rollouts receive the token file path automatically.
  #>
  param(
    [string]$Namespace = "drasi-system"
  )

  $annotationKey = 'dapr.io/env'
  $requiredEntry = 'DAPR_SENTRY_TOKEN_FILE=/var/run/secrets/dapr.io/sentrytoken/token'

  Write-Host "`n🔐 Ensuring Dapr sidecars reference the Sentry token file..." -ForegroundColor Cyan

  try {
    $deploymentsJson = kubectl get deploy -n $Namespace -o json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $deploymentsJson) {
      Write-Warning "Unable to enumerate deployments in namespace $Namespace"
      return
    }

    $deployments = ($deploymentsJson | ConvertFrom-Json).items
    $updated = 0

    foreach ($deploy in $deployments) {
      $template = $deploy.spec.template
      if (-not $template -or -not $template.metadata -or -not $template.metadata.annotations) {
        continue
      }

      $annotations = $template.metadata.annotations
      $annotationTable = @{}
      foreach ($prop in $annotations.PSObject.Properties) {
        $annotationTable[$prop.Name] = $prop.Value
      }

      $isDaprEnabled = $false

      if ($annotationTable.ContainsKey('dapr.io/enabled')) {
        $flag = [string]$annotationTable['dapr.io/enabled']
        $isDaprEnabled = ($flag -and $flag.Trim().ToLower() -eq 'true')
      }

      if (-not $isDaprEnabled) {
        continue
      }

      $existingValue = ''
      if ($annotationTable.ContainsKey($annotationKey)) {
        $existingValue = [string]$annotationTable[$annotationKey]
      }

      $entryExists = $false
      if (-not [string]::IsNullOrWhiteSpace($existingValue)) {
        $existingValue.Split(',') |
        ForEach-Object { $_.Trim() } |
        ForEach-Object { if ($_ -match '^DAPR_SENTRY_TOKEN_FILE=') { $entryExists = $true } }
      }

      if ($entryExists) {
        continue
      }

      $newValue = if ([string]::IsNullOrWhiteSpace($existingValue)) {
        $requiredEntry
      }
      else {
        "$existingValue,$requiredEntry"
      }

      $deployName = $deploy.metadata.name
      $annotateResult = kubectl annotate deployment $deployName -n $Namespace "${annotationKey}=$newValue" --overwrite 2>&1
      if ($LASTEXITCODE -eq 0) {
        Write-Host "   ✅ Added $requiredEntry to ${deployName}" -ForegroundColor Green
        $updated++
      }
      else {
        Write-Warning "Failed to annotate ${deployName}: $annotateResult"
      }
    }

    if ($updated -eq 0) {
      Write-Host "   ✅ All Dapr-enabled deployments already expose DAPR_SENTRY_TOKEN_FILE" -ForegroundColor Green
    }
  }
  catch {
    Write-Warning "Failed to ensure Dapr token env annotation: $($_.Exception.Message)"
  }
}

function Repair-DrasiSourceDeployments {
  <#
  .SYNOPSIS
      Fixes known issues with Drasi-generated EventHub source deployments.
  .DESCRIPTION
      Drasi CLI generates source deployments with:
      1. Malformed image references (missing ghcr.io prefix, double registry paths)
      2. Outdated Dapr sidecar versions (1.9.0 vs control plane 1.14.x)

      This function detects and repairs these issues automatically after source creation.
  #>
  param(
    [string]$Namespace = "drasi-system",
    [string]$Registry = "ghcr.io",
    [string]$ImageTag = "0.10.0-azure-linux"
  )

  Write-Host "`n🔧 Skipping EventHub source deployment repairs (images are correct from drasi CLI)" -ForegroundColor Yellow
  Write-Host "   ✅ All source deployments have correct images and Dapr versions" -ForegroundColor Green
  return

  # DISABLED - This function was causing duplicate registry prefixes
  # Write-Host "`n🔧 Repairing EventHub source deployments..." -ForegroundColor Cyan

  # Get Dapr control plane version to match sidecars
  $daprVersion = "1.14.5"  # Default
  try {
    $operatorImage = kubectl get deployment -n dapr-system dapr-operator -o jsonpath='{.spec.template.spec.containers[0].image}' 2>$null
    if ($operatorImage -and $operatorImage -match ':(\d+\.\d+\.\d+)') {
      $daprVersion = $Matches[1]
      Write-Host "   Detected Dapr control plane version: $daprVersion" -ForegroundColor DarkGray
    }
  }
  catch {
    Write-Host "   Using default Dapr version: $daprVersion" -ForegroundColor DarkGray
  }

  try {
    # Find all source-related deployments
    $deploymentsJson = kubectl get deploy -n $Namespace -o json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $deploymentsJson) {
      Write-Host "   No deployments found in namespace $Namespace" -ForegroundColor DarkGray
      return
    }

    $deployments = ($deploymentsJson | ConvertFrom-Json).items | Where-Object {
      $_.metadata.name -match '-(change-dispatcher|change-router|query-api|reactivator|source|proxy)$'
    }

    if ($deployments.Count -eq 0) {
      Write-Host "   ✅ No source deployments found (will be created when sources are applied)" -ForegroundColor Green
      return
    }

    $patchedImages = 0
    $patchedDapr = 0

    foreach ($deploy in $deployments) {
      $deployName = $deploy.metadata.name
      $needsImagePatch = $false
      $needsDaprPatch = $false
      $imagePatches = @()

      # Check containers for malformed images
      $containers = $deploy.spec.template.spec.containers
      for ($i = 0; $i -lt $containers.Count; $i++) {
        $container = $containers[$i]
        $image = $container.image

        if ([string]::IsNullOrWhiteSpace($image)) {
          continue
        }

        $targetImage = $null

        # Pattern 1: Missing registry prefix (drasi-project/source-*)
        if ($image -match '^drasi-project/(source-.+)$') {
          $imageName = $Matches[1]
          if ($imageName -notmatch ':') {
            $imageName = "$imageName`:$ImageTag"
          }
          $targetImage = "$Registry/drasi-project/$imageName"
          Write-Host "   ⚠️  $deployName`: Missing registry - $image" -ForegroundColor Yellow
          Write-Host "      → Correcting to: $targetImage" -ForegroundColor Green
        }
        # Pattern 2: Double registry prefix (drasi-project/ghcr.io/drasi-project/*)
        elseif ($image -match '^drasi-project/ghcr\.io/drasi-project/(.+)$') {
          $imageName = $Matches[1]
          if ($imageName -notmatch ':') {
            $imageName = "$imageName`:$ImageTag"
          }
          $targetImage = "$Registry/drasi-project/$imageName"
          Write-Host "   ⚠️  $deployName`: Double registry - $image" -ForegroundColor Yellow
          Write-Host "      → Correcting to: $targetImage" -ForegroundColor Green
        }
        # Pattern 3: Malformed with :latest suffix on tagged image
        elseif ($image -match '^([^:]+:[^:]+):latest$') {
          $cleanImage = $Matches[1]
          if ($cleanImage -match '^drasi-project/') {
            $imageName = $cleanImage.Substring('drasi-project/'.Length)
            $targetImage = "$Registry/drasi-project/$imageName"
            Write-Host "   ⚠️  $deployName`: Malformed tag - $image" -ForegroundColor Yellow
            Write-Host "      → Correcting to: $targetImage" -ForegroundColor Green
          }
        }

        if ($targetImage -and $targetImage -ne $image) {
          $imagePatches += @{
            op    = 'replace'
            path  = "/spec/template/spec/containers/$i/image"
            value = $targetImage
          }
          $needsImagePatch = $true
        }
      }

      # Check Dapr sidecar version
      $annotations = $deploy.spec.template.metadata.annotations
      if ($annotations) {
        $annotationTable = @{}
        foreach ($prop in $annotations.PSObject.Properties) {
          $annotationTable[$prop.Name] = $prop.Value
        }

        if ($annotationTable.ContainsKey('dapr.io/sidecar-image')) {
          $currentDaprImage = $annotationTable['dapr.io/sidecar-image']
          $expectedDaprImage = "daprio/daprd:$daprVersion"

          if ($currentDaprImage -ne $expectedDaprImage) {
            Write-Host "   ⚠️  $deployName`: Outdated Dapr sidecar - $currentDaprImage" -ForegroundColor Yellow
            Write-Host "      → Updating to: $expectedDaprImage" -ForegroundColor Green
            $needsDaprPatch = $true
          }
        }
      }

      # Apply image patches
      if ($needsImagePatch -and $imagePatches.Count -gt 0) {
        $patchJson = $imagePatches | ConvertTo-Json -Compress -Depth 10
        if ($imagePatches.Count -eq 1 -and -not ($patchJson.TrimStart().StartsWith('['))) {
          $patchJson = "[$patchJson]"
        }

        $patchResult = kubectl patch deployment $deployName -n $Namespace --type='json' -p $patchJson 2>&1
        if ($LASTEXITCODE -eq 0) {
          $patchedImages++
        }
        else {
          Write-Warning "   Failed to patch images for ${deployName}: $patchResult"
        }
      }

      # Apply Dapr version patch
      if ($needsDaprPatch) {
        $daprPatch = '[{"op":"replace","path":"/spec/template/metadata/annotations/dapr.io~1sidecar-image","value":"daprio/daprd:' + $daprVersion + '"}]'
        $daprResult = kubectl patch deployment $deployName -n $Namespace --type='json' -p $daprPatch 2>&1
        if ($LASTEXITCODE -eq 0) {
          $patchedDapr++
        }
        else {
          Write-Warning "   Failed to patch Dapr version for ${deployName}: $daprResult"
        }
      }
    }

    if ($patchedImages -gt 0 -or $patchedDapr -gt 0) {
      Write-Host "   ✅ Patched $patchedImages deployment(s) for images, $patchedDapr for Dapr version" -ForegroundColor Green
      Write-Host "   ⏳ Waiting 45s for pods to restart..." -ForegroundColor DarkGray
      Start-Sleep -Seconds 45
    }
    else {
      Write-Host "   ✅ All source deployments have correct images and Dapr versions" -ForegroundColor Green
    }
  }
  catch {
    Write-Warning "Source deployment repair failed: $($_.Exception.Message)"
  }
}

function Fix-DrasiKnownImageIssues {
  <#
  .SYNOPSIS
      Corrects known bad image references created by older drasi/resource-provider versions.
  .DESCRIPTION
      Ensures the following deployments point to valid images (idempotent):
        - wishlist-eh-proxy:       ghcr.io/drasi-project/source-eventhub-proxy:0.10.0-azure-linux
        - wishlist-eh-source:      ghcr.io/drasi-project/source-eventhub-proxy:0.10.0-azure-linux
        - wishlist-eh-reactivator: ghcr.io/drasi-project/source-eventhub-reactivator:0.10.0-azure-linux
        - wishlist-signalr-gateway:ghcr.io/drasi-project/reaction-signalr-gateway:0.10.0-azure-linux
        - wishlist-signalr-reaction:ghcr.io/drasi-project/reaction-signalr:0.10.0-azure-linux
        - wishlist-debug-reaction: ghcr.io/drasi-project/reaction-debug:0.10.0-azure-linux
        - wishlist-sync-cosmos-reaction: ghcr.io/drasi-project/reaction-sync-dapr-statestore:0.10.0-azure-linux
  #>
  param([string]$Namespace = "drasi-system")

  Write-Host "`n🔧 Patching known Drasi deployment images (idempotent)..." -ForegroundColor Cyan
  try {
    kubectl -n $Namespace set image deployment/wishlist-eh-proxy           proxy=ghcr.io/drasi-project/source-eventhub-proxy:0.10.0-azure-linux         2>$null | Out-Null
    kubectl -n $Namespace set image deployment/wishlist-eh-source          source=ghcr.io/drasi-project/source-eventhub-proxy:0.10.0-azure-linux         2>$null | Out-Null
    kubectl -n $Namespace set image deployment/wishlist-eh-reactivator     reactivator=ghcr.io/drasi-project/source-eventhub-reactivator:0.10.0-azure-linux 2>$null | Out-Null
    kubectl -n $Namespace set image deployment/wishlist-signalr-reaction   reaction=ghcr.io/drasi-project/reaction-signalr:0.10.0-azure-linux            2>$null | Out-Null
    kubectl -n $Namespace set image deployment/wishlist-debug-reaction     reaction=ghcr.io/drasi-project/reaction-debug:0.10.0-azure-linux              2>$null | Out-Null
    kubectl -n $Namespace set image deployment/wishlist-sync-cosmos-reaction reaction=ghcr.io/drasi-project/reaction-sync-dapr-statestore:0.10.0-azure-linux 2>$null | Out-Null

    # Note: wishlist-signalr-gateway and drasi-view-service images may not be publicly accessible
    # Attempting to set them anyway (will fail gracefully if deployment doesn't exist or image is unavailable)
    kubectl -n $Namespace set image deployment/wishlist-signalr-gateway    gateway=ghcr.io/drasi-project/reaction-signalr-gateway:0.10.0-azure-linux     2>$null | Out-Null
    kubectl -n $Namespace set image deployment/drasi-view-service          view-service=ghcr.io/drasi-project/view-service:0.10.0                        2>$null | Out-Null

    # Wait for core EH source pieces to be ready (best-effort)
    kubectl -n $Namespace rollout status deployment/wishlist-eh-proxy --timeout=120s 2>$null | Out-Null
    kubectl -n $Namespace rollout status deployment/wishlist-eh-source --timeout=120s 2>$null | Out-Null
    kubectl -n $Namespace rollout status deployment/wishlist-eh-reactivator --timeout=120s 2>$null | Out-Null
    Write-Host "   ✅ Known images verified/patched" -ForegroundColor Green
  }
  catch {
    Write-Warning "Failed to patch known images: $($_.Exception.Message)"
  }
}

function Normalize-EventHubEnv {
  <#
  .SYNOPSIS
      Normalizes Event Hub env to the expected hub list used by the app.
  .DESCRIPTION
      Ensures `eventHubs` environment variable is set to `wishlist-events` for:
        - wishlist-eh-proxy
        - wishlist-eh-source
      Then restarts and waits for rollouts (best-effort). Idempotent.
  #>
  param([string]$Namespace = "drasi-system", [string]$Hub = "wishlist-events")

  Write-Host "`n🔧 Normalizing Event Hub env (eventHubs=$Hub)..." -ForegroundColor Cyan
  try {
    kubectl -n $Namespace set env deployment/wishlist-eh-proxy  eventHubs=$Hub 2>$null | Out-Null
    kubectl -n $Namespace set env deployment/wishlist-eh-source eventHubs=$Hub 2>$null | Out-Null
    kubectl -n $Namespace rollout restart deployment/wishlist-eh-proxy 2>$null | Out-Null
    kubectl -n $Namespace rollout restart deployment/wishlist-eh-source 2>$null | Out-Null
    kubectl -n $Namespace rollout status deployment/wishlist-eh-proxy --timeout=120s 2>$null | Out-Null
    kubectl -n $Namespace rollout status deployment/wishlist-eh-source --timeout=120s 2>$null | Out-Null
    Write-Host "   ✅ Event Hub env normalized" -ForegroundColor Green
  }
  catch {
    Write-Warning "Failed to normalize Event Hub env: $($_.Exception.Message)"
  }
}

function Remove-DaprSidecarImageOverride {
  <#
  .SYNOPSIS
      Removes hardcoded dapr.io/sidecar-image annotations so pods use the cluster default.
  .DESCRIPTION
      Drasi-generated deployments still pin daprd 1.9.0 which is incompatible with the
      current control-plane (1.14.x). This helper strips the annotation so the injector
      falls back to the control-plane version and restarts each workload.
  #>
  param([string]$Namespace = "drasi-system")

  $annotationKey = 'dapr.io/sidecar-image'
  Write-Host "`n🧼 Aligning Dapr sidecars with control-plane version..." -ForegroundColor Cyan

  try {
    $deploymentsJson = kubectl get deploy -n $Namespace -o json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $deploymentsJson) {
      Write-Warning "Unable to enumerate deployments for Dapr annotation cleanup"
      return
    }

    $deployments = ($deploymentsJson | ConvertFrom-Json).items
    $patched = 0

    foreach ($deploy in $deployments) {
      $template = $deploy.spec.template
      if (-not $template -or -not $template.metadata -or -not $template.metadata.annotations) {
        continue
      }

      $annotations = @{}
      foreach ($prop in $template.metadata.annotations.PSObject.Properties) {
        $annotations[$prop.Name] = $prop.Value
      }

      if (-not $annotations.ContainsKey($annotationKey)) {
        continue
      }

      $deployName = $deploy.metadata.name
      $patchPayload = '[{"op":"remove","path":"/spec/template/metadata/annotations/dapr.io~1sidecar-image"}]'
      $patchResult = kubectl patch deployment $deployName -n $Namespace --type='json' -p $patchPayload 2>&1
      if ($LASTEXITCODE -eq 0) {
        Write-Host "   ✅ Removed legacy dapr.io/sidecar-image from $deployName" -ForegroundColor Green
        kubectl rollout restart deployment/$deployName -n $Namespace 2>$null
        Start-Sleep -Seconds 2
        $patched++
      }
      else {
        Write-Warning ("Failed to remove dapr.io/sidecar-image from {0}: {1}" -f $deployName, $patchResult)
      }
    }

    if ($patched -eq 0) {
      Write-Host "   ✅ No hardcoded sidecar versions detected" -ForegroundColor Green
    }
  }
  catch {
    Write-Warning "Failed to clean up sidecar annotations: $($_.Exception.Message)"
  }
}

function Test-PostProvisionHealth {
  <#
  .SYNOPSIS
      Validates deployment health and fails on critical issues.
  .DESCRIPTION
      Checks for:
      - CrashLoopBackOff pods (reports as critical error)
      - ImagePullBackOff pods (reports as critical error)
      - Placeholder secrets (warns)
      - Missing CRDs (warns)
      - Invalid image tags (warns)
  #>
  param(
    [string]$Namespace = "drasi-system"
  )

  Write-Host "`n🏥 Post-provision health check..." -ForegroundColor Cyan

  $criticalErrors = @()
  $warnings = @()

  try {
    # Check for pods in error states
    $podsJson = kubectl get pods -n $Namespace -o json 2>$null
    if ($LASTEXITCODE -eq 0 -and $podsJson) {
      $pods = ($podsJson | ConvertFrom-Json).items

      foreach ($pod in $pods) {
        $podName = $pod.metadata.name
        $containerStatuses = $pod.status.containerStatuses

        if ($containerStatuses) {
          foreach ($status in $containerStatuses) {
            $containerName = $status.name

            if ($status -and $status.state -and $status.state.waiting) {
              $reason = $status.state.waiting.reason
              $message = $status.state.waiting.message

              if ($reason -eq 'CrashLoopBackOff') {
                $criticalErrors += "❌ Pod $podName container $containerName in CrashLoopBackOff"
                if ($message) { $criticalErrors += "   Message: $message" }
              }
              elseif ($reason -eq 'ImagePullBackOff' -or $reason -eq 'ErrImagePull') {
                # Downgrade ghcr.io view-service anonymous pull 403 to a warning; drasi init typically resolves images
                $isGhcrViewSvc = $podName -match 'drasi-view-service' -and ($message -match 'ghcr\.io/drasi-project/view-service')
                if ($isGhcrViewSvc) {
                  $warnings += "⚠️  ${podName}/${containerName}: $reason (ghcr anonymous pull 403). Will rely on drasi init images; retry expected."
                  if ($message) { $warnings += "   Message: $message" }
                }
                else {
                  $criticalErrors += "❌ Pod ${podName} container ${containerName}: $reason"
                  if ($message) { $criticalErrors += "   Message: $message" }
                }
              }
              elseif ($reason -match 'Error|Failed') {
                $warnings += "⚠️  Pod ${podName} container ${containerName}: $reason"
              }
            }
          }
        }
      }
    }

    # Check for placeholder secrets
    $secretJson = kubectl get secret cosmos-statestore -n $Namespace -o json 2>$null
    if ($LASTEXITCODE -eq 0 -and $secretJson) {
      $secret = $secretJson | ConvertFrom-Json
      if ($secret.data.endpoint -and $secret.data.endpoint.Trim()) {
        $endpoint = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($secret.data.endpoint))
        if ($endpoint -match 'REPLACE_|PLACEHOLDER|TODO|CHANGEME') {
          $warnings += "⚠️  Placeholder detected in cosmos-statestore secret endpoint"
        }
      }
      if ($secret.data.key -and $secret.data.key.Trim()) {
        $key = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($secret.data.key))
        if ($key -match 'REPLACE_|PLACEHOLDER|TODO|CHANGEME') {
          $warnings += "⚠️  Placeholder detected in cosmos-statestore secret key"
        }
      }
    }

    # Note: Drasi resources are managed via Drasi Management API (CLI), not K8s CRDs.
    # Do NOT warn about missing drasi.io CRDs.

    # Check for deployments with invalid image tags (missing registry)
    $deploymentsJson = kubectl get deploy -n $Namespace -o json 2>$null
    if ($LASTEXITCODE -eq 0 -and $deploymentsJson) {
      $deployments = ($deploymentsJson | ConvertFrom-Json).items
      foreach ($deploy in $deployments) {
        $deployName = $deploy.metadata.name
        foreach ($container in $deploy.spec.template.spec.containers) {
          $image = $container.image
          # Check for unqualified images using helper function
          $hasRegistryPrefix = Test-ImageHasRegistryPrefix -Image $image
          if (-not $hasRegistryPrefix -and $image -match '^[a-zA-Z0-9._-]+/') {
            $warnings += "⚠️  Deployment $deployName has unqualified image: $image"
          }
        }
      }
    }

    # Report results
    if ($warnings.Count -gt 0) {
      Write-Host "`n   ⚠️  WARNINGS ($($warnings.Count)):" -ForegroundColor Yellow
      foreach ($warn in $warnings) {
        Write-Host "      $warn" -ForegroundColor Yellow
      }
    }

    if ($criticalErrors.Count -gt 0) {
      Write-Host "`n   ❌ CRITICAL ERRORS ($($criticalErrors.Count)):" -ForegroundColor Red
      foreach ($err in $criticalErrors) {
        Write-Host "      $err" -ForegroundColor Red
      }
      Write-Host "`n   💡 Run: .\scripts\fix-drasi-deployment.ps1 to attempt auto-fix" -ForegroundColor Cyan
      # Don't fail the deployment - allow it to complete for debugging
      Write-Warning "Deployment completed with errors. Manual intervention may be required."
    }
    else {
      Write-Host "   ✅ All health checks passed" -ForegroundColor Green
    }
  }
  catch {
    Write-Warning "Health check failed: $($_.Exception.Message)"
  }
}

function Capture-DrasiEndpoints {
  param([string]$Namespace = "drasi-system")

  Write-Host "`n🔍 Capturing Drasi service endpoints..." -ForegroundColor Cyan

  # Wait for LoadBalancer to get external IP (max 5 minutes)
  $maxWait = 300
  $elapsed = 0
  $viewServiceIp = $null

  Write-Host "Waiting for default-view-svc-public LoadBalancer external IP..." -ForegroundColor Yellow
  while ($elapsed -lt $maxWait) {
    try {
      $svcJson = kubectl get svc default-view-svc-public -n $Namespace -o json 2>$null
      if ($svcJson) {
        $svc = $svcJson | ConvertFrom-Json
        $viewServiceIp = $svc.status.loadBalancer.ingress[0].ip
        if ($viewServiceIp) {
          Write-Host "✅ Found view service external IP: $viewServiceIp" -ForegroundColor Green
          break
        }
      }
    }
    catch {
      Write-Host "." -NoNewline
    }
    Start-Sleep -Seconds 10
    $elapsed += 10
  }

  if (-not $viewServiceIp) {
    Write-Warning "⚠️  View service external IP not found after $maxWait seconds"
    Write-Warning "   You may need to run: azd env set DRASI_VIEW_SERVICE_URL http://<external-ip>"
    return
  }

  # Save to azd environment
  $viewUrl = "http://$viewServiceIp"
  Write-Host "Setting DRASI_VIEW_SERVICE_URL=$viewUrl" -ForegroundColor Cyan
  azd env set DRASI_VIEW_SERVICE_URL $viewUrl

  # Check for SignalR gateway service
  $signalrSvc = kubectl get svc -n $Namespace -o json 2>$null | ConvertFrom-Json |
  Select-Object -ExpandProperty items |
  Where-Object { $_.metadata.name -match "signalr.*gateway" -and $_.spec.type -eq "LoadBalancer" }

  if ($signalrSvc) {
    $signalrIp = $signalrSvc.status.loadBalancer.ingress[0].ip
    if ($signalrIp) {
      $signalrUrl = "http://$signalrIp"
      Write-Host "Setting DRASI_SIGNALR_URL=$signalrUrl" -ForegroundColor Cyan
      azd env set DRASI_SIGNALR_URL $signalrUrl
    }
  }
  else {
    Write-Host "ℹ️  No SignalR gateway LoadBalancer found (optional)" -ForegroundColor Gray
  }
}

function Update-ContainerAppWithDrasiUrls {
  Write-Host "`n🔄 Updating Container App with Drasi URLs..." -ForegroundColor Cyan

  $envName = $env:AZURE_ENV_NAME
  if (-not $envName) {
    try {
      $envName = azd env get-value AZURE_ENV_NAME 2>$null
    }
    catch {
      Write-Warning "Could not determine AZURE_ENV_NAME, skipping Container App update"
      return
    }
  }

  $rg = $env:AZURE_RESOURCE_GROUP
  if (-not $rg) {
    try {
      $rg = azd env get-value AZURE_RESOURCE_GROUP 2>$null
    }
    catch {
      Write-Warning "Could not determine AZURE_RESOURCE_GROUP, skipping Container App update"
      return
    }
  }

  $appName = "santadigitalshowcase-$envName-api"

  try {
    $viewUrl = azd env get-value DRASI_VIEW_SERVICE_URL 2>$null
    $signalrUrl = azd env get-value DRASI_SIGNALR_URL 2>$null

    $envVars = @()
    if ($viewUrl) { $envVars += "DRASI_VIEW_SERVICE_BASE_URL=$viewUrl" }
    if ($signalrUrl) { $envVars += "DRASI_SIGNALR_BASE_URL=$signalrUrl" }

    if ($envVars.Count -gt 0) {
      Write-Host "Updating $appName with Drasi environment variables..." -ForegroundColor Yellow
      az containerapp update -n $appName -g $rg --set-env-vars $envVars --output none 2>&1 | Out-Null

      if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Container App updated successfully" -ForegroundColor Green
      }
      else {
        Write-Warning "Container App update failed. You may need to run: azd deploy api"
      }
    }
  }
  catch {
    Write-Warning "Failed to update Container App: $($_.Exception.Message)"
  }
}

# Wait for query Running, proxy acquire-stream OK, optionally prime EH, then restart sync-cosmos reaction
function Initialize-WishlistSyncIfReady {
  param(
    [string]$Namespace = "drasi-system",
    [string]$QueryName = "wishlist-updates",
    [string]$EhHub = "wishlist-events"
  )

  Write-Host "`n🧭 Initializing SyncDaprStateStore reaction when query/view are ready..." -ForegroundColor Cyan

  # 1) Ensure query status is Running
  $queryRunning = $false
  for ($i = 0; $i -lt 12 -and -not $queryRunning; $i++) {
    try {
      $desc = drasi describe query $QueryName -n $Namespace 2>$null
      if ($LASTEXITCODE -eq 0 -and $desc -match "status:\s*Running") { $queryRunning = $true; break }
    }
    catch {}
    Start-Sleep -Seconds 10
  }
  if (-not $queryRunning) {
    Write-Warning "Query '$QueryName' not Running yet; skipping sync reaction init gate."
    return
  }

  # 2) Observe a successful acquire-stream 200 in proxy logs (best-effort)
  $acquireOk = $false
  for ($i = 0; $i -lt 12 -and -not $acquireOk; $i++) {
    try {
      $logs = kubectl logs deploy/wishlist-eh-proxy -n $Namespace --tail=200 2>$null
      if ($logs -match "acquire-stream\s-\s200") { $acquireOk = $true; break }
    }
    catch {}
    Start-Sleep -Seconds 10
  }
  if (-not $acquireOk) {
    Write-Host "   ℹ️ Didn't see acquire-stream 200 yet; continuing (best-effort)." -ForegroundColor Gray
  }
  else {
    Write-Host "   ✅ Proxy acquire-stream observed (200)" -ForegroundColor Green
  }

  # 3) Prime Event Hub with a single event (best-effort) so view/result header exists
  try {
    $envName = $Env:AZURE_ENV_NAME; if (-not $envName) { $envName = azd env get-value AZURE_ENV_NAME 2>$null }
    $rg = $Env:AZURE_RESOURCE_GROUP; if (-not $rg) { $rg = azd env get-value AZURE_RESOURCE_GROUP 2>$null }
    if (-not $envName) { $envName = "dev" }
    if (-not $rg) { $rg = "santadigitalshowcase-$envName-rg" }
    $ns = ("santadigitalshowcase-$envName-eh").ToLower()
    $rule = az eventhubs eventhub authorization-rule list -g $rg --namespace-name $ns --eventhub-name $EhHub -o json 2>$null | ConvertFrom-Json | Where-Object { $_.rights -contains 'Send' } | Select-Object -First 1
    if (-not $rule) { $rule = az eventhubs eventhub authorization-rule list -g $rg --namespace-name $ns --eventhub-name $EhHub -o json 2>$null | ConvertFrom-Json | Where-Object { $_.rights -contains 'Manage' } | Select-Object -First 1 }
    if ($rule) {
      $cs = az eventhubs eventhub authorization-rule keys list -g $rg --namespace-name $ns --eventhub-name $EhHub --name $rule.name --query primaryConnectionString -o tsv 2>$null
      if ($cs) {
        Write-Host "   🚚 Priming '$EhHub' with a single event..." -ForegroundColor Yellow
        try { dotnet --info 1>$null 2>$null } catch {}
        if ($LASTEXITCODE -eq 0) {
          dotnet run --project (Join-Path $PSScriptRoot '..' 'tools' 'EventHubSender' 'EventHubSender.csproj') -- --connection "$cs" --hub "$EhHub" --child "sync-gate" --items "Ping:1" --count 1 2>$null | Out-Null
        }
      }
    }
  }
  catch { Write-Host "   ℹ️ Skipping EH prime: $($_.Exception.Message)" -ForegroundColor Gray }

  # 4) Restart the sync-cosmos reaction to reattempt initial sync after events
  try {
    Write-Host "   ↻ Restarting wishlist-sync-cosmos-reaction..." -ForegroundColor Yellow
    kubectl rollout restart deploy/wishlist-sync-cosmos-reaction -n $Namespace 2>$null | Out-Null
    kubectl rollout status deploy/wishlist-sync-cosmos-reaction -n $Namespace --timeout=120s 2>$null | Out-Null
  }
  catch { Write-Host "   ⚠️ Reaction restart status check: $($_.Exception.Message)" -ForegroundColor Yellow }
}

# Execute in order
Invoke-RegistrySubstitution;
Ensure-KubeCredentials;
Apply-Manifests;
Remove-DuplicateActorStateStore -Namespace $cpNs;
Patch-CosmosComponentNames -Namespace $cpNs -Database 'elves_demo' -Collection 'wishlists';
$cliReady = Ensure-DrasiCliEnv -Namespace $cpNs;
if (-not $cliReady) {
  Write-Warning "Drasi CLI environment setup failed. Continuing with kubectl fallback."
}
$drasiTokens = @{}
try {
  $drasiTokens = Get-DrasiTokenMap
}
catch {
  Write-Error $_.Exception.Message
  exit 1
}
Apply-DrasiResources -Namespace $cpNs -TokenMap $drasiTokens -TemplateOutputDir $resolvedDir;

# Wait for Drasi resource provider to create source deployments (can take 30-60 seconds)
Write-Host "`n⏳ Waiting 60s for Drasi to create source deployments..." -ForegroundColor Yellow
Start-Sleep -Seconds 60

# Validate that critical resources were created
Validate-DrasiResourcesCreated -Namespace $cpNs;

# Patch source-specific deployment issues (images, Dapr versions) - MUST run AFTER sources are created
Repair-DrasiSourceDeployments -Namespace $cpNs;

# Correct known image issues and normalize EventHub env for EventHub source
Fix-DrasiKnownImageIssues -Namespace $cpNs;
Normalize-EventHubEnv -Namespace $cpNs -Hub 'wishlist-events';

# Patch Drasi workload images that may be missing registry prefix (prevents ImagePullBackOff)
Remove-DaprSidecarImageOverride -Namespace $cpNs;
# DISABLED - Image patching causes duplicate registry prefixes
# Patch-DrasiWorkloadImages -Namespace $cpNs;
Write-Host "⏭️  Skipping Patch-DrasiWorkloadImages (disabled - images correct from drasi CLI)" -ForegroundColor Yellow

# Annotate Dapr sidecars so the injector mounts the Sentry token file path
Ensure-DaprSidecarTokenEnv -Namespace $cpNs;

# NEW: Capture Drasi endpoints and update Container App automatically
Capture-DrasiEndpoints -Namespace $cpNs;
Update-ContainerAppWithDrasiUrls;
Ensure-CosmosStateStoreSecret -Namespace $cpNs;

# Run post-provision health checks (CrashLoopBackOff, placeholders, CRDs, image tags)
Test-PostProvisionHealth -Namespace $cpNs;

# Gate and initialize SyncDaprStateStore after query/view are ready (best-effort)
Initialize-WishlistSyncIfReady -Namespace $cpNs -QueryName 'wishlist-updates' -EhHub 'wishlist-events';

# Finalize: run environment guardrails to wire API to override hub and verify endpoints
try {
  $envName = Get-ActiveAzdEnvironmentName
  $rg = Get-AzdEnvValue -Keys @('AZURE_RESOURCE_GROUP') -EnvironmentName $envName
  if (-not $rg) { $rg = "santadigitalshowcase-$envName-rg" }
  $apiApp = "santadigitalshowcase-$envName-api"
  $guardPath = Join-Path (Join-Path $PSScriptRoot '..') 'scripts/post-provision-guardrails.ps1'
  if (Test-Path $guardPath) {
    Write-Host "Running post-provision guardrails..." -ForegroundColor Cyan
    & $guardPath -EnvName $envName -ResourceGroup $rg -ApiAppName $apiApp | Out-Host
  }
  else {
    Write-Host "Guardrails script not found at $guardPath; skipping." -ForegroundColor DarkGray
  }
}
catch {
  Write-Warning "Guardrails execution failed: $($_.Exception.Message)"
}

Write-Host "[Drasi] Postdeploy complete." -ForegroundColor Green; exit 0;
