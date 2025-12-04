@description('Location for resources')
param location string = resourceGroup().location

@description('Name prefix for resources')
param namePrefix string

@description('AKS VNet name')
param aksVnetName string

@description('AKS VNet resource group')
param aksVnetResourceGroup string

@description('Tags for resources')
param resourceTags object = {}

// Reference to existing AKS VNet in node resource group
resource aksVnet 'Microsoft.Network/virtualNetworks@2025-01-01' existing = {
  scope: resourceGroup(aksVnetResourceGroup)
  name: aksVnetName
}

// Container Apps VNet and Subnet
resource containerAppsVnet 'Microsoft.Network/virtualNetworks@2025-01-01' = {
  name: '${namePrefix}-cae-vnet'
  location: location
  tags: resourceTags
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.240.0.0/16' // Non-overlapping with AKS VNet (10.224.0.0/12)
      ]
    }
    subnets: [
      {
        name: 'infrastructure-subnet'
        properties: {
          addressPrefix: '10.240.0.0/23' // /23 = 512 IPs (required for Container Apps)
          // NOTE: Do NOT pre-delegate subnet. Container Apps Environment will handle delegation.
        }
      }
    ]
  }
}

// Peering from Container Apps VNet to AKS VNet
resource caeToAksPeering 'Microsoft.Network/virtualNetworks/virtualNetworkPeerings@2025-01-01' = {
  parent: containerAppsVnet
  name: 'cae-to-aks-peering'
  properties: {
    allowVirtualNetworkAccess: true
    allowForwardedTraffic: true
    allowGatewayTransit: false
    useRemoteGateways: false
    remoteVirtualNetwork: {
      id: aksVnet.id
    }
  }
}

// Peering from AKS VNet to Container Apps VNet (requires cross-RG deployment)
module aksToCaePeering 'vnet-peering-remote.bicep' = {
  name: 'aks-to-cae-peering'
  scope: resourceGroup(aksVnetResourceGroup)
  params: {
    sourceVnetName: aksVnetName
    targetVnetId: containerAppsVnet.id
    peeringName: 'aks-to-cae-peering'
  }
}

output vnetId string = containerAppsVnet.id
output infrastructureSubnetId string = containerAppsVnet.properties.subnets[0].id
output vnetName string = containerAppsVnet.name
