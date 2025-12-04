# VNet Integration: Container Apps ↔ AKS Drasi

## Architecture Overview

```
Azure Container Apps (API)
    ↓ VNet Integration
    ↓
VNet Peering
    ↓
AKS VNet
    ↓
Drasi View Service (default-view-svc.drasi-system.svc.cluster.local)
```

## Solution Components

1. **Container Apps Environment** with VNet integration
2. **AKS VNet** with Drasi running
3. **VNet Peering** between the two networks
4. **Private DNS** for service discovery
5. **Managed Identity** for authentication

## Step-by-Step Implementation

### Step 1: Create VNet for Container Apps (if not exists)

```bash
# Variables
RESOURCE_GROUP="rg-drasic-prod"
LOCATION="australiaeast"
VNET_NAME="vnet-containerapp"
SUBNET_NAME="subnet-containerapp"
AKS_VNET_NAME="<your-aks-vnet-name>"  # Get from: az aks show -n <cluster> -g <rg> --query nodeResourceGroup

# Create VNet for Container Apps
az network vnet create \
  --resource-group $RESOURCE_GROUP \
  --name $VNET_NAME \
  --location $LOCATION \
  --address-prefix 10.1.0.0/16

# Create subnet for Container Apps (requires /23 or larger)
az network vnet subnet create \
  --resource-group $RESOURCE_GROUP \
  --vnet-name $VNET_NAME \
  --name $SUBNET_NAME \
  --address-prefixes 10.1.0.0/23
```

### Step 2: Get AKS VNet Details

```bash
# Get AKS VNet name and resource group
AKS_NAME="<your-aks-cluster-name>"
AKS_RG="<your-aks-resource-group>"

# Get AKS node resource group (where VNet lives)
AKS_NODE_RG=$(az aks show -n $AKS_NAME -g $AKS_RG --query nodeResourceGroup -o tsv)

# Get AKS VNet name
AKS_VNET_NAME=$(az network vnet list -g $AKS_NODE_RG --query "[0].name" -o tsv)

echo "AKS Node RG: $AKS_NODE_RG"
echo "AKS VNet: $AKS_VNET_NAME"
```

### Step 3: Create VNet Peering

```bash
# Get VNet IDs
CONTAINERAPP_VNET_ID=$(az network vnet show -g $RESOURCE_GROUP -n $VNET_NAME --query id -o tsv)
AKS_VNET_ID=$(az network vnet show -g $AKS_NODE_RG -n $AKS_VNET_NAME --query id -o tsv)

# Create peering from Container App VNet to AKS VNet
az network vnet peering create \
  --resource-group $RESOURCE_GROUP \
  --name containerapp-to-aks \
  --vnet-name $VNET_NAME \
  --remote-vnet $AKS_VNET_ID \
  --allow-vnet-access

# Create peering from AKS VNet to Container App VNet
az network vnet peering create \
  --resource-group $AKS_NODE_RG \
  --name aks-to-containerapp \
  --vnet-name $AKS_VNET_NAME \
  --remote-vnet $CONTAINERAPP_VNET_ID \
  --allow-vnet-access
```

### Step 4: Update Container Apps Environment with VNet

```bash
# Get subnet ID
SUBNET_ID=$(az network vnet subnet show \
  --resource-group $RESOURCE_GROUP \
  --vnet-name $VNET_NAME \
  --name $SUBNET_NAME \
  --query id -o tsv)

# Create new Container Apps Environment with VNet
# NOTE: Existing environments can't be updated with VNet - must recreate
ENVIRONMENT_NAME="drasicrhsith-prod-env"

az containerapp env create \
  --name $ENVIRONMENT_NAME \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --infrastructure-subnet-resource-id $SUBNET_ID \
  --internal-only false
```

### Step 5: Deploy Container App to VNet-enabled Environment

```bash
CONTAINER_APP_NAME="drasicrhsith-prod-api"
ACR_NAME="<your-acr-name>"

# Redeploy app to new environment
az containerapp create \
  --name $CONTAINER_APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --environment $ENVIRONMENT_NAME \
  --image $ACR_NAME.azurecr.io/santa-api:latest \
  --target-port 80 \
  --ingress external \
  --registry-server $ACR_NAME.azurecr.io \
  --registry-identity system \
  --env-vars \
    COSMOS_ENDPOINT=https://drasicrhsith-prod-cosmos.documents.azure.com:443/ \
    AZURE_OPENAI_ENDPOINT=https://drasicrhsith-prod-oai-74hjkh.openai.azure.com/ \
    AZURE_OPENAI_DEPLOYMENT_NAME=gpt-4.1 \
    EVENTHUB_FQDN=drasicrhsith-prod-eh.servicebus.windows.net \
    EVENTHUB_NAME=wishlist-events \
    KEYVAULT_URI=https://drasicrhsithprodkvn72hjd.vault.azure.net/ \
    DRASI_QUERY_CONTAINER=default
```

