# ✅ Drasi Deployment - Complete Checklist

## Issue Resolution Summary

All Drasi EventHub source deployment issues have been identified and fixed. The deployment script now handles all workarounds automatically.

## ✅ Completed Tasks

### 1. Root Cause Analysis
- [x] Identified Dapr app-id mismatch (source vs proxy naming)
- [x] Found missing Dapr app-port annotation
- [x] Discovered streaming invoker DNS resolution issue
- [x] Located reactivator connection string injection bug

### 2. Automated Fixes Implemented
- [x] Enhanced `Update-EventHubAuthMode` function to patch deployments
- [x] Created `Create-SourceProxyService` function for DNS resolution
- [x] Integrated fixes into `apply-drasi-resources.ps1` workflow
- [x] Added automatic reactivator connection string injection

### 3. Documentation Created
- [x] `drasi/TROUBLESHOOTING.md` - Comprehensive troubleshooting guide
- [x] `drasi/QUICK-START.md` - Quick deployment guide
- [x] `drasi/manifests/05-source-proxy-service.yaml` - Service manifest
- [x] `docs/historical/drasi-eventhub-fixes-2025-11-25.md` - Detailed fix summary

### 4. Testing & Verification
- [x] Continuous query shows "Running" status
- [x] EventHub source shows "Available: true"
- [x] Proxy service has valid endpoints
- [x] Dapr annotations correct on all deployments
- [x] DNS resolution working for streaming invocation

## 📋 Deployment Checklist

Use this checklist for future deployments:

### Prerequisites
- [ ] AKS cluster running and accessible
- [ ] Dapr 1.14.5+ installed on cluster
- [ ] Drasi CLI 0.10.0-azure-linux installed
- [ ] Azure EventHub namespace configured
- [ ] Workload identity federated credentials set up
- [ ] kubectl configured for your cluster

### Installation Steps
1. [ ] Run `drasi/install-drasi.ps1` to install Drasi platform
2. [ ] Wait for control plane pods to be ready (2/2 containers)
3. [ ] Run `drasi/apply-drasi-resources.ps1` to deploy resources
4. [ ] Verify all workarounds applied automatically
5. [ ] Check continuous query status: `drasi list query`
6. [ ] Check source status: `drasi list source`
7. [ ] Verify proxy service: `kubectl get service wishlist-eh-proxy -n drasi-system`

### Verification Steps
- [ ] All Drasi control plane pods running (2/2)
- [ ] EventHub source deployment has correct Dapr annotations
- [ ] Proxy service exists with valid endpoints
- [ ] Continuous queries show "Running" status
- [ ] Source shows "Available: true"
- [ ] No TerminalError messages in query list

### If Issues Occur
1. [ ] Check [TROUBLESHOOTING.md](./drasi/TROUBLESHOOTING.md)
2. [ ] Verify Dapr annotations: See "Check Source Deployment Annotations" section
3. [ ] Check proxy service endpoints: `kubectl get endpoints wishlist-eh-proxy -n drasi-system`
4. [ ] Review reactivator logs: `kubectl logs -n drasi-system deployment/wishlist-eh-reactivator`
5. [ ] Re-run apply script: `drasi/apply-drasi-resources.ps1` (idempotent)

## 🔧 Manual Workaround Reference

If automated fixes fail, apply manually:

### Fix 1: Dapr Annotations
```powershell
kubectl patch deployment wishlist-eh-source -n drasi-system --type='json' -p='[
  {"op": "replace", "path": "/spec/template/metadata/annotations/dapr.io~1app-id", "value": "wishlist-eh-proxy"},
  {"op": "add", "path": "/spec/template/metadata/annotations/dapr.io~1app-port", "value": "80"},
  {"op": "add", "path": "/spec/template/metadata/annotations/dapr.io~1app-protocol", "value": "http"}
]'
```

### Fix 2: Proxy Service
```powershell
kubectl apply -f drasi/manifests/05-source-proxy-service.yaml
```

### Fix 3: Reactivator Connection String
```powershell
$connStr = kubectl get secret drasi-app-secrets -n drasi-system -o jsonpath='{.data.EVENTHUB_CONNECTION}' | 
  ForEach-Object { [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($_)) }
kubectl set env deployment/wishlist-eh-reactivator -n drasi-system "ConnectionString=$connStr"
```

## 📁 File Reference

### Modified Files
- `drasi/apply-drasi-resources.ps1` - Enhanced with automated workarounds
- `drasi/sources/eventhub-source.yaml` - EventHub source configuration
- `drasi/resources/drasi-resources.yaml` - Complete Drasi resources
- `drasi/resources/continuous-queries.yaml` - Simplified query definitions

### New Files
- `drasi/TROUBLESHOOTING.md` - Troubleshooting guide
- `drasi/QUICK-START.md` - Quick start guide
- `drasi/manifests/05-source-proxy-service.yaml` - Proxy service manifest
- `docs/historical/drasi-eventhub-fixes-2025-11-25.md` - Fix documentation

## 🎯 Success Criteria

Deployment is successful when:
- ✅ `drasi list query` shows `wishlist-updates: Running`
- ✅ `drasi list source` shows `wishlist-eh: Available: true`
- ✅ `kubectl get service wishlist-eh-proxy -n drasi-system` returns service with endpoints
- ✅ No TerminalError in continuous query status
- ✅ Proxy pod logs show successful `/supports-stream` and `/acquire-stream` invocations

## 📞 Support

For issues not covered in documentation:
1. Check [TROUBLESHOOTING.md](./drasi/TROUBLESHOOTING.md)
2. Review [drasi-eventhub-fixes-2025-11-25.md](./docs/historical/drasi-eventhub-fixes-2025-11-25.md)
3. Check Drasi logs: `kubectl logs -n drasi-system <pod-name>`
4. Review Dapr logs: `kubectl logs -n drasi-system <pod-name> -c daprd`

---
**Status**: All issues resolved ✅
**Last Updated**: November 25, 2025
**Drasi Version**: 0.10.0-azure-linux
