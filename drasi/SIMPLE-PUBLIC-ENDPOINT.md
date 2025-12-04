# Simple Public Endpoint Setup (No VNet)

**Time**: 5 minutes | **Complexity**: Low

---

## Overview

Instead of VNet peering, expose Drasi view service via a public LoadBalancer endpoint. This is simpler but less secure (suitable for demos and development).

---

## ⚡ Quick Setup

### Step 1: Deploy LoadBalancer Service (2 minutes)

```bash
kubectl apply -f drasi/manifests/drasi-view-service-lb.yaml
```

### Step 2: Get Public IP (1 minute)

Wait for Azure to assign a public IP (30-60 seconds):

```bash
kubectl get svc -n drasi-system default-view-svc-public -w
# Wait until EXTERNAL-IP shows an IP address (not <pending>)
```

Get the IP:

```bash
DRASI_PUBLIC_IP=$(kubectl get svc -n drasi-system default-view-svc-public -o jsonpath='{.status.loadBalancer.ingress[0].ip}')
echo "Drasi Public IP: $DRASI_PUBLIC_IP"
```

### Step 3: Configure API (2 minutes)

Update your Container App with the public endpoint:

```bash
az containerapp update \
  -n drasicrhsith-prod-api \
  -g <YOUR_RESOURCE_GROUP> \
  --set-env-vars "DRASI_VIEW_SERVICE_BASE_URL=http://$DRASI_PUBLIC_IP"
```

Or update `src/appsettings.json` for local dev:

```json
{
  "Drasi": {
    "QueryContainer": "default",
    "ViewServiceBaseUrl": "http://<PUBLIC_IP>"
  }
}
```

### Step 4: Test (1 minute)

```bash
# Test Drasi endpoint directly
curl http://$DRASI_PUBLIC_IP/wishlist-trending-1h

# Test API endpoint
curl https://<API_URL>/api/v1/drasi/insights
# Expected: Real data, not empty arrays!
```

---

## ✅ Verification

**Success indicators:**
- `kubectl get svc -n drasi-system default-view-svc-public` shows EXTERNAL-IP
- `curl http://<IP>/wishlist-trending-1h` returns JSON
- API logs show: `Querying Drasi view service: http://<IP>/...`
- Frontend dashboard displays live data

---

## 💰 Cost

- **Azure LoadBalancer**: ~$18/month for standard SKU
- **Public IP**: ~$3/month
- **Total**: ~$21/month

Compare to VNet peering: ~$1-5/month (but requires 30 min setup)

---

## 🔒 Security Considerations

**⚠️ This exposes Drasi to the internet!**

**For demos/dev**: This is fine - quick and simple.

**For production**: Consider:
1. Add Network Security Group (NSG) rules to restrict source IPs
2. Use Internal LoadBalancer + VNet peering (see `VNET-INTEGRATION.md`)
3. Add API Gateway with authentication
4. Use Azure Private Link

**Quick NSG restriction** (allow only your Container App):

```bash
# Get Container App outbound IPs
OUTBOUND_IPS=$(az containerapp show -n drasicrhsith-prod-api -g <RG> --query "properties.outboundIpAddresses" -o tsv)

# Add NSG rule (if NSG exists on AKS subnet)
az network nsg rule create \
  -g <AKS_NODE_RG> \
  --nsg-name <AKS_NSG_NAME> \
  -n AllowContainerAppToDrasi \
  --priority 100 \
  --source-address-prefixes $OUTBOUND_IPS \
  --destination-port-ranges 80 \
  --access Allow \
  --protocol Tcp
```

---

## 🔄 Migration to VNet Later

If you want to switch to VNet peering later:

```bash
# Delete LoadBalancer service
kubectl delete svc -n drasi-system default-view-svc-public

# Follow VNET-INTEGRATION.md
# Update DRASI_VIEW_SERVICE_BASE_URL to ClusterIP instead
```

---

## 🆘 Troubleshooting

**Issue**: EXTERNAL-IP stuck at `<pending>`  
**Solution**: Check AKS networking:
```bash
kubectl describe svc -n drasi-system default-view-svc-public
# Look for events/errors
```

**Issue**: curl returns "Connection refused"  
**Solution**: Check pod selector:
```bash
kubectl get pods -n drasi-system -l drasi/type=QueryContainer
# Should show default-query-container pod
```

**Issue**: API still returns empty results  
**Solution**: Restart Container App:
```bash
az containerapp revision restart -n drasicrhsith-prod-api -g <RG>
```

---

## 📝 Summary

**What you did:**
1. Created LoadBalancer service in AKS
2. Got public IP for Drasi
3. Configured API with public endpoint
4. Tested end-to-end

**What works now:**
- Container Apps → Public Internet → Azure LoadBalancer → Drasi in AKS
- No VNet setup required
- Full Drasi integration functional

**Trade-offs:**
- ✅ Simple (5 minutes vs 30 minutes)
- ✅ Works immediately
- ❌ Less secure (public endpoint)
- ❌ Higher cost (~$21/month vs ~$1-5/month)

---

**For production**: Migrate to VNet peering using `VNET-INTEGRATION.md` when ready.