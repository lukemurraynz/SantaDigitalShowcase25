using Microsoft.Extensions.Diagnostics.HealthChecks;
using Drasicrhsit.Infrastructure;

namespace Services;

/// <summary>
/// Health check for Drasi view service connectivity.
/// Returns Degraded (not Unhealthy) when Drasi is unreachable to avoid blocking API readiness
/// when Drasi hasn't been deployed yet or is temporarily unavailable.
/// </summary>
public class DrasiHealthCheck : IHealthCheck
{
    private readonly IDrasiViewClient _drasiClient;
    private readonly IConfiguration _config;
    private readonly ILogger<DrasiHealthCheck> _logger;

    public DrasiHealthCheck(
        IDrasiViewClient drasiClient,
        IConfiguration config,
        ILogger<DrasiHealthCheck> logger)
    {
        _drasiClient = drasiClient;
        _config = config;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if Drasi view service URL is configured
            var viewServiceUrl = ConfigurationHelper.GetOptionalValue(
                _config,
                "Drasi:ViewServiceBaseUrl",
                "DRASI_VIEW_SERVICE_BASE_URL");

            var queryContainer = ConfigurationHelper.GetValue(
                _config,
                "Drasi:QueryContainer",
                "DRASI_QUERY_CONTAINER",
                "default");

            // If view service URL is not configured, return degraded (not blocking)
            // This allows the API to be ready even before Drasi is deployed
            if (string.IsNullOrEmpty(viewServiceUrl))
            {
                _logger.LogWarning("DRASI_VIEW_SERVICE_BASE_URL not configured - Drasi health check skipped. " +
                    "Set this to the Drasi view service LoadBalancer public IP after 'azd deploy drasi'.");
                return HealthCheckResult.Degraded(
                    "Drasi view service URL not configured (DRASI_VIEW_SERVICE_BASE_URL)",
                    data: new Dictionary<string, object>
                    {
                        { "configured", false },
                        { "hint", "Run 'azd deploy drasi' then 'azd deploy api' to configure Drasi URLs" },
                        { "timestamp", DateTime.UtcNow }
                    });
            }

            // Test with a known query (use smallest/fastest one)
            var testQuery = "wishlist-trending-1h";

            var results = await _drasiClient.GetCurrentResultAsync(
                queryContainer,
                testQuery,
                cancellationToken);

            var data = new Dictionary<string, object>
            {
                { "queryContainer", queryContainer },
                { "testQuery", testQuery },
                { "resultCount", results.Count },
                { "viewServiceUrl", viewServiceUrl },
                { "timestamp", DateTime.UtcNow }
            };

            return HealthCheckResult.Healthy(
                "Drasi view service is accessible",
                data);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Drasi view service health check failed - connectivity issue. " +
                "Ensure DRASI_VIEW_SERVICE_BASE_URL is set to the correct LoadBalancer IP.");
            return HealthCheckResult.Degraded(
                "Drasi view service unreachable - check DRASI_VIEW_SERVICE_BASE_URL configuration",
                ex,
                new Dictionary<string, object>
                {
                    { "error", ex.Message },
                    { "hint", "Verify the Drasi view service LoadBalancer has an external IP and is accessible" }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Drasi view service health check failed");
            return HealthCheckResult.Degraded(
                "Drasi view service check failed",
                ex,
                new Dictionary<string, object> { { "error", ex.Message } });
        }
    }
}
