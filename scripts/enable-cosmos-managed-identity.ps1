<#
.SYNOPSIS
  Enables Managed Identity (Microsoft Entra ID) for Dapr Cosmos DB state store on AKS using Workload Identity.

.DESCRIPTION
  - Enables AKS OIDC issuer and Workload Identity
  - Creates a User Assigned Managed Identity (UAMI)
  - Assigns "Cosmos DB Built-in Data Contributor" on the Cosmos account scope
  - Creates a Kubernetes ServiceAccount annotated with the UAMI client ID
  - Creates Federated Identity Credential for the SA subject
  - Patches Drasi deployments to use the ServiceAccount and WI pod label
  - Updates Dapr component `cosmos-state` to use `azureClientId` (no key)

.PARAMETER ResourceGroup
  Azure resource group name (e.g., santaworkshop-ae1-rg)

.PARAMETER Project
  Project short name (e.g., santaworkshop)

.PARAMETER Env
  Environment name (e.g., ae1)

.PARAMETER Namespace
  Kubernetes namespace (default: drasi-system)

.EXAMPLE
  ./scripts/enable-cosmos-managed-identity.ps1 -ResourceGroup santaworkshop-ae1-rg -Project santaworkshop -Env ae1
#>

param(
  [Parameter(Mandatory = $true)][string]$ResourceGroup,
  [Parameter(Mandatory = $true)][string]$Project,
  [Parameter(Mandatory = $true)][string]$Env,
  [string]$Namespace = 'drasi-system'
)

$ErrorActionPreference = 'Stop'
$prefix = "$Project-$Env".ToLower()
$aks = "$prefix-aks"
$cosmos = "$prefix-cosmos"
$miName = "$prefix-cosmos-mi"
$saName = 'drasi-cosmos-sa'

Write-Host "🔧 Enabling Managed Identity for Cosmos state store..." -ForegroundColor Cyan

# 1) Enable OIDC issuer and Workload Identity on AKS
Write-Host "Enabling OIDC issuer + Workload Identity on AKS ($aks)..." -ForegroundColor Yellow
az aks update -n $aks -g $ResourceGroup --enable-oidc-issuer --enable-workload-identity 2>$null | Out-Null
$issuer = az aks show -n $aks -g $ResourceGroup --query oidcIssuerProfile.issuerUrl -o tsv
if (-not $issuer) { throw "Failed to retrieve AKS OIDC issuer URL" }
Write-Host "OIDC Issuer: $issuer" -ForegroundColor DarkGray

# 2) Create (or get) User Assigned Managed Identity
Write-Host "Creating/Ensuring UAMI: $miName" -ForegroundColor Yellow
az identity create -g $ResourceGroup -n $miName 2>$null | Out-Null
$mi = az identity show -g $ResourceGroup -n $miName -o json | ConvertFrom-Json
$miClientId = $mi.clientId
$miPrincipalId = $mi.principalId
if (-not $miClientId -or -not $miPrincipalId) { throw "Failed to resolve UAMI identifiers" }
Write-Host "UAMI clientId: $miClientId" -ForegroundColor DarkGray

