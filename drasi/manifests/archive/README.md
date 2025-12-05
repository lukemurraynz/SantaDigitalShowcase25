# Archived Drasi Manifests

This directory contains Drasi manifest files that are **NOT used by the automated deployment scripts**.

## Archive Date
December 6, 2025

## Reason for Archival
Comprehensive analysis revealed these files are never referenced by deployment scripts.

The automated deployment only uses 3 files from drasi/manifests/:
1. **kubernetes-resources.yaml** - Core infrastructure
2. **drasi-resources.yaml** - Drasi CRD resources
3. **02-drasi-infra.yaml** - Infrastructure (manifest fallback only)

## Archived Files

### Infrastructure Files
- **00-namespace.yaml** - Namespace creation
- **00-dapr.yaml** - Dapr configuration
- **serviceaccount.yaml** - ServiceAccount (duplicate)

### Component/Secret Files
- **21-cosmos-state-secret.yaml** - Cosmos secret (created dynamically)

### Service Files
- **05-source-proxy-service.yaml** - Source proxy service
- **drasi-view-service-deployment.yaml** - View service deployment
- **drasi-view-service-lb.yaml** - View service load balancer
- **signalr-reaction-override.yaml** - SignalR reaction configuration

## Recovery Instructions

To restore: Copy from archive back to drasi/manifests/ and apply with kubectl.

See MANIFEST_ANALYSIS.md in project root for complete analysis.
