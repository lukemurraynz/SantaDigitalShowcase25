using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Middleware
{
    /// <summary>
    /// Lightweight role middleware - requires X-Role header for basic access control.
    /// Very light security - just ensures clients know to send the header.
    /// </summary>
    public sealed class RoleTrackingMiddleware
    {
        readonly RequestDelegate _next;
        public RoleTrackingMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);

            // Allow OPTIONS requests through for CORS preflight
            if (ctx.Request.Method == "OPTIONS")
            {
                await _next(ctx);
                return;
            }

            // Exempt specific endpoints from role requirement
            var path = ctx.Request.Path.Value ?? "";

            // Allow unauthenticated access for infrastructure & realtime endpoints.
            // These should be callable by platform health probes, dashboards, and SignalR clients
            // without forcing the lightweight X-Role header.
            bool IsExemptPath(string p)
            {
                if (string.IsNullOrEmpty(p))
                    return false;
                // Static files (frontend assets) - must be accessible without X-Role header
                if (p.StartsWith("/assets/", System.StringComparison.OrdinalIgnoreCase))
                    return true;
                if (p.EndsWith(".js", System.StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".css", System.StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".ico", System.StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".woff", System.StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".woff2", System.StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".ttf", System.StringComparison.OrdinalIgnoreCase))
                    return true;
                // External hub proxy (/hub/...) and local in-process hub (/api/hub)
                if (p.StartsWith("/hub", System.StringComparison.OrdinalIgnoreCase))
                    return true;
                if (p.StartsWith("/api/hub", System.StringComparison.OrdinalIgnoreCase))
                    return true;
                // Versioned API endpoints are public for this demo; allow without X-Role
                if (p.StartsWith("/api/v1/", System.StringComparison.OrdinalIgnoreCase))
                    return true;
                // Health / readiness / liveness endpoints (includes /plain-health for auth isolation testing)
                if (p.Equals("/healthz", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (p.Equals("/health", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (p.Equals("/readyz", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (p.Equals("/livez", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (p.Equals("/plain-health", StringComparison.OrdinalIgnoreCase))
                    return true;
                // API index & version metadata
                if (p.Equals("/", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (p.Equals("/api", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (p.StartsWith("/api/version", StringComparison.OrdinalIgnoreCase))
                    return true;
                // Lightweight diagnostics ping
                if (p.StartsWith("/api/pingz", StringComparison.OrdinalIgnoreCase))
                    return true;
                return false;
            }

            if (IsExemptPath(path))
            {
                await _next(ctx);
                return;
            }

            // Extract X-Role header (e.g., "operator", "auditor", "viewer")
            string? role = ctx.Request.Headers["X-Role"].FirstOrDefault();

            // Allow requests from Static Web App linked backend (Azure adds X-MS-CLIENT-PRINCIPAL-ID header)
            // The linked backend doesn't forward custom headers like X-Role, so we trust the Azure platform
            var staticWebAppPrincipal = ctx.Request.Headers["X-MS-CLIENT-PRINCIPAL-ID"].FirstOrDefault();
            var staticWebAppHeader = ctx.Request.Headers["X-MS-ORIGINAL-URL"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(staticWebAppPrincipal) || !string.IsNullOrWhiteSpace(staticWebAppHeader))
            {
                // Request is coming through Static Web App linked backend proxy
                // Default to "operator" role for Static Web App requests
                role = "operator";
            }
            else if (string.IsNullOrWhiteSpace(role))
            {
                // Direct access requires X-Role header (light security gate) unless exempt.
                // Add diagnostic detail header for troubleshooting 401s like SignalR negotiate.
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.Headers["X-Diag-RoleMissing"] = "1";
                await ctx.Response.WriteAsync("X-Role header required");
                return;
            }

            // Attach as claim for downstream logging/metrics
            var identity = new ClaimsIdentity(new[] { new Claim("role", role) }, "demo");
            ctx.User = new ClaimsPrincipal(identity);
            ctx.Items["X-Role"] = role;

            await _next(ctx);
        }
    }
}
