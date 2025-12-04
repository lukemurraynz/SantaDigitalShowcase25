# Drasi Quick Start

This guide helps you deploy Drasi with EventHub sources on AKS.

## Prerequisites
- AKS cluster with Dapr 1.14.5+
- Azure EventHub namespace
- Drasi CLI 0.10.0-azure-linux
- kubectl configured for your cluster

## Deployment

### 1. Install Drasi
```powershell
cd drasi
.\install-drasi.ps1
```

### 2. Apply Resources (Automated Workarounds Included)
```powershell
.\apply-drasi-resources.ps1
```

This script automatically applies all necessary workarounds for EventHub sources:
- ✅ Fixes Dapr app-id mismatch
- ✅ Adds missing Dapr annotations
- ✅ Creates proxy service for DNS resolution
- ✅ Injects reactivator connection strings

### 3. Verify Deployment
```powershell
# Check continuous queries
drasi list query

# Expected: wishlist-updates shows "Running"

# Check source
drasi list source

# Expected: wishlist-eh shows "Available: true"

# Check proxy service
kubectl get service wishlist-eh-proxy -n drasi-system
kubectl get endpoints wishlist-eh-proxy -n drasi-system

# Expected: Service exists with at least one endpoint
```

## Troubleshooting

If continuous queries show `TerminalError`, see [TROUBLESHOOTING.md](./TROUBLESHOOTING.md) for:
- Known issues and root causes
- Manual fix procedures
- Verification steps
- Architecture notes

## Common Issues

### Continuous Query Fails to Start
**Symptoms**: TerminalError with "app channel is not initialized" or "error sending request"

**Quick Fix**:
```powershell
# Re-run the apply script - it will patch existing deployments
.\apply-drasi-resources.ps1
```

### Reactivator Authentication Fails
**Symptoms**: Reactivator logs show "No credentials provided"

**Quick Fix**:
```powershell
# Reactivator workaround is included in apply script
# Manually verify:
kubectl logs -n drasi-system deployment/wishlist-eh-reactivator --tail=20
```

## Files Overview

- `apply-drasi-resources.ps1` - Main deployment script with automated workarounds
- `TROUBLESHOOTING.md` - Detailed troubleshooting guide
- `sources/eventhub-source.yaml` - EventHub source definition
- `resources/drasi-resources.yaml` - Query container, sources, queries, reactions
- `resources/continuous-queries.yaml` - Simplified continuous query definitions
- `manifests/05-source-proxy-service.yaml` - Proxy service for DNS resolution

## Next Steps

After successful deployment:
1. Send test events to EventHub
2. Verify continuous queries process events
3. Check reactions trigger correctly
4. Monitor Drasi components

## Documentation

- [AKS Setup](./AKS-SETUP.md) - Cluster preparation
- [VNet Integration](./VNET-INTEGRATION.md) - Network configuration
- [API Deployment](./AKS-API-DEPLOYMENT.md) - Backend API setup
- [Troubleshooting](./TROUBLESHOOTING.md) - Issue resolution

---
*For detailed information about EventHub source fixes, see `docs/historical/drasi-eventhub-fixes-2025-11-25.md`*
