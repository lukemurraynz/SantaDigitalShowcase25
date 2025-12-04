@description('Location for resources')
param location string

@description('Name prefix for resources')
param namePrefix string

@description('App Service Plan SKU (B1, S1, etc.)')
param planSku string = 'B1'


@description('COSMOS connection string to inject as app setting (if provided)')
@secure()
param cosmosConnectionString string = ''

var planName = toLower(format('{0}-plan', namePrefix))
var appName  = toLower(format('{0}-api', namePrefix))

resource plan 'Microsoft.Web/serverfarms@2025-03-01' = {
  name: planName
  location: location
  sku: {
    name: planSku
    tier: contains(['B1','B2','B3'], planSku) ? 'Basic' : 'Standard'
    size: planSku
    capacity: 1
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource site 'Microsoft.Web/sites@2025-03-01' = {
  name: appName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: plan.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      appSettings: [
        {
          name: 'ASPNETCORE_URLS'
          value: 'http://0.0.0.0:8080'
        }
        {
          name: 'COSMOS_CONNECTION_STRING'
          value: cosmosConnectionString
        }
      ]
    }
    httpsOnly: true
  }
}

@description('App Service default hostname')
output hostname string = site.properties.defaultHostName

@description('App Service resource id')
output siteId string = site.id

@description('App Service name')
output siteName string = site.name
