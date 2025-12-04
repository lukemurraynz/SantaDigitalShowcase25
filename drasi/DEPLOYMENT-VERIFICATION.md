# Deployment Verification Checklist

After running \zd up\ on a new environment, verify the fix is working:

## 1. Check Role Assignment Created
\\\powershell
$rg = (azd env get-value AZURE_RESOURCE_GROUP)
$ehNamespace = (azd env get-value eventHubFqdn) -replace '\.servicebus\.windows\.net$', ''
$drasiClientId = (azd env get-value DRASI_MI_CLIENT_ID)

az role assignment list \
  --assignee $drasiClientId \
  --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$rg/providers/Microsoft.EventHub/namespaces/$ehNamespace" \
  --query "[?roleDefinitionName=='Azure Event Hubs Data Receiver']" \
  -o table
\\\

Expected: One role assignment row showing 'Azure Event Hubs Data Receiver'

## 2. Verify Wait Function Executed
Check apply-drasi-resources.ps1 output for:
\\\
[Drasi] Waiting for EventHub RBAC role assignment propagation (MI: <clientId>, EH: <fqdn>)
✓ EventHub RBAC role assignment confirmed (attempt X)
Allowing additional 30s for AAD token propagation...
\\\

## 3. Verify Drasi Source Success
\\\powershell
# Check source status
drasi describe source wishlist-eh -n drasi-system

# Check source pod logs (should NOT contain UnauthorizedAccessException)
kubectl logs -n drasi-system -l drasi/source=wishlist-eh -c source --tail=100 | Select-String "Unauthorized|UnauthorizedAccessException"
\\\

Expected: 
- Source status: available: true
- No UnauthorizedAccessException in logs
- HTTP 200 responses on acquire-stream requests

## 4. Verify Continuous Queries Work
\\\powershell
# List queries
drasi list continuousquery -n drasi-system

# Check for errors
drasi describe continuousquery wishlist-trending-1h -n drasi-system | Select-String "status:|errorMessage:"
\\\

Expected:
- All queries show status: Running (after bootstrap)
- No TerminalError or 502 Bad Gateway errors
- errorMessage empty or related to data structure (not RBAC)

## If Issues Occur
1. Wait 5 minutes for RBAC propagation
2. Restart source pod: \kubectl delete pod -n drasi-system -l drasi/source=wishlist-eh\
3. Check TROUBLESHOOTING.md Issue #1 for manual fix steps

