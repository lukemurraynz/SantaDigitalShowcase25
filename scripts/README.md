# Scripts Directory

This directory contains operational scripts for Santa's Workshop demonstration and testing.

> 📖 **ISE Playbook Compliance**: This directory follows the [ISE Engineering Playbook](https://microsoft.github.io/code-with-engineering-playbook/) guidelines for maintaining minimal legacy artifacts and clear documentation.

## 📋 Script Catalog

### ✅ Supported Scripts

| Script | Purpose | Usage |
|--------|---------|-------|
| `demo-interactive.ps1` | **Primary demo script** - Menu-driven interface for all demos | `.\demo-interactive.ps1 -ApiUrl "https://..."` |
| `test-demo-readiness.ps1` | Pre-demo validation - Tests all components | `.\test-demo-readiness.ps1 -ApiUrl "https://..."` |
| `test-smoke.ps1` | CI/CD smoke tests for pipelines | `.\test-smoke.ps1 -ApiUrl "..." -FrontendUrl "..."` |
| `send-wishlist-event.ps1` | Direct EventHub event sender | `.\send-wishlist-event.ps1 -ChildId "..." -Items "..."` |
| `drasi-config.ps1` | Drasi configuration helper | Used by deployment scripts |
| `ci-placeholder.ps1` | CI/CD placeholder for future enhancements | N/A |

### ⚠️ Archived Scripts (Deprecated)

Scripts in `archive/` are **deprecated** and retained for historical reference only:

| Archived Script | Replacement |
|----------------|-------------|
| `simulate.ps1` | `demo-interactive.ps1` (Scenarios 1, 6) |
| `simulate-naughty-nice.ps1` | `demo-interactive.ps1` (Scenario 4) |
| `test-us1.ps1`, `test-us2.ps1`, `test-us3.ps1` | `test-demo-readiness.ps1` |
| `test-us1-drasi-sim.ps1` | `demo-interactive.ps1` (Scenario 7) |
| `test-ui.ps1` | `demo-interactive.ps1` (Health Check) |

See `archive/README.md` for migration guidance if you encounter documentation referencing these scripts.

## Primary Demo Script

### 🎅 demo-interactive.ps1
**The main demonstration script** - Use this for all presentations and technical demos.

```powershell
# Auto-discover API from azd environment
.\demo-interactive.ps1

# Explicitly specify API endpoint
.\demo-interactive.ps1 -ApiUrl "https://your-api.azurecontainerapps.io"
```

**Features:**
- Menu-driven interface with 7 scenarios
- End-to-end wishlist processing
- Multi-agent collaboration demos
- Streaming SSE recommendations  
- Naughty/Nice behavior detection
- Agent tools showcase
- Full system validation
- Integrated health checks
- Built-in documentation

## Testing & Validation Scripts

### test-demo-readiness.ps1
Comprehensive pre-demo validation script. Tests all components:
- Infrastructure connectivity
- API health & readiness
- Cosmos DB connection
- Drasi integration
- Agent Framework status

```powershell
.\test-demo-readiness.ps1 -ApiUrl "https://your-api.azurecontainerapps.io"
```

### test-smoke.ps1
CI/CD smoke test for automated pipelines.

```powershell
.\test-smoke.ps1 -ApiUrl "https://api-url" -FrontendUrl "https://frontend-url"
```

## Utility Scripts

### send-wishlist-event.ps1
Sends wishlist events directly to EventHub (bypasses API).

```powershell
.\send-wishlist-event.ps1 -ChildId "child-123" -Items "Train:1,Drone:1" -Hub "wishlist-events"
```

### drasi-config.ps1
Drasi configuration and resource management helper.

### ci-placeholder.ps1
Placeholder for future CI/CD enhancements.

## Quick Reference

| Use Case | Script |
|----------|--------|
| 🎯 **Demo presentation** | `demo-interactive.ps1` |
| ✅ **Pre-demo validation** | `test-demo-readiness.ps1` or demo-interactive [7] |
| 🏥 **Quick health check** | demo-interactive [H] |
| 🔧 **Direct EventHub test** | `send-wishlist-event.ps1` |
| 🚀 **CI/CD smoke test** | `test-smoke.ps1` |

## ⚠️ Documentation Migration Notice

If you encounter documentation (guides, specs, or tutorials) that references archived scripts like:
- `scripts/simulate.ps1`
- `scripts/simulate-naughty-nice.ps1`  
- `scripts/test-us1.ps1`, `scripts/test-us2.ps1`, `scripts/test-us3.ps1`

**Use `demo-interactive.ps1` instead.** The archived scripts are retained only for historical reference.

### Migration Examples

| Old Command | New Command |
|-------------|-------------|
| `.\scripts\simulate.ps1 -ChildId "alice" -UseWishlist` | `.\scripts\demo-interactive.ps1` → Select Scenario 1 or 6 |
| `.\scripts\simulate-naughty-nice.ps1 -Behavior nice` | `.\scripts\demo-interactive.ps1` → Select Scenario 4 |
| `.\scripts\test-us1.ps1` | `.\scripts\test-demo-readiness.ps1` |

## Documentation

For detailed usage and architecture:
- Main demo guide: `../DEMO-GUIDE.md`
- Quick reference: `../DEMO-QUICK-REF.md`
- Architecture docs: `../docs/architecture/`
