@description('Azure location')
param location string

@description('azd environment name for resource naming')
param env string

// Removed unused namePrefix after switching to explicit vaultName parameter

@description('Tenant ID for access policies')
param tenantId string = tenant().tenantId


@description('Cosmos endpoint URI')
param cosmosEndpoint string

@description('Cosmos account id')
param cosmosAccountId string

@description('Cosmos database name')
param cosmosDatabase string

@description('Event Hub send rule id')
param eventHubSendRuleId string

@description('Event Hub listen rule id')
param eventHubListenRuleId string

@description('Explicit Key Vault name (precomputed for uniqueness)')
param vaultName string

resource vault 'Microsoft.KeyVault/vaults@2025-05-01' = {
  name: toLower(vaultName)
  location: location
  tags: {
    environment: env
  }
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enablePurgeProtection: true
    enableSoftDelete: true
    // Access policies applied separately to avoid cyclic dependency
    accessPolicies: []
  }
}

// Secrets
resource secretCosmosEndpoint 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  name: 'cosmos-endpoint'
  parent: vault
  properties: {
    value: cosmosEndpoint
  }
}

resource secretCosmosDatabase 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  name: 'cosmos-database'
  parent: vault
  properties: {
    value: cosmosDatabase
  }
}

resource secretCosmosKey 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  name: 'cosmos-key'
  parent: vault
  properties: {
    value: listKeys(cosmosAccountId, '2024-05-15').primaryMasterKey
  }
}

resource secretEventHubSend 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  name: 'eventhub-send'
  parent: vault
  properties: {
    value: listKeys(eventHubSendRuleId, '2025-05-01-preview').primaryConnectionString
  }
}

resource secretEventHubListen 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  name: 'eventhub-listen'
  parent: vault
  properties: {
    value: listKeys(eventHubListenRuleId, '2025-05-01-preview').primaryConnectionString
  }
}

@description('Key Vault URI')
output keyVaultUri string = vault.properties.vaultUri

@description('Key Vault name')
output keyVaultName string = vault.name
