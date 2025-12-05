@description('Azure location for all resources')
param location string = resourceGroup().location

@description('Short environment name (e.g., dev, prod)')
param env string

@description('Project name used for resource naming')
param project string

@description('Azure OpenAI account name (globally unique). Includes deterministic suffix.')
param openaiName string = toLower(format(
  '{0}-{1}-oai-{2}',
  project,
  env,
  substring(uniqueString(subscription().id, resourceGroup().name, project, env, 'oai'), 0, 6)
))

@description('AKS node count (system pool). Increase if Drasi workloads saturate pod capacity.')
param aksNodeCount int = 2

@description('Model name to deploy for Azure OpenAI.')
param openaiModelName string = 'gpt-4o'

@description('Optional explicit model version (empty = latest/default).')
param openaiModelVersion string = ''

@description('Deployment SKU name (Standard / GlobalStandard / GlobalProvisionedManaged).')
param openaiDeploymentSkuName string = 'Standard'

@description('Deployment capacity (tokens-per-minute or provisioned units).')
param openaiDeploymentCapacity int = 1

@description('Enable public network access for Azure OpenAI (disable for private endpoint only).')
param openaiPublicAccessEnabled bool = true

@description('Enable ACR admin user (false recommended).')
param acrEnableAdminUser bool = false

@description('Log Analytics retention in days (30 default, up to 730).')
param logAnalyticsRetentionDays int = 30

@description('Optional tags applied to resources.')
param resourceTags object = {
  project: project
  env: env
}

@description('Location for Azure OpenAI account (may differ if model unavailable in primary).')
param openaiLocation string = location

@description('Drasi view service public URL (e.g., http://4.195.112.229). Set by azd after AKS LoadBalancer provisions.')
param drasiViewServiceUrl string = ''

@description('Drasi SignalR hub base URL (e.g., http://4.195.112.230). Set by azd after SignalR Reaction gateway provisions.')
param drasiSignalRUrl string = ''

// Naming variables
// Base prefix remains human-readable (project-env)
var basePrefix = toLower(format('{0}-{1}', project, env))
var namePrefix = basePrefix
var ehHubName = 'wishlist-events'
// Cosmos stable; Key Vault unique (soft-delete locks base name) - use longer unique string
var cosmosAccountName = toLower(format('{0}-{1}-cosmos', project, env))
// Use shorter base with longer unique suffix to stay under 24 chars while avoiding soft-delete conflicts
var kvBase = 'drasi${env}'
var keyVaultUnique = substring(uniqueString(subscription().id, resourceGroup().name, deployment().name), 0, 10)
var keyVaultName = toLower('${kvBase}${keyVaultUnique}')

// Effective model properties
var effectiveModelName = openaiModelName
var effectiveModelVersion = empty(openaiModelVersion) ? null : openaiModelVersion
var effectiveCapacity = openaiDeploymentCapacity
var requiresGlobalStandard = (toLower(openaiLocation) == 'australiaeast') && (toLower(openaiDeploymentSkuName) == 'standard')
var finalDeploymentSkuName = requiresGlobalStandard ? 'GlobalStandard' : openaiDeploymentSkuName

// Cosmos module
module cosmos './modules/cosmos.bicep' = {
  name: 'cosmos'
  params: {
    location: location
    namePrefix: namePrefix
  }
}

// Azure OpenAI account
resource openai 'Microsoft.CognitiveServices/accounts@2025-09-01' = {
  name: openaiName
  location: openaiLocation
  kind: 'OpenAI'
  tags: resourceTags
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: openaiName
    publicNetworkAccess: openaiPublicAccessEnabled ? 'Enabled' : 'Disabled'
  }
}

// OpenAI deployment
resource openaiDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-09-01' = {
  parent: openai
  name: effectiveModelName
  properties: {
    model: empty(effectiveModelVersion)
      ? {
          format: 'OpenAI'
          name: effectiveModelName
        }
      : {
          format: 'OpenAI'
          name: effectiveModelName
          version: effectiveModelVersion
        }
  }
  sku: {
    name: finalDeploymentSkuName
    capacity: effectiveCapacity
  }
}

// Log Analytics workspace
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2025-07-01' = {
  name: toLower(format('{0}-law', namePrefix))
  location: location
  tags: resourceTags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: logAnalyticsRetentionDays
    features: {
      legacy: 0
      searchVersion: 1
    }
  }
}

var logAnalyticsKeys = logAnalytics.listKeys()

// Container Apps managed environment WITHOUT VNet integration
// VNet Peering disabled - Container Apps use public Drasi LoadBalancer IP to avoid HTTP 400 errors
resource caEnv 'Microsoft.App/managedEnvironments@2025-07-01' = {
  name: toLower(format('{0}-cae', namePrefix))
  location: location
  tags: resourceTags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalyticsKeys.primarySharedKey
      }
    }
  }
}

