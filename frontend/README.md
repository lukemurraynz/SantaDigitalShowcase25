# Frontend

This directory contains the React + Vite frontend for Santa's Digital Elves.

## Getting Started

```bash
# Install dependencies
npm install

# Start development server
npm run dev

# Build for production
npm run build

# Run linting
npm run lint
```

## Static Web App Configuration

The `staticwebapp.config.json` file configures Azure Static Web Apps routing and behavior. This file is required for proper SPA routing and API proxying.

### Configuration Overview

| Setting | Purpose |
|---------|---------|
| `navigationFallback` | Enables SPA routing by redirecting unmatched routes to `index.html` |
| `routes` | Defines API routes, authentication rules, and backend proxying |

### Modifying the Configuration

1. **Adding new API routes**: Add entries to the `routes` array with appropriate `route` patterns and `rewrite` targets
2. **Changing authentication**: Modify the `allowedRoles` array to control access (e.g., `anonymous`, `authenticated`)
3. **Backend proxy changes**: Update the `rewrite` URLs to point to your Container App endpoint

### Example Configuration Changes

```json
{
  "routes": [
    {
      "route": "/api/*",
      "allowedRoles": ["anonymous"]
    },
    {
      "route": "/api/{*path}",
      "rewrite": "https://your-api.azurecontainerapps.io/api/{path}",
      "forwardingGateway": true
    }
  ]
}
```

### References

- [Azure Static Web Apps configuration](https://learn.microsoft.com/azure/static-web-apps/configuration)
- [SWA routing documentation](https://learn.microsoft.com/azure/static-web-apps/routes)
