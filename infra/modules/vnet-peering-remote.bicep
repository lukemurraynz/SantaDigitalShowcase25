@description('Name of the source VNet')
param sourceVnetName string

@description('Resource ID of the target VNet')
param targetVnetId string

@description('Name for the peering connection')
param peeringName string

resource sourceVnet 'Microsoft.Network/virtualNetworks@2025-01-01' existing = {
  name: sourceVnetName
}

resource peering 'Microsoft.Network/virtualNetworks/virtualNetworkPeerings@2025-01-01' = {
  parent: sourceVnet
  name: peeringName
  properties: {
    allowVirtualNetworkAccess: true
    allowForwardedTraffic: true
    allowGatewayTransit: false
    useRemoteGateways: false
    remoteVirtualNetwork: {
      id: targetVnetId
    }
  }
}

output peeringId string = peering.id
