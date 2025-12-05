# Issue #11 Analysis: Unused Manifests and Resources

## VNet Peering Module - ✅ CONFIRMED RESOLVED

The vnet-peering.bicep file does NOT exist in infra/modules/.

- **Status**: Already cleaned up
- **Action**: None required - issue #11 is resolved

## Drasi Deployment Analysis - COMPLETE

### Deployment Script Flow

Based on comprehensive analysis of `azure.yaml`, `install-drasi.ps1`, and `apply-drasi-resources.ps1`:

1. **azure.yaml** triggers `azd deploy drasi`
2. **drasi/install-drasi.ps1** (predeploy hook):
   - Uses `drasi init` CLI (recommended path)
   - Falls back to manifests if CLI unavailable
   - Applies `manifests/kubernetes-resources.yaml` for Mongo/Redis/Dapr
   - Applies `manifests/02-drasi-infra.yaml` (manifest-based fallback only)
3. **drasi/apply-drasi-resources.ps1** (postdeploy hook):
   - Applies `manifests/kubernetes-resources.yaml` (line 452)
   - Applies `manifests/drasi-resources.yaml` (line 538)
   - Applies ALL `*.yaml` files from `resources/` directory (line 624+)

### ✅ Files ACTUALLY USED by Deployment Scripts

#### drasi/manifests/ (Only 2 files used)

1. **kubernetes-resources.yaml** - Applied by install-drasi.ps1 AND apply-drasi-resources.ps1
2. **drasi-resources.yaml** - Applied by apply-drasi-resources.ps1 line 538
3. **02-drasi-infra.yaml** - Applied by install-drasi.ps1 (manifest fallback only)

#### drasi/resources/ (All YAML files used)

1. **cosmos-state-component.yaml** - Applied by loop at line 624
2. **drasi-resources.yaml** - Applied by loop at line 624
3. **providers.yaml** - Referenced at line 302, applied by loop
4. **providers.yaml.template** - Template source for providers.yaml

### ❌ Files NOT USED by Deployment Scripts (Can be removed)

#### In drasi/manifests/ (9 unused files)

1. **00-namespace.yaml** - NOT referenced
2. **00-dapr.yaml** - NOT referenced
3. **00-kubernetes-resources.yaml** - Placeholder, superseded
4. **01-kubernetes-resources.yaml** - Placeholder, superseded
5. **serviceaccount.yaml** - NOT referenced
6. **secrets-placeholder.yaml** - NOT referenced
7. **21-cosmos-state-secret.yaml** - NOT referenced
8. **05-source-proxy-service.yaml** - NOT referenced
9. **drasi-view-service-deployment.yaml** - NOT referenced
10. **drasi-view-service-lb.yaml** - NOT referenced
11. **signalr-reaction-override.yaml** - NOT referenced
12. **dapr-config.yaml** - Located in drasi/ root, not manifests/

## Conclusion - User is CORRECT ✅

**You are absolutely right!** The vast majority of files in `drasi/manifests/` are NOT used by the deployment scripts. Only 3 files are actually referenced:

✅ **KEEP (3 files):**

- `kubernetes-resources.yaml`
- `drasi-resources.yaml`
- `02-drasi-infra.yaml` (manifest fallback only)

❌ **REMOVE (11 files):**

- All other `.yaml` files in `drasi/manifests/` can be safely removed

The deployment uses:

1. Drasi CLI (`drasi init` + `drasi apply`) as primary path
2. Only 3 manifest files for infrastructure and resources
3. All YAML files from `resources/` directory (which are actively used)