// Azure Container Registry (globally unique DNS name)
resource acr 'Microsoft.ContainerRegistry/registries@2025-11-01' = {
  name: toLower(format(
    '{0}{1}acr{2}',
    project,
    env,
    substring(uniqueString(subscription().id, resourceGroup().name, project, env, 'acr'), 0, 4)
  ))
  location: location
  tags: resourceTags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: acrEnableAdminUser
  }
}

// Event Hub namespace + hub via module
module eh './modules/eventhub.bicep' = {
  name: format('eventhub-{0}', uniqueString(deployment().name))
  params: {
    location: location
    namePrefix: namePrefix
    hubName: ehHubName
  }
}

@description('Container image to deploy for the API')
param apiImage string

// API container app module
module api './modules/containerapp.bicep' = {
  name: 'api'
  params: {
    location: location
    namePrefix: namePrefix
    environmentId: caEnv.id
    image: apiImage
    registryServer: acr.properties.loginServer
    acrName: acr.name
    // Inject runtime configuration via environment variables
    keyVaultUri: keyvault.outputs.keyVaultUri
    cosmosEndpoint: cosmos.outputs.endpoint
    openaiEndpoint: openai.properties.endpoint
    openaiDeployment: effectiveModelName
    eventHubNamespaceFqdn: toLower(format('{0}.servicebus.windows.net', eh.outputs.eventHubsNamespaceName))
    eventHubName: eh.outputs.eventHubName
    drasiViewServiceUrl: drasiViewServiceUrl
    drasiSignalRUrl: drasiSignalRUrl
    drasiQueryContainer: 'default'
    // webHost removed - frontend is now served from the same Container App
  }
}

// Environment diagnostics
resource caEnvDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'environment-alllogs'
  scope: caEnv
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

// AKS cluster for Drasi runtime
module aks './modules/aks.bicep' = {
  name: 'aks'
  params: {
    location: location
    namePrefix: namePrefix
    nodeCount: aksNodeCount
  }
}

// Key Vault module (after Event Hub + Cosmos available)
module keyvault './modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    env: env
    vaultName: keyVaultName
    cosmosEndpoint: cosmos.outputs.endpoint
    cosmosAccountId: cosmos.outputs.accountId
    cosmosDatabase: cosmos.outputs.database
    eventHubSendRuleId: eh.outputs.sendRuleId
    eventHubListenRuleId: eh.outputs.listenRuleId
  }
}

// Existing Key Vault resource for RBAC scope
resource keyVaultExisting 'Microsoft.KeyVault/vaults@2025-05-01' existing = {
  name: keyVaultName
}

// Inline role assignments for Container App managed identity
// Use guid with static inputs only (api.name is known at deployment start)
// Grant AcrPull on ACR
resource acrAcrPullAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(uniqueString(subscription().subscriptionId, acr.id, api.name, 'AcrPull'))
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '7f951dda-4ed3-4680-a7ca-43fe172d538d' // AcrPull
    )
    principalId: api.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// Grant Cognitive Services OpenAI User on OpenAI account
// Implicit dependency via api.outputs.principalId ensures identity is created first
resource openaiUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(uniqueString(subscription().subscriptionId, openai.id, api.name, 'OpenAI'))
  scope: openai
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd' // Cognitive Services OpenAI User
    )
    principalId: api.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// Grant Key Vault Secrets User on Key Vault
resource kvSecretsUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(uniqueString(subscription().subscriptionId, keyVaultExisting.id, api.name, 'KV'))
  scope: keyVaultExisting
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roledefinitions',
      '4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User
    )
    principalId: api.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// Grant Cosmos DB Account Contributor ("DB Contributor") to Container App managed identity for data & metadata operations
// Note: Provides broad account management capabilities including access to keys. Evaluate least privilege in production.
// Reference the cosmos account created by the module
resource cosmosAccountExisting 'Microsoft.DocumentDB/databaseAccounts@2025-10-15' existing = {
  name: cosmosAccountName
  dependsOn: [
    cosmos
  ]
}

resource cosmosAccountContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(uniqueString(subscription().subscriptionId, cosmosAccountExisting.id, api.name, 'CosmosCtrl'))
  scope: cosmosAccountExisting
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '5bd9cd88-fe45-4216-938b-f97437e15450' // DocumentDB Account Contributor
    )
    principalId: api.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// Cosmos DB Data Plane RBAC role assignment for Container App
resource cosmosDataContributorAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2025-10-15' = {
  name: guid(uniqueString(subscription().subscriptionId, cosmosAccountExisting.id, api.name, 'CosmosData'))
  parent: cosmosAccountExisting
  properties: {
    roleDefinitionId: resourceId(
      'Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions',
      cosmosAccountName,
      '00000000-0000-0000-0000-000000000002'
    ) // Cosmos DB Built-in Data Contributor (read/write) - https://learn.microsoft.com/azure/cosmos-db/nosql/reference-data-plane-security
    principalId: api.outputs.principalId
    scope: cosmosAccountExisting.id
  }
}

