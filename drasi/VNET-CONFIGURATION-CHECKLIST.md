# VNet Integration Configuration Checklist

**Purpose**: Enable Azure Container Apps to access Drasi view service running in AKS cluster via VNet peering.

**Estimated Time**: 30-45 minutes  
**Prerequisites**: Azure CLI authenticated, contributor access to subscription, kubectl configured for AKS cluster

---

## ✅ Pre-Implementation Checklist

### Information Gathering (5 minutes)

- [ ] **Get AKS VNet Details**
  ```bash
  # Get AKS node resource group
  az aks show -n <AKS_CLUSTER_NAME> -g <RESOURCE_GROUP> --query nodeResourceGroup -o tsv
  
  # Get AKS VNet name and CIDR
  az network vnet list -g <NODE_RESOURCE_GROUP> --query "[0].[name,addressSpace.addressPrefixes[0]]" -o tsv
  # Example output: aks-vnet-12345, 10.0.0.0/16
  ```

- [ ] **Get Drasi View Service ClusterIP**
  ```bash
  kubectl get svc -n drasi-system default-view-svc -o jsonpath='{.spec.clusterIP}'
  # Example output: 10.0.123.45
  # Record this IP - you'll need it for Step 8
  ```

- [ ] **Get Container Apps Environment Details**
  ```bash
  az containerapp env show -n <ENV_NAME> -g <RESOURCE_GROUP> --query "[name,location]" -o tsv
  ```

- [ ] **Choose Non-Overlapping CIDR** for Container Apps VNet
  - AKS VNet: `10.0.0.0/16` (example)
  - Container Apps VNet: `10.1.0.0/16` ✅ (no overlap)
  - Container Apps Subnet: `10.1.0.0/23` (512 IPs required)

---

## 🚀 Implementation Steps

### Step 1: Create VNet for Container Apps (5 minutes)

- [ ] **Create VNet**
  ```bash
  RESOURCE_GROUP="<YOUR_RESOURCE_GROUP>"
  LOCATION="<YOUR_LOCATION>"  # e.g., eastus
  VNET_NAME="containerapp-vnet"
  
  az network vnet create \
    -g $RESOURCE_GROUP \
    -n $VNET_NAME \
    -l $LOCATION \
    --address-prefix 10.1.0.0/16
  ```

- [ ] **Create Subnet for Container Apps**
  ```bash
  SUBNET_NAME="containerapp-subnet"
  
  az network vnet subnet create \
    -g $RESOURCE_GROUP \
    --vnet-name $VNET_NAME \
    -n $SUBNET_NAME \
    --address-prefix 10.1.0.0/23 \
    --delegations Microsoft.App/environments
  ```

- [ ] **Get Subnet ID** (needed for Step 3)
  ```bash
  SUBNET_ID=$(az network vnet subnet show \
    -g $RESOURCE_GROUP \
    --vnet-name $VNET_NAME \
    -n $SUBNET_NAME \
    --query id -o tsv)
  
  echo "Subnet ID: $SUBNET_ID"
  ```

**Validation**:
```bash
az network vnet show -g $RESOURCE_GROUP -n $VNET_NAME --query "[name,addressSpace.addressPrefixes[0]]" -o tsv
# Expected: containerapp-vnet, 10.1.0.0/16
```

---

### Step 2: Create VNet Peering (10 minutes)

- [ ] **Get AKS VNet ID**
  ```bash
  AKS_NODE_RG="<AKS_NODE_RESOURCE_GROUP>"  # from pre-implementation
  AKS_VNET_NAME="<AKS_VNET_NAME>"  # from pre-implementation
  
  AKS_VNET_ID=$(az network vnet show \
    -g $AKS_NODE_RG \
    -n $AKS_VNET_NAME \
    --query id -o tsv)
  
  echo "AKS VNet ID: $AKS_VNET_ID"
  ```

- [ ] **Get Container Apps VNet ID**
  ```bash
  CA_VNET_ID=$(az network vnet show \
    -g $RESOURCE_GROUP \
    -n $VNET_NAME \
    --query id -o tsv)
  
  echo "Container Apps VNet ID: $CA_VNET_ID"
  ```

