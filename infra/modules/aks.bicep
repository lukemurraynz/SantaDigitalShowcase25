@description('Location for AKS cluster.')
param location string

@description('Name prefix (project-env) used to derive cluster name.')
param namePrefix string

@description('Kubernetes version (empty for default).')
param k8sVersion string = ''

@description('Node size for system pool.')
param nodeVmSize string = 'Standard_D4s_v5'

@description('Node count for system pool.')
param nodeCount int = 1

var clusterName = toLower(format('{0}-aks', namePrefix))

resource managedCluster 'Microsoft.ContainerService/managedClusters@2025-09-01' = {
  name: clusterName
  location: location
  sku: {
    name: 'Base'
    tier: 'Free'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    kubernetesVersion: empty(k8sVersion) ? null : k8sVersion
    dnsPrefix: replace(clusterName, '-', '')
    enableRBAC: true
    // Enable OIDC issuer for Azure AD workload identity federation
    oidcIssuerProfile: {
      enabled: true
    }
    // Enable workload identity (required for Azure AD workload identity)
    securityProfile: {
      workloadIdentity: {
        enabled: true
      }
    }
    networkProfile: {
      networkPlugin: 'azure'
      loadBalancerSku: 'standard'
      outboundType: 'managedNATGateway'
    }
    agentPoolProfiles: [
      {
        name: 'system'
        vmSize: nodeVmSize
        count: nodeCount
        osType: 'Linux'
        mode: 'System'
        type: 'VirtualMachineScaleSets'
        orchestratorVersion: empty(k8sVersion) ? null : k8sVersion
      }
    ]
  }
  tags: {
    'azd-service-name': 'drasi'
  }
}

// Get the node resource group name (created by AKS)
var nodeResourceGroupName = managedCluster.properties.nodeResourceGroup

// The VNet name in the node RG follows pattern: aks-vnet-<uniqueString>
// We need to look it up using the existing resource without knowing the exact suffix
// For now, pass the resource IDs that the peering module can use to query
@description('AKS cluster name')
output aksName string = managedCluster.name

@description('AKS cluster resource id')
output aksId string = managedCluster.id

@description('AKS OIDC issuer URL for workload identity federation')
output aksOidcIssuer string = managedCluster.properties.oidcIssuerProfile.issuerURL

@description('AKS node resource group name')
output nodeResourceGroup string = nodeResourceGroupName