// Event Hub role assignments (scope via module output to enforce dependency ordering)
// Assign Event Hub roles to API managed identity (module depends on EH creation)
module apiEhRoles './modules/eh-roleassignments.bicep' = {
  name: 'eh-roles-api'
  params: {
    namespaceName: eh.outputs.eventHubsNamespaceName
    principalId: api.outputs.principalId
  }
}

// Drasi user-assigned managed identity for AKS ingestion components
resource drasiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: toLower(format('{0}-drasi-id', namePrefix))
  location: location
  tags: resourceTags
}

// Federated identity credential for Event Hub source service account
// Required per https://drasi.io/how-to-guides/configure-sources/configure-azure-eventhub-source/#aks-setup
resource drasiEventHubSourceFederatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  name: 'drasi-eventhub-source'
  parent: drasiIdentity
  properties: {
    issuer: aks.outputs.aksOidcIssuer
    subject: 'system:serviceaccount:drasi-system:source.wishlist-eh'
    audiences: ['api://AzureADTokenExchange']
  }
}

// Assign Event Hubs Data Receiver to Drasi identity (consumes wishlist/recommendation events)
module drasiEhRoles './modules/eh-roleassignments.bicep' = {
  name: 'eh-roles-drasi'
  params: {
    namespaceName: eh.outputs.eventHubsNamespaceName
    principalId: drasiIdentity.properties.principalId
  }
}

// Cosmos DB Data Plane RBAC role assignment for Drasi identity
resource drasiCosmosDataContributorAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2025-10-15' = {
  name: guid(uniqueString(subscription().subscriptionId, cosmosAccountExisting.id, drasiIdentity.name, 'DrasiCosmos'))
  parent: cosmosAccountExisting
  properties: {
    roleDefinitionId: resourceId(
      'Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions',
      cosmosAccountName,
      '00000000-0000-0000-0000-000000000002'
    ) // Cosmos DB Built-in Data Contributor (read/write) - https://learn.microsoft.com/azure/cosmos-db/nosql/reference-data-plane-security
    principalId: drasiIdentity.properties.principalId
    scope: cosmosAccountExisting.id
  }
  dependsOn: [
    cosmos // Ensure database and containers are created before assigning roles
  ]
}

@description('Drasi managed identity info')
output drasiIdentityInfo object = {
  name: drasiIdentity.name
  principalId: drasiIdentity.properties.principalId
  clientId: drasiIdentity.properties.clientId
}

// NOTE: Static Web App module removed - frontend is now served from the API Container App.
// The Dockerfile.multi builds the frontend and copies it to wwwroot, which is served by the API.
// This eliminates the need for SWA linked backends and simplifies the architecture.

// Outputs
@description('Backend API default host name (also serves frontend)')
output eventHubFqdn string = toLower(format('{0}.servicebus.windows.net', eh.outputs.eventHubsNamespaceName))
output apiHost string = api.outputs.hostname

// webHost output removed - frontend is now served from apiHost (same Container App)

@description('Cosmos endpoint and database')
output cosmosInfo object = {
  endpoint: cosmos.outputs.endpoint
  database: cosmos.outputs.database
}

@description('Event Hubs info (safe identifiers)')
output eventHubsInfo object = {
  namespace: eh.outputs.eventHubsNamespaceName
  hub: eh.outputs.eventHubName
}

@description('Key Vault info (safe identifiers)')
output keyVaultInfo object = {
  name: keyvault.outputs.keyVaultName
  uri: keyvault.outputs.keyVaultUri
}

@description('Azure OpenAI endpoint and deployment info')
output openAIInfo object = {
  endpoint: openai.properties.endpoint
  deployment: effectiveModelName
  provisioningState: openaiDeployment.properties.provisioningState
  location: openaiLocation
  sku: finalDeploymentSkuName
  capacity: effectiveCapacity
}

@description('Log Analytics workspace info')
output logAnalyticsInfo object = {
  name: logAnalytics.name
  customerId: logAnalytics.properties.customerId
  workspaceId: logAnalytics.id
}

@description('Container App name for azd')
output CONTAINER_APP_NAME string = api.outputs.name

@description('Container Apps Environment resource id for azd')
output CONTAINER_APPS_ENVIRONMENT_ID string = caEnv.id

@description('Container Registry endpoint for azd')
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = acr.properties.loginServer

@description('Drasi managed identity clientId (explicit for azd env)')
output DRASI_MI_CLIENT_ID string = drasiIdentity.properties.clientId