# 3) Assign Cosmos DB role on account scope
Write-Host "Assigning 'Cosmos DB Built-in Data Contributor' to UAMI on Cosmos account..." -ForegroundColor Yellow
$cosmosId = az cosmosdb show -n $cosmos -g $ResourceGroup --query id -o tsv
if (-not $cosmosId) { throw "Cosmos account not found: $cosmos" }
try {
  # Some subscriptions may not expose the built-in data-plane role via RBAC role assignment API; prefer SQL data-plane role assignments.
  az cosmosdb sql role assignment create --account-name $cosmos --resource-group $ResourceGroup --scope "/" --principal-id $miPrincipalId --role-definition-id "00000000-0000-0000-0000-000000000001" 2>$null | Out-Null
  az cosmosdb sql role assignment create --account-name $cosmos --resource-group $ResourceGroup --scope "/" --principal-id $miPrincipalId --role-definition-id "00000000-0000-0000-0000-000000000002" 2>$null | Out-Null
  Write-Host "✅ Assigned Cosmos SQL data-plane roles (Reader + Contributor) to UAMI" -ForegroundColor Green
}
catch {
  Write-Warning "Data-plane role assignment encountered errors: $($_.Exception.Message). Attempting control-plane fallback."
  az role assignment create --assignee-object-id $miPrincipalId --assignee-principal-type ServicePrincipal --role "Cosmos DB Account Reader Role" --scope $cosmosId 2>$null | Out-Null
  az role assignment create --assignee-object-id $miPrincipalId --assignee-principal-type ServicePrincipal --role "Cosmos DB Operator" --scope $cosmosId 2>$null | Out-Null
}

# 4) Create ServiceAccount annotated for Workload Identity
Write-Host "Creating annotated ServiceAccount: $saName in $Namespace" -ForegroundColor Yellow
$saYaml = @"
apiVersion: v1
kind: ServiceAccount
metadata:
  name: $saName
  namespace: $Namespace
  annotations:
    azure.workload.identity/client-id: $miClientId
"@
$tmpSa = Join-Path $env:TEMP "sa-$saName.yaml"
$saYaml | Set-Content -Path $tmpSa -Encoding UTF8
kubectl apply -f $tmpSa 2>$null | Out-Null

# 5) Create Federated Identity Credential
Write-Host "Creating Federated Identity Credential..." -ForegroundColor Yellow
$subject = "system:serviceaccount:${Namespace}:$saName"
az identity federated-credential create `
  --name "drasi-cosmos-fic" `
  --identity-name $miName `
  --resource-group $ResourceGroup `
  --issuer $issuer `
  --subject $subject `
  --audiences api://AzureADTokenExchange 2>$null | Out-Null

# 6) Patch Drasi deployments to use SA and WI label
Write-Host "Patching Drasi deployments to use ServiceAccount and WI label..." -ForegroundColor Yellow
$patchJson = '{"spec":{"template":{"metadata":{"labels":{"azure.workload.identity/use":"true"}},"spec":{"serviceAccountName":"' + $saName + '"}}}}'
foreach ($d in @('drasi-api', 'drasi-resource-provider')) {
  kubectl patch deploy $d -n $Namespace -p $patchJson 2>$null | Out-Null
}

# 7) Update Dapr component to use azureClientId (remove dependence on key)
Write-Host "Updating Dapr component 'cosmos-state' to use Managed Identity..." -ForegroundColor Yellow
$comp = kubectl get component cosmos-state -n $Namespace -o json 2>$null | ConvertFrom-Json
if ($comp) {
  $meta = @{}
  for ($i = 0; $i -lt $comp.spec.metadata.Count; $i++) { $meta[$comp.spec.metadata[$i].name] = @{ idx = $i; val = $comp.spec.metadata[$i].value } }
  if (-not $meta.ContainsKey('azureClientId')) {
    # append new metadata entry
    $comp.spec.metadata += @{ name = 'azureClientId'; value = $miClientId }
  }
  else {
    $comp.spec.metadata[$meta['azureClientId'].idx].value = $miClientId
  }
  $tmpComp = Join-Path $env:TEMP 'cosmos-state-mi.json'
  ($comp | ConvertTo-Json -Depth 50) | Set-Content -Path $tmpComp -Encoding UTF8
  kubectl apply -f $tmpComp 2>$null | Out-Null
}

Write-Host "✅ Managed Identity configuration applied. Allow a few minutes for RBAC propagation." -ForegroundColor Green

Write-Host "Hint: Restart Drasi pods to pick up SA/labels:" -ForegroundColor DarkGray
Write-Host "  kubectl rollout restart deployment -n $Namespace drasi-api drasi-resource-provider" -ForegroundColor DarkGray
