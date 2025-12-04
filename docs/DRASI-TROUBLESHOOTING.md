# Drasi Deployment Troubleshooting Guide

This guide provides comprehensive troubleshooting resources for fixing common Drasi deployment issues in this repository.

> **💡 For complex or ambiguous issues**, consider using the [Troubleshooting Specialist Agent](./TROUBLESHOOTING-AGENT.md) which provides structured Kepner-Tregoe methodology for systematic problem analysis.

## Quick Reference

### Issue Resolution Tools

| Issue                           | Diagnostic Tool                 | Fix Tool                             | Agent                                           |
| ------------------------------- | ------------------------------- | ------------------------------------ | ----------------------------------------------- |
| Pods in CrashLoopBackOff        | `validate-drasi-deployment.ps1` | `fix-drasi-deployment.ps1`           | [Drasi Deployment Specialist](#using-the-agent) |
| Placeholder credentials         | `validate-drasi-deployment.ps1` | `fix-drasi-deployment.ps1`           | [Drasi Deployment Specialist](#using-the-agent) |
| API 400 errors (Cosmos queries) | Container App logs + agent      | Cosmos query syntax fixes            | [Drasi Deployment Specialist](#using-the-agent) |
| CRDs not installed              | `validate-drasi-deployment.ps1` | Manual: `drasi init --local-cluster` | [Drasi Deployment Specialist](#using-the-agent) |
| Database/container mismatches   | `validate-drasi-deployment.ps1` | `fix-drasi-deployment.ps1`           | [Drasi Deployment Specialist](#using-the-agent) |

## Automated Validation & Fixes

### Integrated into `azd up`

The deployment pipeline now automatically:

1. ✅ **Provisions infrastructure** via Bicep
2. ✅ **Deploys API service** (replaces bootstrap image)
3. ✅ **Builds and pushes** Drasi reaction images
4. ✅ **Deploys Drasi** control plane and resources
5. ✅ **Runs validation** checks for common issues
6. ✅ **Auto-fixes** detected problems (if possible)
7. ✅ **Re-validates** after fixes applied

**Location in pipeline**: See [azure.yaml](../azure.yaml) `postprovision` hook

### Manual Validation

To validate deployment health at any time:

```powershell
# Get resource group and environment from azd
$rg = azd env get-value AZURE_RESOURCE_GROUP
$env = azd env get-value AZURE_ENV_NAME

# Run validation
.\scripts\validate-drasi-deployment.ps1 -ResourceGroup $rg -Project "drasicrhsith" -Env $env
```

**Checks performed:**

- ✅ Kubernetes cluster connectivity
- ✅ Cosmos DB secret validation (no placeholders)
- ✅ Dapr component configuration (correct database/container names)
- ✅ Pod health status (CrashLoopBackOff detection)
- ✅ CRD installation completeness
- ✅ Cosmos DB resource existence
- ✅ Resource name consistency

**Exit codes:**

- `0`: All checks passed
- `1`: Errors detected (see output for details)

### Manual Fixes

To apply automated fixes for detected issues:

```powershell
# Get resource group and environment from azd
$rg = azd env get-value AZURE_RESOURCE_GROUP
$env = azd env get-value AZURE_ENV_NAME

# Run auto-fix
.\scripts\fix-drasi-deployment.ps1 -ResourceGroup $rg -Project "drasicrhsith" -Env $env
```

**Fixes applied:**

- ✅ Replaces placeholder credentials with actual Cosmos DB keys
- ✅ Corrects Cosmos DB endpoint (removes incorrect region suffixes)
- ✅ Patches Dapr component with correct database/container names
- ✅ Restarts pods to pick up configuration changes
- ✅ Verifies pod health after restart

**Options:**

- `-SkipRestart`: Apply configuration changes without restarting pods

**Exit codes:**

- `0`: All fixes applied successfully
- `1`: Some fixes failed (manual intervention required)

## Using the Drasi Deployment Specialist Agent

This repository includes an AI agent specifically trained on Drasi deployment issues.

### Activating the Agent

In VS Code with GitHub Copilot:

1. Open Copilot Chat
2. Type `@drasi-deployment-specialist` to activate the agent
3. Describe your issue or use a conversation starter

### Agent Capabilities

The agent can help with:

- 🔍 **Diagnosing** pod crashes, API errors, configuration mismatches
- 🔧 **Fixing** credential issues, query syntax errors, resource misconfigurations
- 📝 **Explaining** why issues occurred and how to prevent them
- 🤖 **Generating** validation and fix scripts for your specific scenario
- 📚 **Providing** best practices for Drasi + Azure deployments

### Conversation Starters

Ask the agent:

- "My Drasi pods are in CrashLoopBackOff after azd up"
- "Container App API returns 400 Bad Request for Cosmos DB queries"
- "Drasi CRDs not found - continuous queries won't deploy"
- "How do I prevent placeholder credentials in future deployments?"
- "Validate my Drasi deployment configuration"

### Example Interaction

```
You: @drasi-deployment-specialist My Drasi pods are crashing with "Unable to connect to Dapr"

Agent: Let me check the Drasi control plane for pod health. I'll retrieve pod
statuses and examine Dapr sidecar logs.

[Runs diagnostic commands]

Based on the Dapr sidecar logs showing "Failed to init component cosmos-state:
decode account key: illegal base64 data at input byte 7", the root cause is a
placeholder credential in the Cosmos DB secret.

I'll fix this by:
1. Retrieving actual Cosmos DB credentials
2. Recreating the cosmos-statestore secret
3. Restarting the Drasi pods

[Provides exact commands to run]
```

## Common Issues & Solutions

### Issue 1: Pods in CrashLoopBackOff

**Symptoms:**

```
NAME                                      READY   STATUS             RESTARTS
drasi-api-7f5f49db49-brqmk               1/2     CrashLoopBackOff   5
drasi-resource-provider-75bb7fd4d-9l7f4  1/2     CrashLoopBackOff   5
```

**Root Cause:** Dapr sidecar cannot initialize due to:

- Placeholder credentials in `cosmos-statestore` secret
- Wrong Cosmos DB endpoint
- Incorrect database/container names in Dapr component

**Diagnostic Command:**

```powershell
kubectl logs -n drasi-system <pod-name> -c daprd
```

**Look for:**

- `"Failed to init component cosmos-state"`
- `"decode account key: illegal base64 data"` → Placeholder in secret
- `"404: NotFound - Owner resource does not exist"` → Wrong endpoint/database/container

**Solution:**

```powershell
# Auto-fix (recommended)
.\scripts\fix-drasi-deployment.ps1 -ResourceGroup $rg -Project "drasicrhsith" -Env $env

# OR Manual fix
$cosmosKey = az cosmosdb keys list --name <account> --resource-group <rg> --query primaryMasterKey -o tsv
$cosmosEndpoint = az cosmosdb show --name <account> --resource-group <rg> --query documentEndpoint -o tsv

kubectl delete secret cosmos-statestore -n drasi-system
kubectl create secret generic cosmos-statestore -n drasi-system `
  --from-literal=endpoint="$cosmosEndpoint" `
  --from-literal=key="$cosmosKey"

kubectl patch component cosmos-state -n drasi-system --type='json' `
  -p='[{"op": "replace", "path": "/spec/metadata/2/value", "value": "elves_demo"},
       {"op": "replace", "path": "/spec/metadata/3/value", "value": "wishlists"}]'

kubectl rollout restart deployment -n drasi-system drasi-api drasi-resource-provider
```

### Issue 2: API Returns 400 Bad Request (Cosmos DB Queries)

**Symptoms:**

```
Response status code does not indicate success: 400 (Bad Request)
```

**Container App Logs:**

```
Syntax error, incorrect syntax near 'HAVING'
```

**Root Cause:** Cosmos DB SQL limitations:

- `HAVING` clause not supported outside subqueries
- Nested `ORDER BY` in subqueries not supported
- Aggregate functions restricted in certain contexts

**Solution:**

Move complex logic to in-memory LINQ operations:

```csharp
// ❌ Before (causes 400):
var query = new QueryDefinition(
    "SELECT c.ChildId, COUNT(1) AS count FROM c GROUP BY c.ChildId HAVING COUNT(1) > 1");

// ✅ After (works):
var query = new QueryDefinition(
    "SELECT c.ChildId, COUNT(1) AS count FROM c GROUP BY c.ChildId");

var results = results.Where(x => (int)x.count > 1);  // Filter in-memory
```

**Ask the agent:** "@drasi-deployment-specialist I'm getting 400 errors from my Cosmos DB queries with HAVING clause"

### Issue 3: CRDs Not Installed

**Symptoms:**

```
error: unable to recognize "resources/continuous-queries.yaml": no matches for kind "ContinuousQuery"
```

**Root Cause:** Drasi CRDs not installed before applying custom resources

**Diagnostic Command:**

```powershell
kubectl get crd | Select-String "drasi.io"
```

**Expected Output:**

```
continuousqueries.drasi.io
sources.drasi.io
reactions.drasi.io
```

**Solution:**

```powershell
# CRDs should be installed by Drasi control plane
# If missing, check Drasi API pod logs:
kubectl logs -n drasi-system <drasi-api-pod>

# Recommended: Reinstall Drasi using CLI (handles CRDs automatically)
drasi init -n drasi-system

# OR apply CRD manifests directly (fallback):
kubectl apply -f https://github.com/drasi-project/drasi-platform/releases/download/v0.10.0/drasi-crds.yaml
```

### Issue 4: Placeholder Credentials After azd up

**Symptoms:**

```
decode account key: illegal base64 data at input byte 7
```

**Root Cause:** The `apply-drasi-resources.ps1` script defaulted to placeholder values when Cosmos DB credentials couldn't be retrieved.

**Prevention:** As of the latest version, the script now **fails early** instead of using placeholders:

```powershell
# Old behavior (DANGEROUS):
if (-not $key) { $key = 'REPLACE_PRIMARY_KEY' }  # ❌ Silently fails later

# New behavior (SAFE):
if (-not $endpoint -or -not $key) {
  Write-Error "❌ FATAL: Cannot retrieve Cosmos DB credentials!"
  throw "Cosmos DB credentials unavailable - refusing to create secret with placeholders"
}
```

**If you encounter this:**

1. Check Azure CLI authentication: `az account show`
2. Verify permissions: `az cosmosdb keys list --name <account> --resource-group <rg>`
3. Run fix script: `.\scripts\fix-drasi-deployment.ps1`

### Issue 5: Port-Forwarding Errors During drasi apply

**Symptoms:**

```
E1204 07:54:59.582469 4324 portforward.go:413] "Unhandled Error" err="an error occurred forwarding 50698 -> 8080"
error forwarding port 8080 to pod: connection refused
panic: lost connection to pod
```

**Root Cause:** The `drasi apply` command uses port-forwarding to communicate with the Drasi API pod. If the API pod is not fully ready or is restarting, the port-forward connection fails.

**Prevention:** As of the latest version, the deployment scripts now:

1. **Wait for Drasi API to be ready** before attempting to apply resources
2. **Retry `drasi apply` up to 3 times** with exponential backoff for transient failures
3. **Re-check API readiness** between retries

**If you encounter this manually:**

```powershell
# Wait for Drasi API to be available
kubectl wait --for=condition=available deployment/drasi-api -n drasi-system --timeout=180s

# Verify pod is running and all containers are ready
kubectl get pods -n drasi-system -l drasi/infra=api

# Then retry drasi apply
drasi apply -f drasi/manifests/drasi-resources.yaml -n drasi-system
```

**For persistent issues:**

1. Check if the Drasi API pod has resource constraints: `kubectl describe pod -n drasi-system -l drasi/infra=api`
2. Verify the cluster has sufficient resources
3. Check for network policy issues that may block port-forwarding

### AKS Configuration Considerations

Port forwarding failures are typically **not** caused by AKS configuration issues. The current AKS setup supports port forwarding without modifications because:

- **Not a private cluster**: The API server has a public endpoint
- **No authorized IP ranges**: No IP restrictions on API server access
- **Azure CNI networking**: Fully supports kubectl port-forwarding

However, if you modify the AKS configuration, these settings could affect port forwarding:

| Configuration | Impact on Port Forwarding |
|---------------|---------------------------|
| `enablePrivateCluster: true` | Requires VPN/ExpressRoute/Bastion to access API server |
| `apiServerAccessProfile.authorizedIpRanges` | Must include your client IP address |
| Network Policies in `drasi-system` namespace | Could block pod-to-pod communication |
| Firewall rules on egress | Only affects outbound traffic, not port-forwarding |
| Azure Policy blocking specific ports | Could prevent container from listening on 8080 |

**If you've configured authorized IP ranges**, add your client IP:

```powershell
# Check current authorized IP ranges
az aks show -g <rg> -n <cluster> --query apiServerAccessProfile.authorizedIpRanges

# Add your IP to authorized ranges
az aks update -g <rg> -n <cluster> --api-server-authorized-ip-ranges "<your-ip>/32,<existing-ranges>"
```

## Prevention Best Practices

### 1. Pre-Deployment Checks

Before running `azd up`:

```powershell
# Check Docker running (if building images locally)
docker info

# Check kubectl configured
kubectl cluster-info

# Check Azure CLI authenticated
az account show

# Check correct subscription
az account set --subscription <subscription-id>
```

### 2. Post-Deployment Validation

After `azd up` completes:

```powershell
# Automated validation (runs in postprovision hook)
# OR run manually:
.\scripts\validate-drasi-deployment.ps1 -ResourceGroup $rg -Project "drasicrhsith" -Env $env
```

### 3. Incremental Testing

Test each layer before proceeding:

```powershell
# 1. Verify Cosmos DB
az cosmosdb show -n <account> -g <rg>

# 2. Verify Dapr control plane
kubectl get pods -n dapr-system

# 3. Verify Drasi control plane
kubectl get pods -n drasi-system

# 4. Verify CRDs
kubectl get crd | Select-String "drasi.io"

# 5. Verify Container App
$fqdn = az containerapp show -n <app-name> -g <rg> --query properties.configuration.ingress.fqdn -o tsv
Invoke-WebRequest "https://$fqdn/health"
```

### 4. Use Validation in CI/CD

Add validation to your deployment pipeline:

```yaml
# Example GitHub Actions workflow
- name: Validate Drasi Deployment
  run: |
    $rg = azd env get-value AZURE_RESOURCE_GROUP
    $env = azd env get-value AZURE_ENV_NAME
    .\scripts\validate-drasi-deployment.ps1 -ResourceGroup $rg -Project "drasicrhsith" -Env $env
  shell: pwsh
```

## Advanced Troubleshooting

### Debugging Pod Initialization

```powershell
# Get pod details
kubectl describe pod -n drasi-system <pod-name>

# Check main container logs
kubectl logs -n drasi-system <pod-name> -c drasi-api

# Check Dapr sidecar logs (often the culprit)
kubectl logs -n drasi-system <pod-name> -c daprd

# Check previous container logs (if pod restarted)
kubectl logs -n drasi-system <pod-name> -c daprd --previous
```

### Inspecting Kubernetes Resources

```powershell
# View secret contents (base64 decoded)
kubectl get secret cosmos-statestore -n drasi-system -o json | `
  ConvertFrom-Json | `
  ForEach-Object {
    $_.data.PSObject.Properties | ForEach-Object {
      "$($_.Name): " + [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($_.Value))
    }
  }

# View Dapr component configuration
kubectl get component cosmos-state -n drasi-system -o yaml

# View continuous queries
kubectl get continuousqueries -n drasi-system
drasi list query -n drasi-system
```

### Container App Diagnostics

```powershell
# Get latest revision
az containerapp revision list -n <app-name> -g <rg> --query "[0].name" -o tsv

# Stream logs in real-time
az containerapp logs show -n <app-name> -g <rg> --follow

# Get recent errors
az containerapp logs show -n <app-name> -g <rg> --tail 50 | `
  Select-String -Pattern "error|Error|400|500" -Context 3

# Check revision history
az containerapp revision list -n <app-name> -g <rg> -o table
```

## Getting Help

### 1. Use the Agent

For interactive troubleshooting:

```
@drasi-deployment-specialist [describe your issue]
```

### 2. Run Diagnostics

```powershell
# Full validation report
.\scripts\validate-drasi-deployment.ps1 -ResourceGroup $rg -Project "drasicrhsith" -Env $env

# Collect diagnostic bundle
kubectl logs -n drasi-system --all-containers --prefix > drasi-logs.txt
az containerapp logs show -n <app-name> -g <rg> --tail 100 > api-logs.txt
```

### 3. Check Documentation

- Drasi Documentation: https://drasi.io/docs
- Azure Container Apps: https://learn.microsoft.com/azure/container-apps/
- Azure Cosmos DB: https://learn.microsoft.com/azure/cosmos-db/
- Dapr Documentation: https://docs.dapr.io/

## Repository-Specific Notes

### Naming Conventions

Resources follow the pattern: `{project}-{env}-{suffix}`

Example for `project=drasicrhsith`, `env=dev`:

- Cosmos DB: `drasicrhsith-dev-cosmos`
- Container App: `drasicrhsith-dev-api`
- Event Hub: `drasicrhsith-dev-eh`
- AKS Cluster: `drasicrhsith-dev-aks`

### Expected Configuration

- **Cosmos DB Database**: `elves_demo`
- **Cosmos DB Container**: `wishlists`
- **Drasi Namespace**: `drasi-system`
- **Event Hub**: `wishlist-events`

### API Authentication

The Container App API requires an `X-Role` header:

```powershell
# Correct
Invoke-WebRequest -Uri "https://<fqdn>/api/v1/drasi/insights" `
  -Headers @{'X-Role'='operator'} -Method GET

# Returns 401 Unauthorized without header
Invoke-WebRequest -Uri "https://<fqdn>/api/v1/drasi/insights" -Method GET
```

## Change Log

### Version 1.1 (2025-12-03)

**Added:**

- ✅ `Wait-DrasiApiReady` function to wait for Drasi API before applying resources
- ✅ Retry mechanism for `drasi apply` (up to 3 attempts with backoff)
- ✅ Port-forwarding error detection and recovery
- ✅ API readiness check after `drasi init` completes
- ✅ Documentation for port-forwarding issues (Issue 5)

**Changed:**

- ✅ `apply-drasi-resources.ps1` now waits for API readiness before applying
- ✅ `install-drasi.ps1` now waits for API deployment to be available
- ✅ Individual resource file applies now have retry logic

**Benefits:**

- 🔄 Automatic recovery from transient port-forwarding failures
- ⏱️ Proper sequencing ensures API is ready before resources are applied
- 📈 More reliable deployments with fewer manual retries needed

### Version 1.0 (2025-11-29)

**Added:**

- ✅ Automated validation script (`validate-drasi-deployment.ps1`)
- ✅ Automated fix script (`fix-drasi-deployment.ps1`)
- ✅ Drasi Deployment Specialist agent (`.agents/drasi-deployment-specialist.json`)
- ✅ Integration into `azd up` pipeline (azure.yaml)
- ✅ Early failure on missing credentials (apply-drasi-resources.ps1)
- ✅ Correct database/container defaults (elves_demo/wishlists)

**Changed:**

- ❌ Removed placeholder credential fallback (`REPLACE_PRIMARY_KEY`)
- ✅ Script now fails with clear error if credentials unavailable

**Benefits:**

- 🎯 Issues caught immediately during deployment
- 🔧 Most issues auto-fixed without manual intervention
- 📚 Comprehensive documentation and agent support
- 🛡️ Prevents silent failures from propagating

---

**Last Updated**: 2025-12-03  
**Maintainer**: Repository team  
**Feedback**: Open an issue or ask @drasi-deployment-specialist