## Step 6: Configure DNS Resolution for Kubernetes Services

Container Apps needs to resolve Kubernetes internal DNS names like `default-view-svc.drasi-system.svc.cluster.local`.

### Option A: Use Kubernetes ClusterIP directly (Simplest)

Get the ClusterIP of Drasi view service:

```bash
kubectl get svc -n drasi-system default-view-svc -o jsonpath='{.spec.clusterIP}'
# Example output: 10.0.123.45
```

Update your API configuration to use the IP directly:

```bash
az containerapp update \
  --name $CONTAINER_APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --set-env-vars DRASI_VIEW_SERVICE_URL=http://10.0.123.45
```

Then update `DrasiViewClient.cs`:

```csharp
builder.Services.AddHttpClient<IDrasiViewClient, DrasiViewClient>(client =>
{
    var drasiUrl = Environment.GetEnvironmentVariable("DRASI_VIEW_SERVICE_URL") 
        ?? "http://default-view-svc.drasi-system.svc.cluster.local";
    client.BaseAddress = new Uri(drasiUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

### Option B: Expose via LoadBalancer (For multiple services)

Create a LoadBalancer service for Drasi view:

```yaml
apiVersion: v1
kind: Service
metadata:
  name: default-view-svc-lb
  namespace: drasi-system
  annotations:
    service.beta.kubernetes.io/azure-load-balancer-internal: "true"
spec:
  type: LoadBalancer
  ports:
  - port: 80
    targetPort: 8080
  selector:
    # Match the same selector as default-view-svc
```

Apply and get the internal IP:

```bash
kubectl apply -f drasi-view-loadbalancer.yaml
kubectl get svc -n drasi-system default-view-svc-lb -o jsonpath='{.status.loadBalancer.ingress[0].ip}'
```

## Step 7: Update API Configuration

Update `appsettings.json` or Container App environment variables:

```json
{
  "Drasi": {
    "QueryContainer": "default",
    "ViewServiceBaseUrl": "http://10.0.123.45"
  }
}
```

Or via environment variable:

```bash
az containerapp update \
  --name $CONTAINER_APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --set-env-vars \
    DRASI_QUERY_CONTAINER=default \
    DRASI_VIEW_SERVICE_BASE_URL=http://10.0.123.45
```

## Step 8: Update DrasiViewClient to Use Base URL

Update `src/services/DrasiViewClient.cs`:

```csharp
public class DrasiViewClient : IDrasiViewClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DrasiViewClient> _logger;
    private readonly IConfiguration _config;
    
    public DrasiViewClient(HttpClient httpClient, ILogger<DrasiViewClient> logger, IConfiguration config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config;
    }

    public async Task<List<JsonNode>> GetCurrentResultAsync(string queryContainerId, string queryId, CancellationToken ct = default)
    {
        var results = new List<JsonNode>();
        
        try
        {
            // Use base URL from config if provided, otherwise build from container ID
            var baseUrl = _config["Drasi:ViewServiceBaseUrl"] 
                ?? Environment.GetEnvironmentVariable("DRASI_VIEW_SERVICE_BASE_URL");
            
            var url = !string.IsNullOrEmpty(baseUrl)
                ? $"{baseUrl}/{queryId}"
                : $"http://{queryContainerId}-view-svc/{queryId}";
            
            _logger.LogInformation("Querying Drasi view service: {Url}", url);
            // ... rest of implementation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying Drasi for {QueryId}", queryId);
        }

        return results;
    }
}
```

## Testing Connectivity

### Test 1: Check VNet Peering

```bash
# Check peering status (should be "Connected")
az network vnet peering show \
  --resource-group $RESOURCE_GROUP \
  --vnet-name $VNET_NAME \
  --name containerapp-to-aks \
  --query peeringState
```

### Test 2: Test from Container App Console

```bash
# Open Container App console
az containerapp exec \
  --name $CONTAINER_APP_NAME \
  --resource-group $RESOURCE_GROUP

# Inside the container, test connectivity
curl http://10.0.123.45/wishlist-trending-1h
# Should return JSON with Drasi query results
```

### Test 3: Check API Logs

```bash
az containerapp logs show \
  --name $CONTAINER_APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --tail 50 \
  | grep "Drasi"
