// Centralized API configuration
// Frontend is now served from the same Container App as the API (same-origin in production)
// CORS is still configured for local development where frontend runs on a different port

export const API_URL = (() => {
  // Frontend and API are same-origin on Container App - always use relative URLs
  // Check for Container App hostname for logging purposes
  if (
    typeof window !== "undefined" &&
    window.location.hostname.includes("azurecontainerapps")
  ) {
    console.log("[config] Running on Azure Container App - using relative URLs");
    return ""; // Empty = relative URLs (/api/* routes)
  }

  // Local development: Vite dev server proxies /api/* to localhost:8080
  if (typeof window !== "undefined" && window.location.hostname === "localhost") {
    console.log("[config] Local development - using relative URLs with Vite proxy");
    return ""; // Vite proxy handles /api/* -> localhost:8080
  }

  // Fallback for other environments
  console.warn(
    "[config] WARNING: Unrecognized environment for API_URL resolution.",
    "window.location.hostname:",
    typeof window !== "undefined" ? window.location.hostname : "N/A",
    "- Defaulting to relative URLs. This may not work as expected in some deployment scenarios."
  );
  return "";
})();

console.log("[config] API_URL resolved to:", API_URL || "(empty/relative)");
console.log(
  "[config] window.location.hostname:",
  typeof window !== "undefined" ? window.location.hostname : "N/A"
);