- [ ] **Create Peering: Container Apps → AKS**
  ```bash
  az network vnet peering create \
    -g $RESOURCE_GROUP \
    --vnet-name $VNET_NAME \
    -n containerapp-to-aks \
    --remote-vnet $AKS_VNET_ID \
    --allow-vnet-access
  ```

- [ ] **Create Peering: AKS → Container Apps**
  ```bash
  az network vnet peering create \
    -g $AKS_NODE_RG \
    --vnet-name $AKS_VNET_NAME \
    -n aks-to-containerapp \
    --remote-vnet $CA_VNET_ID \
    --allow-vnet-access
  ```

**Validation**:
```bash
# Check peering status (both should be "Connected")
az network vnet peering show -g $RESOURCE_GROUP --vnet-name $VNET_NAME -n containerapp-to-aks --query peeringState -o tsv
az network vnet peering show -g $AKS_NODE_RG --vnet-name $AKS_VNET_NAME -n aks-to-containerapp --query peeringState -o tsv
# Expected: Connected, Connected
```

---

### Step 3: Create VNet-Integrated Container Apps Environment (10 minutes)

- [ ] **Create New Environment** (or update existing)
  ```bash
  CA_ENV_NAME="drasicrhsith-prod-vnet"
  
  az containerapp env create \
    -g $RESOURCE_GROUP \
    -n $CA_ENV_NAME \
    -l $LOCATION \
    --infrastructure-subnet-resource-id $SUBNET_ID \
    --internal-only false
  ```

**Note**: If you have an existing Container Apps environment, you **cannot** add VNet integration after creation. You must create a new environment and redeploy apps to it.

**Validation**:
```bash
az containerapp env show -n $CA_ENV_NAME -g $RESOURCE_GROUP --query "[name,vnetConfiguration.infrastructureSubnetId]" -o tsv
# Expected: drasicrhsith-prod-vnet, /subscriptions/.../subnets/containerapp-subnet
```

---

### Step 4: Test Network Connectivity (5 minutes)

- [ ] **Deploy Test Container** (optional but recommended)
  ```bash
  az containerapp create \
    -g $RESOURCE_GROUP \
    -n network-test \
    --environment $CA_ENV_NAME \
    --image mcr.microsoft.com/k8se/quickstart:latest \
    --target-port 80 \
    --ingress external
  ```

- [ ] **Test Connectivity via Console**
  ```bash
  # Open Container App console in Azure Portal
  # Or use: az containerapp exec ...
  
  # From container shell, test Drasi ClusterIP:
  curl http://10.0.123.45/wishlist-trending-1h  # Use your actual ClusterIP
  
  # Expected: JSON response with {"header":{...}} followed by {"data":{...}}
  ```

**Validation**: If curl returns JSON data, network connectivity is working! ✅

---

### Step 5: Redeploy API to VNet-Integrated Environment (5 minutes)

- [ ] **Update Azure Developer CLI Environment** (if using `azd`)
  ```bash
  # Update azure.yaml or .env to use new environment name
  AZURE_CONTAINER_APPS_ENVIRONMENT_NAME=drasicrhsith-prod-vnet
  
  azd env set AZURE_CONTAINER_APPS_ENVIRONMENT_NAME $CA_ENV_NAME
  ```

- [ ] **Redeploy API**
  ```bash
  azd deploy api
  # Or: az containerapp update ...
  ```

**Validation**:
```bash
az containerapp show -n drasicrhsith-prod-api -g $RESOURCE_GROUP --query "[name,properties.environmentId]" -o tsv
# Expected: drasicrhsith-prod-api, .../environments/drasicrhsith-prod-vnet
```

---

### Step 6: Configure Drasi View Service Base URL (3 minutes)

