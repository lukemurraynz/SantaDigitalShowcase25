@description('Location for resources')
param location string

@description('Name prefix for resources')
param namePrefix string

@description('Cosmos DB account kind (SQL API)')
param accountKind string = 'GlobalDocumentDB'

@description('Cosmos DB consistency policy')
param consistencyPolicy string = 'Session'

var accountName = toLower(format('{0}-cosmos', namePrefix))
var databaseName = 'elves_demo'

resource account 'Microsoft.DocumentDB/databaseAccounts@2025-10-15' = {
  name: accountName
  location: location
  kind: accountKind
  properties: {
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: {
      defaultConsistencyLevel: consistencyPolicy
    }
    capabilities: []
    enableFreeTier: false
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlDb 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2025-10-15' = {
  name: databaseName
  parent: account
  properties: {
    resource: {
      id: databaseName
    }
    options: {
      autoscaleSettings: {
        maxThroughput: 4000
      }
    }
  }
}

// Application containers
var containerNames = [
  'children'
  'events'
  'jobs'
  'wishlists'
  'recommendations'
  'profiles'
  'assessments'
  'notifications'
  'reports'
  'dlq'
]

// Create each container with autoscale throughput and partition key on childId (where applicable)
// NOTE: Adjust partition strategy in future iterations for high-cardinality workloads.
// Containers share the database autoscale budget; can tune individually if needed.
resource containers 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2025-10-15' = [for name in containerNames: {
  name: name
  parent: sqlDb
  properties: {
    resource: {
      id: name
      partitionKey: {
        paths: [ '/childId' ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
        excludedPaths: [
          {
            path: '/"_etag"/?'
          }
        ]
      }
    }
    options: {
      autoscaleSettings: {
        maxThroughput: 1000
      }
    }
  }
}]

// Leases container for change feed processor (partitioned on /id)
resource leases 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2025-10-15' = {
  name: 'leases'
  parent: sqlDb
  properties: {
    resource: {
      id: 'leases'
      partitionKey: {
        paths: [ '/id' ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [ { path: '/*' } ]
        excludedPaths: [ { path: '/"_etag"/?' } ]
      }
    }
    options: {
      autoscaleSettings: {
        maxThroughput: 1000
      }
    }
  }
}

@description('Cosmos DB account endpoint URI')
output endpoint string = account.properties.documentEndpoint

@description('Cosmos SQL database name')
output database string = databaseName

@description('Cosmos DB account id')
output accountId string = account.id

@description('Cosmos DB account name')
output accountName string = account.name
