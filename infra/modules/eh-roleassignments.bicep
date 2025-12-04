// (location not required for role assignments; removed)

@description('Event Hub namespace name')
param namespaceName string

@description('Principal ID to assign roles to')
param principalId string

// Existing reference to scope role assignments
resource ehNamespaceExisting 'Microsoft.EventHub/namespaces@2025-05-01-preview' existing = {
  name: namespaceName
}

// Sender role
resource eventHubDataSenderAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(uniqueString(subscription().subscriptionId, ehNamespaceExisting.id, principalId, 'Sender'))
  scope: ehNamespaceExisting
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '2b629674-e913-4c01-ae53-ef4638d8f975' // Azure Event Hubs Data Sender
    )
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

// Receiver role
resource eventHubDataReceiverAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(uniqueString(subscription().subscriptionId, ehNamespaceExisting.id, principalId, 'Receiver'))
  scope: ehNamespaceExisting
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'a638d3c7-ab3a-418d-83e6-5f17a39d4fde' // Azure Event Hubs Data Receiver
    )
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