- [ ] **Set Environment Variable in Container App**
  ```bash
  DRASI_CLUSTER_IP="10.0.123.45"  # Use your actual ClusterIP from pre-implementation
  
  az containerapp update \
    -n drasicrhsith-prod-api \
    -g $RESOURCE_GROUP \
    --set-env-vars "DRASI_VIEW_SERVICE_BASE_URL=http://$DRASI_CLUSTER_IP"
  ```

**Alternative**: Update `appsettings.json` (for local development)
```json
{
  "Drasi": {
    "QueryContainer": "default",
    "ViewServiceBaseUrl": "http://10.0.123.45"
  }
}
```

**Validation**:
```bash
az containerapp show -n drasicrhsith-prod-api -g $RESOURCE_GROUP --query "properties.template.containers[0].env[?name=='DRASI_VIEW_SERVICE_BASE_URL'].value" -o tsv
# Expected: http://10.0.123.45
```

---

### Step 7: Restart API Container App (2 minutes)

- [ ] **Restart to Apply Configuration**
  ```bash
  az containerapp revision restart \
    -n drasicrhsith-prod-api \
    -g $RESOURCE_GROUP
  ```

- [ ] **Wait for Healthy Status**
  ```bash
  az containerapp show -n drasicrhsith-prod-api -g $RESOURCE_GROUP --query "properties.runningStatus" -o tsv
  # Expected: Running
  ```

**Validation**:
```bash
az containerapp logs show -n drasicrhsith-prod-api -g $RESOURCE_GROUP --tail 50
# Look for log lines: "Querying Drasi view service: http://10.0.123.45/..."
```

---

### Step 8: End-to-End Testing (5 minutes)

- [ ] **Test API Health Endpoint**
  ```bash
  curl https://<API_URL>/api/health
  # Expected: {"status":"Healthy"}
  ```

- [ ] **Test Drasi Insights Endpoint**
  ```bash
  curl https://<API_URL>/api/v1/drasi/insights
  # Expected: JSON with trending/duplicates/inactive arrays (NOT empty)
  ```

- [ ] **Test Specific Query Endpoint**
  ```bash
  curl https://<API_URL>/api/v1/drasi/queries/wishlist-trending-1h
  # Expected: Array of trending items with item/frequency
  ```

- [ ] **Test Frontend Dashboard**
  ```bash
  # Open: https://<FRONTEND_URL>/
  # Navigate to "Santa's Dashboard" or Elf view
  # Expected: Drasi Insights Panel shows:
  #   - 📊 DRASI INSIGHTS with live stats
  #   - 🔥 TRENDING ITEMS section (populated)
  #   - ⚠️ DUPLICATE REQUESTS section (populated)
  #   - 📊 INACTIVE CHILDREN section (populated)
  ```

- [ ] **Verify Real-Time Updates** (SSE)
  ```bash
  # In browser DevTools Network tab:
  # Filter for: /api/v1/drasi/insights/stream
  # Expected: EventStream connection open, messages arriving every ~10s
  ```

**Validation**: If all tests pass, Drasi integration is fully functional! 🎉

---

## 🔒 Security Configuration (Optional but Recommended)

### Network Security Groups (NSGs)

- [ ] **Create NSG for Container Apps Subnet** (if not auto-created)
  ```bash
  az network nsg create \
    -g $RESOURCE_GROUP \
    -n containerapp-nsg \
    -l $LOCATION
  ```

- [ ] **Allow Outbound to Drasi ClusterIP**
  ```bash
  az network nsg rule create \
    -g $RESOURCE_GROUP \
    --nsg-name containerapp-nsg \
    -n AllowDrasiViewService \
    --priority 100 \
    --direction Outbound \
    --access Allow \
    --protocol Tcp \
    --destination-address-prefixes 10.0.123.45 \
    --destination-port-ranges 80
  ```

- [ ] **Associate NSG with Subnet**
  ```bash
  az network vnet subnet update \
    -g $RESOURCE_GROUP \
    --vnet-name $VNET_NAME \
    -n $SUBNET_NAME \
    --network-security-group containerapp-nsg
  ```

---

## 🧪 Troubleshooting