```

Expected log output:
```
Querying Drasi view service: http://10.0.123.45/wishlist-trending-1h
Retrieved 15 results from Drasi query wishlist-trending-1h
```

### Test 4: Test API Endpoint

```bash
curl https://drasicrhsith-prod-api.icyglacier-0f442c06.australiaeast.azurecontainerapps.io/api/v1/drasi/insights
```

Expected response with real data:
```json
{
  "trending": [
    {"item": "LEGO Space Station", "frequency": 15}
  ],
  "duplicates": [...],
  "inactiveChildren": [...],
  "stats": {...}
}
```

## Network Security

### Network Security Groups (NSGs)

Container App subnet should allow outbound to AKS subnet:

```bash
# Create NSG rule allowing Container Apps to reach AKS
az network nsg rule create \
  --resource-group $RESOURCE_GROUP \
  --nsg-name <containerapp-subnet-nsg> \
  --name AllowContainerAppToAKS \
  --priority 100 \
  --source-address-prefixes 10.1.0.0/23 \
  --destination-address-prefixes <aks-subnet-cidr> \
  --destination-port-ranges 80 443 \
  --direction Outbound \
  --access Allow \
  --protocol Tcp
```

### Firewall Rules

If using Azure Firewall, allow traffic between VNets:

```bash
# Allow Container Apps subnet to AKS subnet
az network firewall network-rule create \
  --collection-name containerapp-to-aks \
  --destination-addresses <aks-subnet-cidr> \
  --destination-ports 80 443 \
  --firewall-name <firewall-name> \
  --name allow-drasi \
  --protocols TCP \
  --resource-group $RESOURCE_GROUP \
  --source-addresses 10.1.0.0/23 \
  --priority 100 \
  --action Allow
```

## Managed Identity for Azure Services

Container Apps already uses managed identity for:
- ✅ Cosmos DB (via `CosmosClient` with DefaultAzureCredential)
- ✅ Key Vault (via `KEYVAULT_URI`)
- ✅ Event Hub (via `EVENTHUB_FQDN`)
- ✅ OpenAI (via `AZURE_OPENAI_ENDPOINT`)

No additional identity configuration needed for Drasi (it's HTTP-only).

## Cost Considerations

- **VNet Peering**: ~$0.01 per GB transferred (very low for API calls)
- **Container Apps VNet**: No additional cost
- **Internal Load Balancer**: ~$0.025/hour (~$18/month) if using Option B
- **Private DNS**: ~$0.50/million queries

**Recommended**: Use Option A (ClusterIP directly) to avoid Load Balancer costs.

## Troubleshooting

### Issue: Container App can't reach Drasi

**Check peering status:**
```bash
az network vnet peering list -g $RESOURCE_GROUP --vnet-name $VNET_NAME -o table
```

**Check route tables:**
```bash
# From Container App console
traceroute 10.0.123.45
```

**Check NSG rules:**
```bash
az network nsg rule list --nsg-name <nsg-name> -g $RESOURCE_GROUP -o table
```

### Issue: DNS resolution failing

**Solution**: Use ClusterIP directly instead of DNS name.

### Issue: Connection timeout

**Check AKS network policy:**
```bash
kubectl get networkpolicies -n drasi-system
```

If network policies exist, add rule to allow Container App subnet:

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: allow-containerapp
  namespace: drasi-system
spec:
  podSelector:
    matchLabels:
      app: default-view
  ingress:
  - from:
    - ipBlock:
        cidr: 10.1.0.0/23  # Container App subnet
    ports:
    - protocol: TCP
      port: 8080
```

## Alternative: Azure Private Link (Future)

For enhanced security, consider Azure Private Link:

1. Expose AKS services via Private Link Service
2. Create Private Endpoint in Container App VNet
3. Automatic private DNS resolution
4. No VNet peering needed

**Cost**: ~$7.30/month per Private Endpoint

## Summary

**Recommended Approach:**
1. ✅ Create VNet for Container Apps
2. ✅ Peer with AKS VNet
3. ✅ Get Drasi view service ClusterIP
4. ✅ Update API to use ClusterIP directly
5. ✅ Test connectivity
6. ✅ Deploy and verify

**Deployment Time**: ~30 minutes

**Benefits:**
- Keep Container Apps for API (simpler management)
- Keep Drasi on AKS (optimal placement)
- Private networking (no internet exposure)
- Managed identities for Azure services
- Low cost (only peering charges)
- Simple to troubleshoot

**Next Steps:**
1. Run the commands in order
2. Update API configuration with ClusterIP
3. Deploy updated API
4. Test end-to-end
