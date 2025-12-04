@description('Location for resources')
param location string

@description('Name prefix for resources')
param namePrefix string

@description('Managed Environment resource ID')
param environmentId string

@description('Container image to deploy (e.g., myacr.azurecr.io/api:latest)')
param image string

@description('Azure Container Registry login server (e.g., myacr.azurecr.io).')
param registryServer string = ''
@description('Azure Container Registry name (without domain).')
param acrName string = ''

@description('Key Vault URI used by the API to resolve secrets.')
param keyVaultUri string = ''

@description('Azure OpenAI endpoint for the Elf agent.')
param openaiEndpoint string = ''

@description('Azure OpenAI deployment name for the Elf agent.')
param openaiDeployment string = ''

@description('Cosmos DB endpoint (non-secret) injected for runtime MSI access).')
param cosmosEndpoint string = ''

@description('Fully qualified Event Hub namespace (e.g., namespace.servicebus.windows.net)')
param eventHubNamespaceFqdn string = ''

@description('Event Hub name for publishing domain events.')
param eventHubName string = ''

@description('Drasi view service base URL (e.g., http://4.195.112.229). Empty if not yet provisioned.')
param drasiViewServiceUrl string = ''

@description('Drasi SignalR hub base URL (e.g., http://4.195.112.230). Empty if not yet provisioned.')
param drasiSignalRUrl string = ''

@description('Drasi query container name (default: default).')
param drasiQueryContainer string = 'default'

// webHost parameter removed - frontend is now served from the same Container App (same-origin)
// No CORS configuration needed for the frontend

// Deprecated parameters removed; secrets now resolved via Key Vault

var appName = toLower(format('{0}-api', namePrefix))
// Secrets removed for connection strings; all sensitive values pulled via MSI from Key Vault

resource app 'Microsoft.App/containerApps@2025-07-01' = {
  name: appName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    environmentId: environmentId
    configuration: {
      // Use single revision mode - only one revision active at a time
      // This ensures environment variables persist across azd deploy updates
      // Note: Some az CLI updates may flip to Multiple; postdeploy script
      // enforces Single again after setting env vars.
      activeRevisionsMode: 'Single'
      registries: empty(registryServer)
        ? []
        : [
            // Use admin credentials if provided (fallback to MSI pattern later)
            {
              server: registryServer
              username: acrCreds.username
              passwordSecretRef: 'acr-pwd'
            }
          ]
      secrets: empty(registryServer)
        ? []
        : [
            {
              name: 'acr-pwd'
              value: acrCreds.passwords[0].value
            }
          ]
      ingress: {
        external: true
        // App listens on port 80 (see Dockerfile ASPNETCORE_URLS + EXPOSE 80)
        targetPort: 80
        transport: 'auto'
        // CORS removed - frontend is now same-origin (served from wwwroot)
        // Only needed for external API consumers (if any)
        stickySessions: {
          // Required for SignalR - ensures negotiate and transport connections go to same replica
          affinity: 'sticky'
        }
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: {
            cpu: 1
            memory: '2Gi'
          }
          env: concat(
            empty(keyVaultUri)
              ? []
              : [
                  {
                    name: 'KEYVAULT_URI'
                    value: keyVaultUri
                  }
                ],
            empty(cosmosEndpoint)
              ? []
              : [
                  {
                    name: 'COSMOS_ENDPOINT'
                    value: cosmosEndpoint
                  }
                ],
            empty(openaiEndpoint)
              ? []
              : [
                  {
                    name: 'AZURE_OPENAI_ENDPOINT'
                    value: openaiEndpoint
                  }
                ],
            empty(openaiDeployment)
              ? []
              : [
                  {
                    name: 'AZURE_OPENAI_DEPLOYMENT_NAME'
                    value: openaiDeployment
                  }
                ],
            // Event Hub MI publishing support
            (empty(eventHubNamespaceFqdn) || empty(eventHubName))
              ? []
              : [
                  {
                    name: 'EVENTHUB_FQDN'
                    value: eventHubNamespaceFqdn
                  }
                  {
                    name: 'EVENTHUB_NAME'
                    value: eventHubName
                  }
                ],
            // Drasi view service URL (set after AKS LoadBalancer provisions)
            empty(drasiViewServiceUrl)
              ? []
              : [
                  {
                    name: 'DRASI_VIEW_SERVICE_BASE_URL'
                    value: drasiViewServiceUrl
                  }
                  {
                    name: 'DRASI_QUERY_CONTAINER'
                    value: drasiQueryContainer
                  }
                ],
            // Drasi SignalR hub URL (set after SignalR Reaction gateway provisions)
            empty(drasiSignalRUrl)
              ? []
              : [
                  {
                    name: 'DRASI_SIGNALR_BASE_URL'
                    value: drasiSignalRUrl
                  }
                ]
            // WEB_HOST env var removed - frontend is now same-origin (served from wwwroot)
          )
        }
      ]
      scale: {
        // Maintain at least one replica to avoid 504 on first request cold starts
        minReplicas: 1
        maxReplicas: 2
      }
    }
  }
}

// Explicitly disable Container Apps Easy Auth to ensure anonymous access
resource appAuthConfig 'Microsoft.App/containerApps/authConfigs@2025-07-01' = {
  name: 'current'
  parent: app
  properties: {
    platform: {
      enabled: false
    }
  }
}

// Existing ACR resource to list credentials (admin user must be enabled)
resource acrExisting 'Microsoft.ContainerRegistry/registries@2025-11-01' existing = {
  name: acrName
}

var acrCreds = acrExisting.listCredentials()

// Secret for ACR password

@description('Container App default hostname')
output hostname string = app.properties.configuration.ingress.fqdn

@description('Container App name')
output name string = app.name

@description('Container App resource id')
output resourceId string = app.id

@description('Container App managed identity principal id')
output principalId string = app.identity.principalId
