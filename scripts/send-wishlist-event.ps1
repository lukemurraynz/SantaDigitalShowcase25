param(
  [string]$ChildId = "child-quickstart",
  [string]$Items = "Train:1,Drone:1",
  [int]$Count = 1,
  [string]$ResourceGroup,
  [string]$Project = 'drasi',
  [string]$Env = 'prod',
  [string]$Hub = 'wishlist-events'
)

Write-Host "[Sender] Preparing wishlist event(s) for Event Hub '$Hub'..." -ForegroundColor Cyan

$connection = $env:EVENTHUB_CONNECTION
if (-not $connection -and $ResourceGroup) {
  # Derive EH namespace naming rule consistent with config script
  if (($Project + '-' + $Env).Length -lt 4) { $ehNamespace = "$Project-$Env-ehns" } else { $ehNamespace = "$Project-$Env-eh" }
  # Try Key Vault first
  $vault = "$Project-$Env-kv"
  $secret = az keyvault secret show --vault-name $vault --name eventhub-listen --query value -o tsv 2>$null
  if ($secret) { $connection = $secret }
  if (-not $connection) {
    Write-Warning "Key Vault secret unavailable; falling back to Event Hubs auth rule.";
    $connection = az eventhubs eventhub authorization-rule keys list `
      --resource-group $ResourceGroup `
      --namespace-name $ehNamespace `
      --eventhub-name $Hub `
      --name listen `
      --query primaryConnectionString -o tsv
  }
}

if (-not $connection) { throw "EVENTHUB_CONNECTION not set and unable to resolve via Azure." }

Write-Host "Using connection string (length $($connection.Length))" -ForegroundColor DarkGray

dotnet run --project "$(Join-Path $PSScriptRoot '..\tools\EventHubSender\EventHubSender.csproj')" -- --connection "$connection" --hub "$Hub" --child "$ChildId" --items "$Items" --count $Count

Write-Host "[Sender] Done." -ForegroundColor Green