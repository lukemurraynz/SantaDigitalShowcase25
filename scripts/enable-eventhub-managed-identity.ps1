# Ensures Drasi managed identity has RBAC on Event Hubs namespace for data access
param(
    [string] $ResourceGroup,
    [string] $NamespaceName,
    [string] $ManagedIdentityName = 'drasi-mi'
)

Write-Host "Assigning Event Hubs RBAC to managed identity '$ManagedIdentityName' on namespace '$NamespaceName' in RG '$ResourceGroup'" -ForegroundColor Cyan

$nsId = "/subscriptions/$((az account show --query id -o tsv))/resourceGroups/$ResourceGroup/providers/Microsoft.EventHub/namespaces/$NamespaceName"

$mi = az identity show -g $ResourceGroup -n $ManagedIdentityName --query "{principalId:principalId,name:name}" -o json | ConvertFrom-Json
if (-not $mi) { throw "Managed identity '$ManagedIdentityName' not found in RG '$ResourceGroup'" }

$principalId = $mi.principalId

az role assignment create --assignee-object-id $principalId --assignee-principal-type ServicePrincipal --role "Azure Event Hubs Data Receiver" --scope $nsId | Out-Null
az role assignment create --assignee-object-id $principalId --assignee-principal-type ServicePrincipal --role "Azure Event Hubs Data Owner" --scope $nsId | Out-Null

Write-Host "RBAC assignments completed: Data Receiver, Data Owner" -ForegroundColor Green