@description('Azure location')
param location string

@description('Name prefix, e.g., drasi-prod')
@minLength(4)
param namePrefix string


@description('Event Hub name to create')
param hubName string = 'wishlist-events'

@description('SKU for Event Hubs namespace')
@allowed(['Basic', 'Standard', 'Premium'])
param sku string = 'Standard'

@description('Throughput units or premium units capacity')
param capacity int = 1

// Use stable namespace name based on prefix; avoid suffix to keep role assignment references simple
var namespaceName = length(namePrefix) < 4 ? '${namePrefix}-ehns' : '${namePrefix}-eh'

resource ehNamespace 'Microsoft.EventHub/namespaces@2025-05-01-preview' = {
  name: namespaceName
  location: location
  sku: {
    name: sku
    tier: sku
    capacity: capacity
  }
  properties: {
    publicNetworkAccess: 'Enabled'
    isAutoInflateEnabled: false
    kafkaEnabled: false
    minimumTlsVersion: '1.2'
  }
}

resource eventHub 'Microsoft.EventHub/namespaces/eventhubs@2025-05-01-preview' = {
  parent: ehNamespace
  name: hubName
  properties: {
    partitionCount: 2
    messageRetentionInDays: 1
    status: 'Active'
  }
}

// Hub-level auth rules for scoped access
resource hubSendRule 'Microsoft.EventHub/namespaces/eventhubs/authorizationRules@2025-05-01-preview' = {
  parent: eventHub
  name: 'send'
  properties: {
    rights: [
      'Send'
    ]
  }
}

resource hubListenRule 'Microsoft.EventHub/namespaces/eventhubs/authorizationRules@2025-05-01-preview' = {
  parent: eventHub
  name: 'listen'
  properties: {
    rights: [
      'Listen'
    ]
  }
}

@description('Event Hubs namespace name')
output eventHubsNamespaceName string = ehNamespace.name

@description('Event Hub name')
output eventHubName string = hubName

@description('Send auth rule resource id for the event hub')
output sendRuleId string = hubSendRule.id

@description('Listen auth rule resource id for the event hub')
output listenRuleId string = hubListenRule.id
