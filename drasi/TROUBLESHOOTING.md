# Drasi EventHub Source Troubleshooting Guide

## Known Issues and Workarounds

### Issue 1: EventHub Source Unauthorized Access Exception

**Symptom**: Drasi EventHub source fails with `System.UnauthorizedAccessException` when attempting to acquire stream:

```
System.UnauthorizedAccessException: Attempted to perform an unauthorized operation.
For troubleshooting information, see https://aka.ms/azsdk/net/eventhubs/exceptions/troubleshoot
   at Azure.Messaging.EventHubs.AmqpError.ThrowIfErrorResponse(AmqpMessage response, String eventHubName)
```

Continuous queries show `TerminalError` with:

```
Failed to fetch data from source 'wishlist-eh': 502 Bad Gateway Error invoking acquire-stream: InvokeError { message: "Error invoking: 500 Internal Server Error - " }
```

**Root Cause**:

- The Drasi managed identity lacks `Azure Event Hubs Data Receiver` RBAC role on the EventHub namespace
- Azure RBAC role assignments can take 1-5 minutes to propagate after `azd up` completes
- If Drasi source pods attempt to connect before role propagation, they fail with unauthorized errors

**Automatic Prevention**:
The `apply-drasi-resources.ps1` script now includes `Wait-EventHubRbacPropagation` function that:

1. Validates the role assignment exists before applying Drasi source
2. Waits up to 5 minutes with 15-second retries
3. Adds 30 seconds for AAD token propagation after confirmation
4. Prevents race condition between `azd up` and Drasi deployment

**Infrastructure as Code**:
The role assignment is defined in `infra/main.bicep`:

```bicep
resource drasiEhDataReceiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(ehNamespaceNameLocal, 'eh-data-receiver', drasiIdentity.name)
  scope: ehNamespaceExisting
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'a638d3c7-ab3a-418d-83e6-5f17a39d4fde' // Azure Event Hubs Data Receiver
    )
    principalId: drasiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}
```

**Manual Fix** (if needed after initial deployment):

```powershell
# Get values
$rg = "rg-dfdf"
$ehNamespace = "drasicrhsith-dfdf-eh"
$drasiClientId = (azd env get-value DRASI_MI_CLIENT_ID)

# Assign role
az role assignment create `
  --role "Azure Event Hubs Data Receiver" `
  --assignee $drasiClientId `
  --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$rg/providers/Microsoft.EventHub/namespaces/$ehNamespace"

# Wait 2-3 minutes for propagation
Start-Sleep -Seconds 180

# Restart source pod to pick up new permissions
kubectl delete pod -n drasi-system -l drasi/source=wishlist-eh
```

**Verification**:

```powershell
# Check role assignments
az role assignment list --assignee $drasiClientId --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$rg/providers/Microsoft.EventHub/namespaces/$ehNamespace"

# Check source pod logs (should no longer show UnauthorizedAccessException)
kubectl logs -n drasi-system -l drasi/source=wishlist-eh -c source --tail=50
```

### Issue 2: Continuous Query Fails with "app channel is not initialized"

**Symptom**: Continuous query shows `TerminalError` with message:

```
Failed to fetch data from source: could not resolve address for wishlist-eh-proxy-dapr.drasi-system.svc.cluster.local: app channel is not initialized
```

**Root Cause**: Drasi resource provider creates EventHub source deployments with incorrect Dapr configuration:

- Sets `dapr.io/app-id` to `{source-id}-source`
- Query-host invokes proxy as `{source-id}-proxy`
- Missing `dapr.io/app-port` annotation prevents Dapr from initializing app channel

**Solution**: Automatically applied by `apply-drasi-resources.ps1` in `Update-EventHubAuthMode` function:

1. Patches source deployment to use correct app-id: `{source-id}-proxy`
2. Adds `dapr.io/app-port: "80"` annotation
3. Adds `dapr.io/app-protocol: "http"` annotation

**Manual Fix** (if needed):

```powershell
kubectl patch deployment wishlist-eh-source -n drasi-system --type='json' -p='[
  {"op": "replace", "path": "/spec/template/metadata/annotations/dapr.io~1app-id", "value": "wishlist-eh-proxy"},
  {"op": "add", "path": "/spec/template/metadata/annotations/dapr.io~1app-port", "value": "80"},
  {"op": "add", "path": "/spec/template/metadata/annotations/dapr.io~1app-protocol", "value": "http"}
]'
```

### Issue 3: Continuous Query Fails with "error sending request for url"

**Symptom**: Continuous query shows `TerminalError` with message:

```
502 Bad Gateway Error invoking acquire-stream: InvokeError { message: "Error invoking HTTP request: error sending request for url (http://wishlist-eh-proxy/acquire-stream)" }
```

**Root Cause**: Drasi query-api streaming invoker (`HttpStreamingInvoker`) constructs URLs as `http://{app-id}/{path}` expecting Kubernetes DNS resolution, but no Service exists for the proxy. The non-streaming invoker correctly uses Dapr HTTP API (`http://localhost:3500/v1.0/invoke/{app-id}/method/{path}`).