### Issue: Peering shows "Initiated" instead of "Connected"
**Solution**: Wait 2-3 minutes. If still not connected, check:
```bash
az network vnet peering list -g $RESOURCE_GROUP --vnet-name $VNET_NAME -o table
az network vnet peering list -g $AKS_NODE_RG --vnet-name $AKS_VNET_NAME -o table
# Ensure both directions exist
```

### Issue: Container App cannot reach Drasi ClusterIP
**Solution**: Check routing and NSG rules:
```bash
# From Container App console:
curl -v http://10.0.123.45/wishlist-trending-1h
# If timeout: routing issue
# If connection refused: wrong port or ClusterIP
# If 404: wrong query path
```

### Issue: API still returns empty results
**Solution**: Check logs and environment variable:
```bash
az containerapp logs show -n drasicrhsith-prod-api -g $RESOURCE_GROUP --tail 100
# Look for: "Querying Drasi view service: http://..."
# If no log line: env var not set or container not restarted
```

### Issue: Frontend shows empty dashboard
**Solution**: Check browser DevTools Console for errors:
```javascript
// Expected API response structure:
{
  "trending": [{"item":"item1","frequency":5}],
  "duplicates": [{"childId":"c1","item":"item1","count":3}],
  "inactive": [{"childId":"c2","daysSinceLastEvent":7}]
}
```

---

## 📊 Cost Estimation

| Resource | Cost | Notes |
|----------|------|-------|
| VNet Peering | ~$0.01/GB | Data transfer between VNets |
| Container Apps Environment (VNet-integrated) | Same as non-VNet | No additional cost |
| Network Security Group | Free | No cost for NSG itself |
| **Total Additional Cost** | **~$1-5/month** | Based on API traffic volume |

**Optimization**: Use ClusterIP directly (current approach) instead of Internal Load Balancer to save ~$18/month.

---

## ✅ Post-Implementation Checklist

- [ ] **Documentation Updated**
  - [ ] Record AKS VNet CIDR in runbook
  - [ ] Record Container Apps VNet CIDR in runbook
  - [ ] Record Drasi ClusterIP in configuration management
  - [ ] Update deployment scripts with new environment name

- [ ] **Monitoring Configured**
  - [ ] Add alert for VNet peering status
  - [ ] Add alert for API connection errors to Drasi
  - [ ] Add dashboard widget showing Drasi query latency

- [ ] **Backup Plan**
  - [ ] Document rollback procedure (redeploy to old environment)
  - [ ] Keep old Container Apps environment for 7 days before deletion

- [ ] **Team Handoff**
  - [ ] Brief DevOps team on VNet architecture
  - [ ] Brief developers on new environment variable requirement
  - [ ] Update CI/CD pipeline with new environment name

---

## 🎯 Success Criteria

**✅ VNet Integration Complete When**:
1. Both peering connections show "Connected" status
2. Container App can curl Drasi ClusterIP successfully
3. API logs show "Querying Drasi view service" messages
4. Frontend dashboard displays real-time Drasi data
5. SSE connection streams updates every ~10 seconds
6. No error logs related to Drasi connectivity

---

## 📚 Reference Documentation

- **Detailed Implementation**: See `drasi/VNET-INTEGRATION.md`
- **Drasi Integration Architecture**: See `DRASI_INTEGRATION.md`
- **Project Summary**: See `IMPLEMENTATION_SUMMARY.md`
- **Azure Container Apps VNet Integration**: https://learn.microsoft.com/azure/container-apps/vnet-custom
- **Azure VNet Peering**: https://learn.microsoft.com/azure/virtual-network/virtual-network-peering-overview

---

## 🆘 Support Contacts

- **Azure Networking Issues**: Azure Support
- **Drasi Query Issues**: Check Drasi documentation and GitHub issues
- **API Code Issues**: See `src/services/DrasiViewClient.cs` and `DrasiInsightsApi.cs`
- **Frontend Issues**: See `frontend/src/components/DrasiInsightsPanel.tsx`

---

**Last Updated**: 2025-11-24  
**Version**: 1.0  
**Status**: ✅ Ready for Implementation