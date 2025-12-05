// Centralized API configuration
// Frontend is served from the same Container App as the API (same-origin deployment)
// All API calls use relative URLs (/api/*) which works for:
//   - Production: same-origin Container App
//   - Development: Vite dev server proxy (/api/* -> localhost:8080)
export const API_URL = "";
