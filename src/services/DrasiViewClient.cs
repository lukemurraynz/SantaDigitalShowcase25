using System.Text.Json;
using System.Text.Json.Nodes;
using Drasicrhsit.Infrastructure;

namespace Services;

public interface IDrasiViewClient
{
    Task<List<JsonNode>> GetCurrentResultAsync(string queryContainerId, string queryId, CancellationToken ct = default);
}

public class DrasiViewClient : IDrasiViewClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DrasiViewClient> _logger;
    private readonly IConfiguration _config;
    private readonly JsonSerializerOptions _jsonOptions;

    public DrasiViewClient(HttpClient httpClient, ILogger<DrasiViewClient> logger, IConfiguration config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<List<JsonNode>> GetCurrentResultAsync(string queryContainerId, string queryId, CancellationToken ct = default)
    {
        var results = new List<JsonNode>();

        try
        {
            // Use base URL from config if provided (for public Drasi endpoint from Container Apps)
            // The DRASI_VIEW_SERVICE_BASE_URL should be set to the LoadBalancer external IP after Drasi deployment
            // Fallback to Kubernetes DNS format is only valid when running inside the AKS cluster
            var baseUrl = ConfigurationHelper.GetOptionalValue(
                _config,
                "Drasi:ViewServiceBaseUrl",
                "DRASI_VIEW_SERVICE_BASE_URL");

            string url;
            if (!string.IsNullOrEmpty(baseUrl))
            {
                url = $"{baseUrl.TrimEnd('/')}/{queryId}";
            }
            else
            {
                // IMPORTANT: K8s DNS format only works inside the cluster. For Container Apps,
                // DRASI_VIEW_SERVICE_BASE_URL must be set to the public LoadBalancer IP.
                url = $"http://{queryContainerId}-view-svc/{queryId}";
                _logger.LogWarning(
                    "DRASI_VIEW_SERVICE_BASE_URL not configured. Using K8s DNS fallback '{Url}' which only works inside AKS cluster. " +
                    "For Container Apps, set DRASI_VIEW_SERVICE_BASE_URL to the Drasi view service LoadBalancer public IP (e.g., http://x.x.x.x).",
                    url);
            }

            _logger.LogInformation("Querying Drasi view service: {Url}", url);

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Drasi view service returned {StatusCode} for query {QueryId}",
                    response.StatusCode, queryId);
                return results;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);

            // Parse JSON streaming array format
            // First item is header: { "header": { "sequence": 0, "timestamp": 0, "state": "running" } }
            // Following items are data: { "data": { ...query results... } }

            await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonNode>(stream, _jsonOptions, ct))
            {
                if (item == null)
                    continue;

                // Skip header items, only collect data items
                if (item["data"] != null)
                {
                    results.Add(item["data"]!.AsObject());
                }
            }

            _logger.LogInformation("Retrieved {Count} results from Drasi query {QueryId}", results.Count, queryId);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error querying Drasi view service for {QueryId}. Ensure DRASI_VIEW_SERVICE_BASE_URL is set to the public Drasi view service URL.", queryId);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error for Drasi query {QueryId}", queryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error querying Drasi for {QueryId}", queryId);
        }

        return results;
    }
}
