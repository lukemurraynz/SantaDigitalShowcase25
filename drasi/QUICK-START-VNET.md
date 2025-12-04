# Quick Start: VNet Integration (Copy-Paste Ready)

**Purpose**: Connect Azure Container Apps to AKS Drasi in 30 minutes.

---

## 🎯 What You Need

1. **Azure CLI** authenticated with contributor access
2. **kubectl** configured for AKS cluster
3. **30 minutes** of focused time

---

## ⚡ Step-by-Step Commands

### 1️⃣ Get Drasi ClusterIP (2 min)

```bash
kubectl get svc -n drasi-system default-view-svc -o jsonpath='{.spec.clusterIP}'
```

**📝 Record this IP**: `_________________` (e.g., 10.0.123.45)

---

### 2️⃣ Get AKS VNet Info (2 min)

```bash
# Replace with your values
AKS_CLUSTER_NAME="<YOUR_AKS_CLUSTER>"
AKS_RESOURCE_GROUP="<YOUR_AKS_RG>"

# Get node resource group
AKS_NODE_RG=$(az aks show -n $AKS_CLUSTER_NAME -g $AKS_RESOURCE_GROUP --query nodeResourceGroup -o tsv)
echo "Node RG: $AKS_NODE_RG"

# Get VNet name and CIDR
AKS_VNET=$(az network vnet list -g $AKS_NODE_RG --query "[0].name" -o tsv)
AKS_CIDR=$(az network vnet show -g $AKS_NODE_RG -n $AKS_VNET --query "addressSpace.addressPrefixes[0]" -o tsv)
echo "AKS VNet: $AKS_VNET ($AKS_CIDR)"
```

**📝 Record**: 
- Node RG: `_________________`
- VNet Name: `_________________`
- CIDR: `_________________` (e.g., 10.0.0.0/16)

---

### 3️⃣ Create Container Apps VNet (5 min)

```bash
# Set your values
RESOURCE_GROUP="<YOUR_RESOURCE_GROUP>"
LOCATION="<YOUR_LOCATION>"  # e.g., eastus
VNET_NAME="containerapp-vnet"
SUBNET_NAME="containerapp-subnet"

# Create VNet (choose non-overlapping CIDR!)
az network vnet create \
  -g $RESOURCE_GROUP \
  -n $VNET_NAME \
  -l $LOCATION \
  --address-prefix 10.1.0.0/16

# Create subnet
az network vnet subnet create \
  -g $RESOURCE_GROUP \
  --vnet-name $VNET_NAME \
  -n $SUBNET_NAME \
  --address-prefix 10.1.0.0/23 \
  --delegations Microsoft.App/environments

# Get subnet ID
SUBNET_ID=$(az network vnet subnet show \
  -g $RESOURCE_GROUP \
  --vnet-name $VNET_NAME \
  -n $SUBNET_NAME \
  --query id -o tsv)
echo "Subnet ID: $SUBNET_ID"
```

**✅ Verify**: VNet created with 10.1.0.0/16 CIDR

---

### 4️⃣ Create VNet Peering (5 min)

```bash
# Get VNet IDs
AKS_VNET_ID=$(az network vnet show -g $AKS_NODE_RG -n $AKS_VNET --query id -o tsv)
CA_VNET_ID=$(az network vnet show -g $RESOURCE_GROUP -n $VNET_NAME --query id -o tsv)

# Create peering: Container Apps → AKS
az network vnet peering create \
  -g $RESOURCE_GROUP \
  --vnet-name $VNET_NAME \
  -n containerapp-to-aks \
  --remote-vnet $AKS_VNET_ID \
  --allow-vnet-access

# Create peering: AKS → Container Apps
az network vnet peering create \
  -g $AKS_NODE_RG \
  --vnet-name $AKS_VNET \
  -n aks-to-containerapp \
  --remote-vnet $CA_VNET_ID \
  --allow-vnet-access
```

**✅ Verify**: Both peerings show "Connected"
```bash
az network vnet peering show -g $RESOURCE_GROUP --vnet-name $VNET_NAME -n containerapp-to-aks --query peeringState -o tsv
# Expected: Connected
```

---

### 5️⃣ Create VNet-Integrated Environment (5 min)

```bash
CA_ENV_NAME="drasicrhsith-prod-vnet"

az containerapp env create \
  -g $RESOURCE_GROUP \
  -n $CA_ENV_NAME \
  -l $LOCATION \
  --infrastructure-subnet-resource-id $SUBNET_ID \
  --internal-only false
```

**✅ Verify**: Environment created
```bash
az containerapp env show -n $CA_ENV_NAME -g $RESOURCE_GROUP --query name -o tsv
# Expected: drasicrhsith-prod-vnet
```

---

### 6️⃣ Test Connectivity (3 min)

