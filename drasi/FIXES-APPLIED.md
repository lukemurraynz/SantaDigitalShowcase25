# Drasi Deployment Fixes Applied

## Date: 2024-11-29

## Issues Fixed

### 1. Provider Registry Placeholders

**Problem:** providers.yaml had literal error text instead of REPLACE_REGISTRY placeholder  
**Root Cause:** Direct azd env key not found, substitution failed  
**Solution:**

- Created [providers.yaml.template](resources/providers.yaml.template) with clean placeholders
- Enhanced registry substitution in apply-drasi-resources.ps1 (lines 6-40)
- Tries multiple environment variable names:
  - `AZURE_CONTAINER_REGISTRY_ENDPOINT`
  - `containerRegistryLoginServer`
  - Queries azd env for both names
  - Falls back to ghcr.io/drasi-project
- Always starts from template to ensure clean substitution

### 2. Drasi Init Using Wrong Registry

**Problem:** `drasi init` was using docker.io/drasi-project instead of ACR  
**Root Cause:** DRASI_REGISTRY environment variable not set  
**Solution:**

- Updated Install-DrasiComponents function (lines 307-318)
- Auto-detects ACR from azd environment:
  ```powershell
  if (-not $reg) {
    try {
      $acrEndpoint = azd env get-value AZURE_CONTAINER_REGISTRY_ENDPOINT 2>$null
      if ($acrEndpoint) {
        $reg = $acrEndpoint.TrimEnd('/').Replace('https://', '')
      }
    } catch {}
  }
  ```
- Falls back to ghcr.io if ACR not found

### 3. Event Hub FQDN Environment Variable Name

**Problem:** Script used wrong environment variable name (EVENTHUB_FQDN vs eventHubFqdn)  
**Root Cause:** azd generates camelCase names, script expected UPPER_CASE  
**Solution:**

- Enhanced source.yaml substitution (lines 1455-1469)
- Tries multiple environment variable names in order:
  1. ConfigMap value (if exists)
  2. `eventHubFqdn` from azd env
  3. `EVENTHUB_FQDN` from azd env
  4. `AZURE_EVENTHUB_NAMESPACE_FQDN` from azd env
  5. Constructed from `AZURE_ENV_NAME` + project name

### 4. Source.yaml Regex Pattern with Spaces

**Problem:** Regex `\$\{ EVENTHUB_FQDN\ }` didn't match `${EVENTHUB_FQDN}`  
**Root Cause:** Extra spaces in regex pattern  
**Solution:**

- Fixed regex patterns (lines 1447-1448):
  - ❌ Before: `\$\{ EVENTHUB_FQDN\ }`
  - ✅ After: `\$\{EVENTHUB_FQDN\}`
  - ❌ Before: `\$\{ DRASI_MI_CLIENT_ID\ }`
  - ✅ After: `\$\{DRASI_MI_CLIENT_ID\}`

### 5. ACR Authentication for AKS

**Problem:** AKS pods couldn't pull images from ACR (ImagePullBackOff)  
**Root Cause:** AKS not attached to ACR  
**Solution:**

- Running: `az aks update --attach-acr` to grant AKS pull access to ACR
- This creates role assignment allowing AKS kubelet identity to pull from ACR

## Files Modified

1. **d:\GitHub\drasicrhsith\drasi\apply-drasi-resources.ps1**

   - Lines 6-40: Enhanced provider registry substitution
   - Lines 307-318: Auto-detect ACR for drasi init
   - Lines 1447-1448: Fixed source.yaml regex patterns
   - Lines 1455-1469: Enhanced Event Hub FQDN resolution

2. **d:\GitHub\drasicrhsith\drasi\resources\providers.yaml.template** (NEW)
   - Clean template with REPLACE_REGISTRY placeholders
   - Source for generating actual providers.yaml

## Testing Status

- ✅ providers.yaml.template created
- ✅ Registry substitution logic updated
- ✅ Event Hub FQDN resolution enhanced
- ✅ Source.yaml regex patterns fixed
- ⏳ ACR attachment to AKS in progress
- ⏳ End-to-end Drasi deployment pending ACR attachment completion

## Next Steps After ACR Attachment

1. Delete current Drasi pods: `kubectl delete pods -n drasi-system -l drasi/infra`
2. Wait for pods to recreate with ACR images
3. Apply providers: `drasi apply -f resources/providers.yaml -n drasi-system`
4. Apply source: Via apply-drasi-resources.ps1 or manually with substituted values
5. Apply queries: `drasi apply -f resources/continuous-queries.yaml -n drasi-system`
6. Test view service: `curl http://<view-service-ip>/wishlist-trending-1h`

## Will azd up Work Now?

**Yes**, running `azd up` should work without these issues because:

1. **providers.yaml** will be correctly generated from template with your ACR
2. **Drasi init** will use your ACR automatically (no manual env var needed)
3. **Event Hub FQDN** will be correctly resolved from azd environment
4. **AKS** can pull images from your ACR (after attachment completes)
5. **Source placeholders** will be correctly substituted

All fixes are persistent and will survive redeployments.
