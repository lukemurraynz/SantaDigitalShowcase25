# Container App Deployment Best Practices

## Issue: Environment Variables Not Persisting After \zd deploy api\

### Root Cause
When running \zd deploy api\, Azure Developer CLI:
1. Builds the Docker image
2. Pushes it to ACR
3. Updates the Container App with the new image **only**
4. **Does NOT re-run the Bicep template**

This means environment variables defined in the Bicep template (like \COSMOS_ENDPOINT\) are not automatically applied to new revisions created by \zd deploy api\.

### What Happened
- Initial \zd up\ correctly deployed Container App with all environment variables from Bicep
- Subsequent \zd deploy api\ commands created new revisions without environment variables
- Backend crashed with "Cosmos configuration missing" error
- Manual fix required: \z containerapp update --set-env-vars\

### Solution Implemented
1. **Set activeRevisionsMode to 'Single'** in \infra/modules/containerapp.bicep\
   - Ensures only one active revision at a time
   - Environment variables should persist across image updates

2. **Use Managed Identity for Cosmos DB**
   - Environment variable: \COSMOS_ENDPOINT\ (not connection strings)
   - Container App uses System Assigned Managed Identity
   - RBAC role assigned: Cosmos DB Built-in Data Contributor

### Best Practices for Future Deployments

#### Option 1: Always Use \zd up\ (Recommended)
\\\powershell
# Full infrastructure + code deployment
azd up
\\\
This ensures:
- Bicep template is executed
- Environment variables are set
- All infrastructure is in sync

#### Option 2: Use \zd deploy\ with Caution
\\\powershell
# Deploy only the API service
azd deploy api
\\\
**Note**: This may not apply environment variables from Bicep. After first \zd deploy api\, verify:
\\\powershell
az containerapp show -n <app-name> -g <resource-group> --query 'properties.template.containers[0].env' -o table
\\\

#### Option 3: Manual Environment Variable Verification
After any \zd deploy api\, check if environment variables are present:
\\\powershell
$appName = "drasicrhsith-${AZURE_ENV_NAME}-api"
$rgName = "rg-${AZURE_ENV_NAME}"

# Verify COSMOS_ENDPOINT exists
$envVars = az containerapp show -n $appName -g $rgName --query 'properties.template.containers[0].env' | ConvertFrom-Json
if (-not ($envVars | Where-Object { $_.name -eq 'COSMOS_ENDPOINT' })) {
    Write-Warning "COSMOS_ENDPOINT is missing! Running azd provision to fix..."
    azd provision
}
\\\

### Required Environment Variables for This App
- \COSMOS_ENDPOINT\ - Cosmos DB endpoint for Managed Identity auth
- \DRASI_SIGNALR_BASE_URL\ - Drasi SignalR hub URL
- \KEYVAULT_URI\ - Key Vault URI for secrets
- \AZURE_OPENAI_ENDPOINT\ - Azure OpenAI endpoint
- \AZURE_OPENAI_DEPLOYMENT_NAME\ - OpenAI deployment name
- \EVENTHUB_FQDN\ - Event Hub namespace FQDN
- \EVENTHUB_NAME\ - Event Hub name
- \DRASI_VIEW_SERVICE_BASE_URL\ - Drasi view service URL
- \WEB_HOST\ - Static Web App hostname for CORS

### Testing After Deployment
\\\powershell
# Test backend API
$apiUrl = azd env get-value apiUrl
Invoke-WebRequest -Uri "$apiUrl/api/v1/ping" -Method GET

# Test frontend
$webUrl = azd env get-value webUrl
Invoke-WebRequest -Uri "$webUrl" -Method GET

# Test frontend -> backend proxy
Invoke-WebRequest -Uri "$webUrl/api/v1/ping" -Method GET
\\\

### Recovery Steps if Environment Variables Are Missing
\\\powershell
# Option 1: Re-provision infrastructure (applies Bicep)
azd provision

# Option 2: Manual fix (if you know the values)
$cosmosEndpoint = az cosmosdb show -n <cosmos-name> -g <rg-name> --query 'documentEndpoint' -o tsv
az containerapp update -n <app-name> -g <rg-name> \
  --set-env-vars "COSMOS_ENDPOINT=$cosmosEndpoint" \
  "DRASI_SIGNALR_BASE_URL=<signalr-url>"
\\\

### Key Takeaway
**For new environments, always use \zd up\ or \zd provision\ after \zd deploy api\ to ensure environment variables are properly configured.**