**Solution**: Automatically created by `apply-drasi-resources.ps1` in `Create-SourceProxyService` function. Creates a Kubernetes Service named `{source-id}-proxy` that routes to source pods.

**Manual Fix** (if needed):

```yaml
apiVersion: v1
kind: Service
metadata:
  name: wishlist-eh-proxy
  namespace: drasi-system
spec:
  selector:
    drasi/resource: wishlist-eh
    drasi/service: source
  ports:
    - protocol: TCP
      port: 80
      targetPort: 80
```

### Issue 3: Reactivator Fails with "No credentials provided"

**Symptom**: Reactivator pod logs show:

```
Azure.Identity.CredentialUnavailableException: ManagedIdentityCredential authentication unavailable. No credentials provided.
```

**Root Cause**: Drasi resource provider doesn't properly pass `fallbackConnectionString` secret reference to reactivator deployments. The secret is defined in the Source spec but not injected as environment variable.

**Solution**: Automatically applied by `apply-drasi-resources.ps1` in `Update-EventHubAuthMode` function:

1. Detects reactivator deployments (`*-reactivator`)
2. Retrieves connection string from `drasi-app-secrets` secret
3. Injects as `ConnectionString` environment variable

**Manual Fix** (if needed):

```powershell
$connStr = kubectl get secret drasi-app-secrets -n drasi-system -o jsonpath='{.data.EVENTHUB_CONNECTION}' |
  ForEach-Object { [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($_)) }
kubectl set env deployment/wishlist-eh-reactivator -n drasi-system "ConnectionString=$connStr"
```

## Verification Steps

### 1. Check Source Deployment Annotations

```powershell
kubectl get deployment wishlist-eh-source -n drasi-system -o jsonpath='{.spec.template.metadata.annotations}' | ConvertFrom-Json | Format-List
```

Expected output:

```
dapr.io/app-id        : wishlist-eh-proxy
dapr.io/app-port      : 80
dapr.io/app-protocol  : http
dapr.io/config        : dapr-config
dapr.io/enabled       : true
```

### 2. Check Proxy Service

```powershell
kubectl get service wishlist-eh-proxy -n drasi-system
kubectl get endpoints wishlist-eh-proxy -n drasi-system
```

Expected: Service exists with at least one endpoint (pod IP:80)

### 3. Check Continuous Query Status

```powershell
drasi list query
```

Expected: `wishlist-updates` shows status `Running`

### 4. Test Proxy Endpoint

```powershell
$podName = kubectl get pod -n drasi-system -l drasi/resource=wishlist-eh,drasi/service=source -o jsonpath='{.items[0].metadata.name}'
kubectl port-forward -n drasi-system $podName 8080:80
Invoke-WebRequest -Uri http://localhost:8080/acquire-stream -Method POST -Body '{"query_id":"test"}' -ContentType "application/json"
```

Expected: HTTP 200 OK

## Architecture Notes

### EventHub Source Components

Drasi creates 5 deployments for each EventHub source:

1. **source** (proxy): Provides bootstrap data streaming via `/acquire-stream`
2. **reactivator**: Consumes events from EventHub and republishes to Drasi pubsub
3. **query-api**: Manages continuous query subscriptions
4. **change-dispatcher**: Routes changes to queries
5. **change-router**: Routes query results to reactions

### Dapr Service Invocation Methods

- **Non-streaming** (v1): Uses Dapr HTTP API `http://localhost:3500/v1.0/invoke/{app-id}/method/{path}`
- **Streaming** (v2): Uses direct HTTP with short DNS `http://{app-id}/{path}` (requires Kubernetes Service)

Query-api checks if proxy supports streaming by calling `/supports-stream`. If it returns 204, uses v2 (streaming), otherwise v1.

## Related Files

- `drasi/apply-drasi-resources.ps1`: Automated workarounds
- `drasi/manifests/05-source-proxy-service.yaml`: Proxy service definition
- `drasi/sources/eventhub-source.yaml`: Source configuration
- `drasi/resources/drasi-resources.yaml`: Complete resource definitions