```bash
# Deploy test container
az containerapp create \
  -g $RESOURCE_GROUP \
  -n network-test \
  --environment $CA_ENV_NAME \
  --image mcr.microsoft.com/k8se/quickstart:latest \
  --target-port 80 \
  --ingress external

# Get console URL
TEST_URL=$(az containerapp show -n network-test -g $RESOURCE_GROUP --query properties.configuration.ingress.fqdn -o tsv)
echo "Test App URL: https://$TEST_URL"
```

**🧪 Test from Container Console**:
```bash
# Open Azure Portal → Container Apps → network-test → Console
# Run:
curl http://<DRASI_CLUSTER_IP>/wishlist-trending-1h
# Expected: JSON response with Drasi data
```

---

### 7️⃣ Redeploy API with VNet (5 min)

```bash
API_NAME="drasicrhsith-prod-api"

# Update to new environment (this will redeploy)
az containerapp update \
  -n $API_NAME \
  -g $RESOURCE_GROUP \
  --set-env-vars "DRASI_VIEW_SERVICE_BASE_URL=http://<DRASI_CLUSTER_IP>"

# Or if redeploying completely:
azd env set AZURE_CONTAINER_APPS_ENVIRONMENT_NAME $CA_ENV_NAME
azd deploy api
```

---

### 8️⃣ Configure Drasi Base URL (2 min)

```bash
DRASI_CLUSTER_IP="<PASTE_YOUR_CLUSTERIP_FROM_STEP_1>"

az containerapp update \
  -n $API_NAME \
  -g $RESOURCE_GROUP \
  --set-env-vars "DRASI_VIEW_SERVICE_BASE_URL=http://$DRASI_CLUSTER_IP"
```

**✅ Verify**: Environment variable set
```bash
az containerapp show -n $API_NAME -g $RESOURCE_GROUP \
  --query "properties.template.containers[0].env[?name=='DRASI_VIEW_SERVICE_BASE_URL'].value" -o tsv
# Expected: http://10.0.123.45
```

---

### 9️⃣ Verify Integration (3 min)

```bash
# Get API URL
API_URL=$(az containerapp show -n $API_NAME -g $RESOURCE_GROUP --query properties.configuration.ingress.fqdn -o tsv)
echo "API URL: https://$API_URL"

# Test health
curl https://$API_URL/api/health
# Expected: {"status":"Healthy"}

# Test Drasi integration
curl https://$API_URL/api/v1/drasi/insights
# Expected: JSON with trending/duplicates/inactive arrays (NOT empty!)
```

---

## ✅ Success Checklist

- [ ] Drasi ClusterIP obtained: `________________`
- [ ] VNet peering status: `Connected` (both directions)
- [ ] Container Apps environment created with VNet
- [ ] Test container can curl Drasi ClusterIP
- [ ] API redeployed to new environment
- [ ] Environment variable `DRASI_VIEW_SERVICE_BASE_URL` set
- [ ] API `/api/v1/drasi/insights` returns real data
- [ ] Frontend dashboard shows live Drasi insights
- [ ] SSE connection streaming updates

---

## 🚨 Common Issues

**Issue**: Peering shows "Initiated" not "Connected"  
**Fix**: Wait 2-3 minutes, then check again

**Issue**: curl timeout from container  
**Fix**: Double-check ClusterIP is correct: `kubectl get svc -n drasi-system default-view-svc`

**Issue**: API still returns empty results  
**Fix**: Check logs for "Querying Drasi" message:
```bash
az containerapp logs show -n $API_NAME -g $RESOURCE_GROUP --tail 50
```

**Issue**: Environment variable not set  
**Fix**: Restart container:
```bash
az containerapp revision restart -n $API_NAME -g $RESOURCE_GROUP
```

---

## 📊 Cost Impact

- **VNet Peering**: ~$0.01/GB data transfer
- **VNet-integrated Container Apps Environment**: Same cost as standard
- **Estimated Total**: ~$1-5/month additional

---

## 📚 Full Documentation

For detailed explanations, troubleshooting, and security configuration:
- **Checklist**: `drasi/VNET-CONFIGURATION-CHECKLIST.md`
- **Technical Guide**: `drasi/VNET-INTEGRATION.md`
- **Architecture**: `DRASI_INTEGRATION.md`

---

## 🆘 Need Help?

1. Check logs: `az containerapp logs show -n $API_NAME -g $RESOURCE_GROUP --tail 100`
2. Review checklist: `drasi/VNET-CONFIGURATION-CHECKLIST.md`
3. Test connectivity: From container console, curl Drasi ClusterIP
4. Verify environment variable: `az containerapp show ... --query "properties.template.containers[0].env"`

---

**Ready to Start?** Copy commands from Step 1 and work through Step 9. Should take ~30 minutes total.

**Last Updated**: 2025-11-24  
**Tested On**: Azure Container Apps + AKS 1.28+