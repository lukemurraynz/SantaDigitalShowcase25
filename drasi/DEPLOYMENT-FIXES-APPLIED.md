# Drasi Deployment Fixes - Summary

## Date: 2025-11-27 09:08:33

## Critical Fixes Applied

### 1. Actor State Store Configuration (FIXED)
**File**: drasi/manifests/02-drasi-infra.yaml
**Issue**: Redis state store had ctorStateStore: "false" which prevented Drasi resource-provider from using actors
**Fix**: Changed to ctorStateStore: "true" in the statestore Component

### 2. Missing PubSub Component (FIXED)
**File**: drasi/manifests/02-drasi-infra.yaml
**Issue**: Continuous queries failed with "pubsub drasi-pubsub is not found"
**Fix**: Added drasi-pubsub Component using Redis

### 3. Image Registry Prefix Issues (AUTOMATED FIX)
**File**: drasi/apply-drasi-resources.ps1
**Issue**: Drasi-generated deployments use images without registry prefix (e.g., drasi-project/...), defaulting to docker.io instead of ghcr.io
**Fix**: Added Fix-DrasiImageRegistries function that:
  - Patches all query container deployments (default-*)
  - Patches all source deployments (wishlist-eh-*)
  - Adds ghcr.io prefix to all images
  - Runs automatically after query container and source application

### 4. Dapr Version Mismatch (AUTOMATED FIX)
**File**: drasi/apply-drasi-resources.ps1
**Issue**: Drasi-generated deployments have hardcoded dapr.io/sidecar-image: daprio/daprd:1.9.0 annotation, but cluster runs Dapr 1.14.5
**Fix**: Added Fix-DrasiDaprVersions function that:
  - Removes hardcoded Dapr sidecar image annotations
  - Allows pods to use cluster default Dapr version
  - Runs automatically after query container and source application

### 5. Service Account Issues (AUTOMATED FIX)
**File**: drasi/apply-drasi-resources.ps1
**Issue**: Source deployments try to use non-existent source.wishlist-eh service account
**Fix**: Existing Update-SourceDeploymentsServiceAccount function now runs automatically

## Deployment Sequence

The apply script now follows this sequence:

1. Apply query container
2. **FIX**: Patch query container deployments (images + Dapr)
3. Apply providers
4. Apply source (with environment variable substitution)
5. **FIX**: Patch source deployments (service account + auth mode + images + Dapr)
6. Wait for source availability
7. Apply continuous queries
8. Apply reactions

## Future Deployments

When running zd up in new environments, these fixes will apply automatically via the postdeploy hook.

No manual intervention should be required for:
- Image registry issues
- Dapr version mismatches  
- Service account configuration
- Actor state store configuration
- PubSub component availability

## Verification Commands

After deployment, verify fixes:

```powershell
# Check all pods are running
kubectl get pods -n drasi-system

# Verify source is available
drasi list source -n drasi-system

# Verify query container is available  
drasi list querycontainer -n drasi-system

# Check continuous queries
drasi list query -n drasi-system

# Verify components exist
kubectl get component -n drasi-system
```

## Known Limitations

1. **API Authentication**: Container App has Azure Static Web Apps authentication enabled
   - API returns 401 for direct access
   - Access via Static Web App frontend works correctly
   
2. **SignalR Reaction**: May fail if SignalR provider not configured (non-critical)

## Files Modified

1. drasi/manifests/02-drasi-infra.yaml - Added pubsub component, confirmed actor state store fix
2. drasi/apply-drasi-resources.ps1 - Added Fix-DrasiImageRegistries and Fix-DrasiDaprVersions functions

