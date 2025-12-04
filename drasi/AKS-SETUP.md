# Drasi on AKS: Setup Guide

This guide connects your deployed app (Container App + Cosmos + SWA) to a Drasi environment running on Azure Kubernetes Service (AKS).

## 1) Install Drasi on AKS
Follow the official Drasi docs:
- Install Drasi CLI
- Set kubectl context to your AKS cluster
- Initialize Drasi in a namespace (default `drasi-system`):

```bash
# Example
drasi init -n drasi-system
```

Reference: https://drasi.io/how-to-guides/installation/install-on-aks/

## 2) Configure app endpoints/secrets for Drasi
Your Drasi reactions need to call the API and write DLQ entries to Cosmos.
Run the helper script to create a Secret and ConfigMap in your Drasi namespace.

```pwsh
# From repo root
# Parameters
#  -ResourceGroup: Azure RG where azd provisioned resources
#  -Project: naming prefix project (default: drasi)
#  -Env: naming prefix environment (default: prod)
#  -Namespace: Drasi K8s namespace (default: drasi-system)

./scripts/drasi-config.ps1 -ResourceGroup <rg-name> -Project drasi -Env prod -Namespace drasi
```

This creates:
- Secret `drasi-app-secrets` with `COSMOS_CONNECTION_STRING`, `EVENTHUB_CONNECTION`, `AGENT_TRIGGER_SECRET`
- ConfigMap `drasi-app-config` with:
  - `API_BASE_URL = https://<container-app-fqdn>/api`
  - `COSMOS_DATABASE = elves_demo`
  
Security note:
- The helper resolves the Event Hubs listen connection string from Azure Key Vault secret `eventhub-listen` (created by IaC). If Key Vault is unavailable, it falls back to the Event Hubs `listen` authorization rule. The value is stored in `drasi-app-secrets` under `EVENTHUB_CONNECTION`.

## 3) Align the graph to cloud URLs
`drasi/graph.yaml` uses environment-driven values so you can run the same graph locally or in AKS:
- `API_BASE_URL` for HTTP sink
- `COSMOS_CONNECTION_STRING`, `COSMOS_DATABASE` for DLQ sink

Ensure your Drasi runtime (queries/reactions) loads these env vars from the K8s Secret/ConfigMap above.

## 4) Source choice
The demo graph uses a filesystem source (`data/wishlist-events/*.json`). For AKS you can either:
- Mount Azure Files to the Drasi workload and keep the filesystem source; or
- Switch to a cloud connector (e.g., Blob Storage, Event Grid, database CDC) per Drasi docs.

See: https://drasi.io/how-to-guides/configure-sources/

### Option A: Azure Event Hubs (recommended)
This repo includes IaC to provision an Event Hubs namespace and a hub `wishlist-events`, plus a helper that creates the required K8s Secret for Drasi. After running the helper in step 2, apply the Event Hub Source:

```pwsh
# From repo root; ensure your kubectl context is the AKS cluster
drasi apply -f drasi/sources/eventhub-source.yaml

# Verify
drasi list source
drasi describe source wishlist-events-eh
```

Notes:
- The script creates Secret `drasi-app-secrets` with key `EVENTHUB_CONNECTION` in your namespace.
- The Source subscribes to the hub `wishlist-events` in the `${project}-${env}-eh` namespace.
- If your project/env prefix is < 4 chars, the EH namespace is `${project}-${env}-ehns`.

## 5) End-to-end test
- Verify SWA proxies `/api/*` to the Container App.
- Verify Drasi can POST to `${API_BASE_URL}/orchestrator/ingest` (HTTP sinks and reactions), and check your API logs for 202 Accepted.
- Ensure DLQ fallback writes to Cosmos container `dlq`.

What the postdeploy script wires up:
- An Http Reaction for `wishlist-updates` that posts wishlist changes to the orchestrator (type `wishlist`).
- An Http Reaction for `wishlist-inactive-children-3d` that posts per-child inactivity notifications (type `notification`).
- An Http Reaction for `wishlist-duplicates-by-child` that posts per-child duplicate-item notifications (type `notification`).
- A Debug Reaction `wishlist-debug` to visualize continuous query results.

If you need a Drasi deployment example wired to consume these K8s vars, tell me your preferred source connector and I can add manifests/Helm values.
