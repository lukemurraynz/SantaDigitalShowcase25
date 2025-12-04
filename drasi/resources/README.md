# Drasi Resources

This directory contains the Drasi resource definitions that are applied during deployment.

## Active Resources

| File | Description |
|------|-------------|
| `drasi-resources.yaml` | Main comprehensive file containing Source, ContinuousQuery, and Reaction definitions |
| `cosmos-state-component.yaml` | Dapr component for Cosmos DB state store (applied via kubectl) |
| `providers.yaml` | Provider overrides with pinned image versions |
| `providers.yaml.template` | Template for providers.yaml (generated during deployment) |

## Disabled Resources

The `disabled/` subdirectory contains older or duplicate resource files that are not applied during deployment.
These files are kept for reference but should not be used as they may conflict with the main `drasi-resources.yaml`.

## Resource Application

Resources are applied by the `apply-drasi-resources.ps1` script during the `azd deploy drasi` step:

1. First, `manifests/drasi-resources.yaml` is applied (contains providers and QueryContainer)
2. Then, all `.yaml` files in this directory are processed:
   - Dapr components (detected by `apiVersion: dapr.io/`) are applied via `kubectl`
   - Drasi resources (Source, ContinuousQuery, Reaction) are applied via `drasi apply`
   - Template files (`.template`) and substituted files (`-substituted`) are skipped

## Template Variables

The `drasi-resources.yaml` file uses template variables that are substituted during deployment:

- `${DRASI_MI_CLIENT_ID}` - Managed Identity client ID for Event Hub authentication
- `${EVENTHUB_FQDN}` - Event Hub namespace FQDN (e.g., `namespace.servicebus.windows.net`)

These values are retrieved from the azd environment by the `Get-DrasiTokenMap` function.
