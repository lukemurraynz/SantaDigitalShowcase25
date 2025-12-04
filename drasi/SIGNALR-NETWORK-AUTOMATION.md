# Drasi SignalR Network Configuration - Deployment Automation

## Problem Solved
Container Apps and AKS are on separate networks. ClusterIP services in AKS are not accessible from Container Apps.

## Solution Implemented
Automatically create Kubernetes LoadBalancer for Drasi SignalR gateway during \zd up\ deployment.

## Files Modified

### 1. drasi/apply-drasi-resources.ps1
**Lines ~1295-1330**: Added automatic SignalR LoadBalancer creation
- Deletes any ClusterIP service \wishlist-signalr-gateway\
- Creates LoadBalancer service \wishlist-signalr-lb\ exposing port 8080
- Waits up to 120s for public IP assignment
- Tests connectivity to \/hub/negotiate\ endpoint
- Stores \DRASI_SIGNALR_URL\ in azd environment

**Lines ~1360-1375**: Updated Container App configuration retrieval
- Uses \wishlist-signalr-lb\ service instead of non-existent gateway
- Falls back to retrieving IP if not set earlier

### 2. azure.yaml
**Line 13**: Added \drasiSignalRUrl\ parameter
\\\yaml
drasiSignalRUrl: \
\\\

### 3. infra/main.bicep
**Line 58**: Parameter already exists (no changes needed)
\\\icep
param drasiSignalRUrl string = ''
\\\

### 4. infra/modules/containerapp.bicep
**Lines 156-162**: Parameter already wired (no changes needed)
\\\icep
{
  name: 'DRASI_SIGNALR_BASE_URL'
  value: drasiSignalRUrl
}
\\\

## Deployment Flow (azd up)

1. **Infrastructure**: \zd provision\ creates AKS, Container Apps, Cosmos DB, etc.
2. **Drasi Install**: \drasi/install-drasi.ps1\ runs as predeploy hook
3. **Drasi Resources**: \drasi/apply-drasi-resources.ps1\ runs as postdeploy hook:
   - Applies SignalR reaction
   - Creates LoadBalancer service \wishlist-signalr-lb\
   - Waits for public IP (e.g., 20.167.108.94)
   - Stores \DRASI_SIGNALR_URL=http://20.167.108.94:8080\ in azd env
4. **Container App Update**: Script automatically configures Container App with:
   - \DRASI_VIEW_SERVICE_BASE_URL=http://<view-service-ip>\
   - \DRASI_SIGNALR_BASE_URL=http://<signalr-lb-ip>:8080\
5. **Frontend Deploy**: \zd deploy web\ uses correct backend URLs

## Testing

\\\powershell
# Check LoadBalancer exists
kubectl get svc wishlist-signalr-lb -n drasi-system

# Get public IP
\ = kubectl get svc wishlist-signalr-lb -n drasi-system -o jsonpath='{.status.loadBalancer.ingress[0].ip}'

# Test SignalR negotiate
Invoke-WebRequest -Uri \"http://\:8080/hub/negotiate?negotiateVersion=1\" -Method POST

# Check azd environment
azd env get-values | Select-String \"DRASI\"
\\\

## Architecture

\\\
EventHub → Drasi (AKS) → SignalR Reaction
                           ↓
                  LoadBalancer (public IP)
                           ↓
                  Container Apps API (/hub proxy)
                           ↓
                      Frontend (WebSocket)
\\\

## Benefits

✅ **Automated**: No manual \kubectl expose\ commands needed
✅ **Repeatable**: Works with \zd up\ in any new environment  
✅ **Validated**: Tests SignalR connectivity before proceeding
✅ **Documented**: IP stored in azd environment for transparency
✅ **Bridge**: Solves Container Apps ↔ AKS network isolation

Generated: 2025-11-27 10:02:23
