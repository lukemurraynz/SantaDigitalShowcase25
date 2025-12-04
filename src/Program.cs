using Azure.Identity;
using Drasicrhsit.Infrastructure;
using Drasicrhsit.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Models;
using Services;
using Middleware;
using Microsoft.Azure.Cosmos; // for CosmosClient
using Polly;
using Polly.Timeout;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Directory.GetCurrentDirectory()
});

// Explicitly configure console logging for Container Apps
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Configuration: appsettings + environment variables + optional .env at repo root
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Attempt to load .env from repo root (one level up from src) - ONLY in Development
string? loadedEnvFilePath = null;
if (builder.Environment.IsDevelopment())
{
    var repoRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, ".."));
    var envPath = Path.Combine(repoRoot, ".env");
    if (File.Exists(envPath))
    {
        loadedEnvFilePath = envPath;
        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var idx = trimmed.IndexOf('=');
            if (idx > 0)
            {
                var key = trimmed[..idx].Trim();
                var value = trimmed[(idx + 1)..].Trim().Trim('"');
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}

// Basic services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddRouting();
// Enable SignalR for in-process hub (frontend expects /api/hub)
// Configure to prefer long polling/SSE since WebSockets may be blocked through SWA routing
builder.Services.AddSignalR(options =>
{
    // Reduce keep-alive to detect disconnects faster
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.EnableDetailedErrors = true; // Enable for troubleshooting handshake errors

    // Increase handshake timeout for proxied connections (SWA adds latency)
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);

    // Allow longer message buffer for SSE/LongPolling through SWA proxy
    options.MaximumReceiveMessageSize = 128 * 1024; // 128KB
});

// Configure JSON options for request/response binding
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

builder.Services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();
builder.Services.AddSingleton<CosmosSetup>();
builder.Services.AddOptions<ElfRecommendationAgentOptions>()
    .Bind(builder.Configuration.GetSection("ElfAgents:Recommendation"));

// Drasi integration - HttpClient for view service queries with Polly resilience
builder.Services.AddHttpClient<IDrasiViewClient, DrasiViewClient>(client => client.Timeout = TimeSpan.FromSeconds(30))
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;

        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);

        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
    });

// Register Drasi health check
builder.Services.AddHealthChecks()
    .AddCheck<DrasiHealthCheck>(
        "drasi",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "ready", "drasi" });

// Named HttpClient for SignalR proxy (fixes socket exhaustion anti-pattern)
builder.Services.AddHttpClient("drasi-signalr-proxy")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

// Cosmos client & repository registrations (new domain repositories)
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    // Prefer environment variable COSMOS_ENDPOINT (non-secret) then config section
    var endpoint = Drasicrhsit.Infrastructure.ConfigurationHelper.GetOptionalValue(
        cfg,
        "Cosmos:Endpoint",
        "COSMOS_ENDPOINT");
    if (string.IsNullOrWhiteSpace(endpoint))
    {
        endpoint = cfg["Cosmos:Endpoint"]; // expected when using MSI or key
    }
    var key = cfg["Cosmos:Key"]; // may be blank/placeholder when using MSI

    bool KeyLooksValid(string? k)
    {
        if (string.IsNullOrWhiteSpace(k))
            return false;
        try
        {
            _ = Convert.FromBase64String(k);
            return true;
        }
        catch { return false; }
    }

    CosmosClient client;
    if (!string.IsNullOrWhiteSpace(endpoint))
    {
        // Configure Cosmos serialization options to use camelCase (matches partition key /childId)
        var cosmosOptions = new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase // ChildId → childId
            }
        };

        if (KeyLooksValid(key))
        {
            client = new CosmosClient(endpoint, key, cosmosOptions);
        }
        else
        {
            // Use system managed identity (DefaultAzureCredential)
            client = new CosmosClient(endpoint, new DefaultAzureCredential(), cosmosOptions);
        }
    }
    else
    {
        // Fallback to Key Vault secrets via CosmosSetup (endpoint + key)
        var setup = sp.GetRequiredService<CosmosSetup>();
        var created = setup.TryCreateClientAsync().GetAwaiter().GetResult();
        if (created is null)
        {
            throw new InvalidOperationException("Cosmos configuration missing: no endpoint in config and secrets could not produce a client.");
        }
        client = created;
    }

    // Provisioning of database and containers is now handled via infrastructure (Bicep).
    // Remove in-code CreateDatabaseIfNotExists / CreateContainerIfNotExists to avoid RBAC startup failures.
    return client;
});
builder.Services.AddSingleton<ICosmosRepository>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var client = sp.GetRequiredService<CosmosClient>();
    var dbName = cfg["Cosmos:DatabaseName"] ?? "elves_demo";
    return new CosmosRepository(client, dbName);
});
builder.Services.AddScoped<IWishlistRepository, WishlistRepository>();
builder.Services.AddScoped<IRecommendationRepository, RecommendationRepository>();
builder.Services.AddScoped<IProfileSnapshotRepository, ProfileSnapshotRepository>();
builder.Services.AddScoped<ILogisticsAssessmentRepository, LogisticsAssessmentRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddSingleton<IStreamResumeStore, InMemoryStreamResumeStore>();
builder.Services.AddSingleton<IStreamMetrics, InMemoryStreamMetrics>();
builder.Services.AddScoped<IStreamEventService, StreamEventService>();
builder.Services.AddSingleton<IFallbackUtils, FallbackUtils>();
builder.Services.AddSingleton<IMetricsService, InMemoryMetricsService>();
builder.Services.AddScoped<ISseStreamService, SseStreamService>();
builder.Services.AddScoped<IWishlistService, WishlistService>();
builder.Services.AddScoped<IRecommendationOrchestrator, RecommendationOrchestrator>();
// Wrap in HubStreamBroadcaster to fan-out to SignalR clients
builder.Services.AddSingleton<InMemoryStreamBroadcaster>();
builder.Services.AddSingleton<IStreamBroadcaster>(sp =>
    new Services.HubStreamBroadcaster(
        sp.GetRequiredService<InMemoryStreamBroadcaster>(),
        sp.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Realtime.DrasiEventsHub>>(),
        sp.GetRequiredService<ILogger<Services.HubStreamBroadcaster>>()));
// US2 services
builder.Services.AddScoped<IProfilePreferenceExtractor, ProfilePreferenceExtractor>();
builder.Services.AddScoped<IAiProfileAgent, AiProfileAgent>();
// US3 services
builder.Services.AddScoped<ILogisticsStatusDeriver, LogisticsStatusDeriver>();
builder.Services.AddScoped<IAiLogisticsAgent, AiLogisticsAgent>();
builder.Services.AddScoped<ILogisticsAssessmentValidator, LogisticsAssessmentValidator>();
// Idempotency store for POST operations
builder.Services.AddSingleton<InMemoryIdempotencyStore>();

// Elf Recommendation Agent: Azure OpenAI + Microsoft Agent Framework, following sample pattern
builder.Services.AddSingleton<AIAgent>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var endpoint = Drasicrhsit.Infrastructure.ConfigurationHelper.GetRequiredValue(
        cfg,
        "AzureOpenAI:Endpoint",
        "AZURE_OPENAI_ENDPOINT");
    var deploymentName = Drasicrhsit.Infrastructure.ConfigurationHelper.GetRequiredValue(
        cfg,
        "AzureOpenAI:DeploymentName",
        "AZURE_OPENAI_DEPLOYMENT_NAME");

    var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
    var chatClient = azureClient.GetChatClient(deploymentName).AsIChatClient();
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ElfRecommendationAgentOptions>>().Value;
    var systemPrompt = options.SystemPromptOverride ?? ElfAgentPrompts.ElfRecommendationAgentSystemPrompt;
    return chatClient.CreateAIAgent(name: "ElfRecommendationAgent", instructions: systemPrompt);
});

builder.Services.AddScoped<IElfAgentOrchestrator, ElfAgentOrchestrator>();
builder.Services.AddSingleton<IEventRepository, EventRepository>();
builder.Services.AddSingleton<IJobRepository, JobRepository>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddScoped<IAgentRationaleService, AgentRationaleService>();
builder.Services.AddSingleton<IReportGenerator, ReportGenerator>();
builder.Services.AddSingleton<IReportRepository, ReportRepository>();
builder.Services.AddSingleton<IAvailabilityService, AvailabilityService>();
builder.Services.AddSingleton<IChildRepository, ChildRepository>();
builder.Services.AddSingleton<IChildProfileService, ChildProfileService>();
builder.Services.AddScoped<IElfRecommendationService, ElfRecommendationService>();
builder.Services.AddScoped<ILogisticsAssessmentService, LogisticsAssessmentService>();
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddSingleton<INotificationMutator>(sp => (NotificationService)sp.GetRequiredService<INotificationService>());
builder.Services.AddSingleton<IEventPublisher, EventHubPublisher>();
builder.Services.AddSingleton<IDrasiRealtimeService, DrasiRealtimeService>();

// Enhanced Agent Framework services
builder.Services.AddScoped<AgentToolLibrary>();

// MultiAgentOrchestrator - creates IChatClient internally to avoid DI issues with preview packages
builder.Services.AddScoped<IMultiAgentOrchestrator, MultiAgentOrchestrator>();
builder.Services.AddScoped<IStreamingAgentService, StreamingAgentService>();
builder.Services.AddScoped<INaughtyNiceEventHandler, NaughtyNiceEventHandler>();

// Azure API Guidelines: ETag service for conditional requests (optimistic concurrency)
builder.Services.AddSingleton<IETagService, ETagService>();

// Re-enable change feed publisher for wishlist events as single authoritative emission source.
builder.Services.AddHostedService<CosmosWishlistChangeFeedPublisher>();
builder.Services.AddHostedService<CosmosRecommendationChangeFeedPublisher>();

// Background service to seed SignalR hub cache with Cosmos DB data
// This ensures frontend can display trending/duplicates/inactive data even without Drasi reactions
builder.Services.AddHostedService<DrasiHubCacheSeeder>();

// CORS configuration - read allowed origins from configuration (supports env override via CORS__AllowedOrigins__0, etc.)
// NOTE: Frontend is now served from the same Container App (same-origin in production), so CORS is mainly for
// local development where frontend runs on a different port.
const string ViteDevServerOrigin = "http://localhost:5173";
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
var allowedOrigins = new List<string>(configuredOrigins);

// Add localhost for development (Vite dev server)
if (!allowedOrigins.Contains(ViteDevServerOrigin))
{
    allowedOrigins.Add(ViteDevServerOrigin);
}

builder.Services.AddCors(o => o.AddPolicy("dev", p => p
    .WithOrigins(allowedOrigins.ToArray())
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// Log startup diagnostics using structured logging (after app.Build so ILogger is available)
if (loadedEnvFilePath is not null)
{
    app.Logger.LogInformation("Loaded .env file from {EnvFilePath}", loadedEnvFilePath);
}
app.Logger.LogInformation("Registered {ServiceName} hosted service", nameof(CosmosWishlistChangeFeedPublisher));
app.Logger.LogInformation("Registered {ServiceName} hosted service", nameof(CosmosRecommendationChangeFeedPublisher));

// CORS must be early in pipeline to handle preflight OPTIONS requests
// Add Access-Control-Allow-Credentials to ALL responses (SignalR negotiate needs this)
app.Use(async (context, next) =>
{
    var origin = context.Request.Headers["Origin"].ToString();

    // For SignalR and all cross-origin requests, ensure credentials header is set
    if (!string.IsNullOrWhiteSpace(origin))
    {
        context.Response.OnStarting(() =>
        {
            // Only add if not already set and origin header is present
            if (!context.Response.Headers.ContainsKey("Access-Control-Allow-Credentials"))
            {
                context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            }
            return Task.CompletedTask;
        });
    }

    await next();
});
app.UseCors("dev");

// Fast-path preflight for cross-origin requests (mainly for local development)
app.Use(async (context, next) =>
{
    if (HttpMethods.IsOptions(context.Request.Method))
    {
        var origin = context.Request.Headers["Origin"].ToString();
        // Reflect origin only if configured as allowed to avoid wildcard with credentials
        // NOTE: Frontend is now same-origin in production, so this is mainly for local development
        var configuredOrigins = context.RequestServices.GetRequiredService<IConfiguration>()
            .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        var allowedList = new List<string>(configuredOrigins);
        allowedList.Add(ViteDevServerOrigin);

        if (!string.IsNullOrWhiteSpace(origin) && allowedList.Contains(origin))
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Vary"] = "Origin";
            context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            var reqHeaders = context.Request.Headers["Access-Control-Request-Headers"].ToString();
            context.Response.Headers["Access-Control-Allow-Headers"] = string.IsNullOrWhiteSpace(reqHeaders) ? "Content-Type, Authorization, x-requested-with" : reqHeaders;
            var reqMethod = context.Request.Headers["Access-Control-Request-Method"].ToString();
            context.Response.Headers["Access-Control-Allow-Methods"] = string.IsNullOrWhiteSpace(reqMethod) ? "GET, POST, OPTIONS" : reqMethod;
        }
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return; // short-circuit
    }
    await next();
});

// Plain health endpoint mapped BEFORE middleware to isolate ingress/auth issues (no role tracking, no enrichers)
app.MapGet("/plain-health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }))
    .WithTags("Infrastructure", "Health");

// WebSocket support for SignalR proxy
app.UseWebSockets();

// Native WebSocket/HTTP proxy for external Drasi hubs (only /hub/* now). Frontend local hub served at /api/hub.
var signalrBaseUrl = Drasicrhsit.Infrastructure.ConfigurationHelper.GetOptionalValue(
    builder.Configuration,
    "Drasi:SignalRBaseUrl",
    "DRASI_SIGNALR_BASE_URL");
if (!string.IsNullOrWhiteSpace(signalrBaseUrl))
{
    if (!Uri.TryCreate(signalrBaseUrl, UriKind.Absolute, out var srBase))
    {
        app.Logger.LogWarning("DRASI_SIGNALR_BASE_URL is not a valid absolute URI: {Url}", signalrBaseUrl);
    }
    else
    {
        // Preserve path; only trim trailing slash
        signalrBaseUrl = srBase.GetLeftPart(UriPartial.Path).TrimEnd('/');
        app.Logger.LogInformation("Configuring WebSocket proxy for external Drasi hubs at {Url}", signalrBaseUrl);

        app.MapWhen(
            context => context.Request.Path.StartsWithSegments("/hub"),
            hubApp =>
            {
                hubApp.Run(async context =>
                {
                    var originalPath = context.Request.Path.Value ?? string.Empty;
                    var forwardPath = originalPath; // no /api strip logic now

                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var targetUri = new UriBuilder(signalrBaseUrl)
                        {
                            Scheme = "ws",
                            Path = forwardPath,
                            Query = context.Request.QueryString.ToString()
                        }.Uri;

                        using var clientWebSocket = new System.Net.WebSockets.ClientWebSocket();
                        foreach (var header in context.Request.Headers)
                        {
                            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                                header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                                header.Key.StartsWith("Sec-WebSocket", StringComparison.OrdinalIgnoreCase))
                                continue;
                            clientWebSocket.Options.SetRequestHeader(header.Key, header.Value);
                        }

                        await clientWebSocket.ConnectAsync(targetUri, context.RequestAborted);
                        using var serverWebSocket = await context.WebSockets.AcceptWebSocketAsync();

                        var clientToServer = RelayWebSocketAsync(clientWebSocket, serverWebSocket, context.RequestAborted);
                        var serverToClient = RelayWebSocketAsync(serverWebSocket, clientWebSocket, context.RequestAborted);
                        await Task.WhenAll(clientToServer, serverToClient);
                    }
                    else
                    {
                        var targetUrl = $"{signalrBaseUrl}{forwardPath}{context.Request.QueryString}";
                        var httpClientFactory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
                        var httpClient = httpClientFactory.CreateClient("drasi-signalr-proxy");
                        var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUrl);
                        foreach (var header in context.Request.Headers)
                        {
                            if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                            {
                                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                            }
                        }
                        if (context.Request.Method == "POST" && context.Request.ContentLength > 0)
                        {
                            requestMessage.Content = new StreamContent(context.Request.Body);
                            if (context.Request.ContentType != null)
                            {
                                requestMessage.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(context.Request.ContentType);
                            }
                        }
                        var response = await httpClient.SendAsync(requestMessage, context.RequestAborted);
                        context.Response.StatusCode = (int)response.StatusCode;
                        foreach (var header in response.Headers.Concat(response.Content.Headers))
                        {
                            if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                            {
                                context.Response.Headers[header.Key] = header.Value.ToArray();
                            }
                        }
                        await response.Content.CopyToAsync(context.Response.Body);
                    }
                });
            });
    }
}

// Option B: If an internal SignalR reaction base URL is provided, proxy /api/hub/* to it.
// Otherwise, serve the in-process hub at /api/hub.
var internalSignalrBaseUrl = Drasicrhsit.Infrastructure.ConfigurationHelper.GetOptionalValue(
    builder.Configuration,
    "Drasi:InternalSignalRBaseUrl",
    "DRASI_SIGNALR_INTERNAL_BASE_URL");
if (!string.IsNullOrWhiteSpace(internalSignalrBaseUrl) && Uri.TryCreate(internalSignalrBaseUrl, UriKind.Absolute, out var internalSrBase))
{
    internalSignalrBaseUrl = internalSrBase.GetLeftPart(UriPartial.Path).TrimEnd('/');
    app.Logger.LogInformation("Configuring internal SignalR proxy for /api/hub to {Url}", internalSignalrBaseUrl);

    app.MapWhen(
        context => context.Request.Path.StartsWithSegments("/api/hub"),
        hubApp =>
        {
            hubApp.Run(async context =>
            {
                var forwardPath = context.Request.Path.Value ?? string.Empty;
                // Strip /api prefix: /api/hub/negotiate → /hub/negotiate
                forwardPath = forwardPath.Replace("/api/hub", "/hub");
                var targetUrl = $"{internalSignalrBaseUrl}{forwardPath}{context.Request.QueryString}";
                var httpClientFactory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient("drasi-signalr-proxy");
                var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUrl);
                foreach (var header in context.Request.Headers)
                {
                    if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    {
                        requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                    }
                }
                if (context.Request.Method == "POST" && context.Request.ContentLength > 0)
                {
                    requestMessage.Content = new StreamContent(context.Request.Body);
                    if (context.Request.ContentType != null)
                    {
                        requestMessage.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(context.Request.ContentType);
                    }
                }
                var response = await httpClient.SendAsync(requestMessage, context.RequestAborted);
                context.Response.StatusCode = (int)response.StatusCode;
                foreach (var header in response.Headers.Concat(response.Content.Headers))
                {
                    if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.Headers[header.Key] = header.Value.ToArray();
                    }
                }
                await response.Content.CopyToAsync(context.Response.Body);
            });
        });
}
else
{
    // In-process hub endpoint for frontend
    // Optimize for SWA proxy: disable WebSockets, use only SSE and LongPolling
    app.MapHub<Realtime.DrasiEventsHub>("/api/hub", options =>
    {
        options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.ServerSentEvents |
                              Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;

        // Increase timeouts for proxied connections
        options.TransportMaxBufferSize = 128 * 1024; // 128KB
        options.ApplicationMaxBufferSize = 128 * 1024;

        // Allow longer initial connection window for negotiate -> connect flow through SWA
        options.CloseOnAuthenticationExpiration = false;
    }).RequireCors("dev");
}

// Ensure CORS credentials header is set for SignalR hub requests
app.Use(async (context, next) =>
{
    await next();

    // Add credentials header to SignalR responses if origin is allowed
    if (context.Request.Path.StartsWithSegments("/api/hub") || context.Request.Path.StartsWithSegments("/hub"))
    {
        var origin = context.Request.Headers["Origin"].ToString();
        if (!string.IsNullOrWhiteSpace(origin) &&
            context.Response.Headers.ContainsKey("Access-Control-Allow-Origin") &&
            !context.Response.Headers.ContainsKey("Access-Control-Allow-Credentials"))
        {
            context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
        }
    }
});

// Lightweight negotiate/transport diagnostics for SignalR hub
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/hub"))
    {
        var isNegotiate = context.Request.Path.Value?.EndsWith("/negotiate", StringComparison.OrdinalIgnoreCase) == true
            || string.Equals(context.Request.Query["negotiate"], "1", StringComparison.OrdinalIgnoreCase);
        var transport = context.Request.Query["transport"].ToString();
        app.Logger.LogInformation("[SignalR] {Method} {Path} negotiate={Negotiate} transport={Transport}",
            context.Request.Method,
            context.Request.Path,
            isNegotiate,
            string.IsNullOrWhiteSpace(transport) ? "(unspecified)" : transport);
    }
    await next();
});

// Helper method for WebSocket relay
static async Task RelayWebSocketAsync(System.Net.WebSockets.WebSocket source,
    System.Net.WebSockets.WebSocket destination, CancellationToken cancellationToken)
{
    var buffer = new byte[4096];
    while (source.State == System.Net.WebSockets.WebSocketState.Open &&
           destination.State == System.Net.WebSockets.WebSocketState.Open)
    {
        var result = await source.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
        {
            await destination.CloseAsync(result.CloseStatus ?? System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                result.CloseStatusDescription, cancellationToken);
            break;
        }

        await destination.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count),
            result.MessageType, result.EndOfMessage, cancellationToken);
    }
}

// Correlation IDs in logs/headers
app.UseCorrelationIds();

// Lightweight role tracking (non-enforcing) + logging and loop prevention
app.UseMiddleware<RoleTrackingMiddleware>();
app.UseMiddleware<LoggingEnricherMiddleware>();
app.UseMiddleware<DrasiLoopPreventionMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();

// Request diagnostics middleware (structured logging) - enabled via configuration
if (app.Configuration.GetValue<bool>("Diagnostics:EnableRequestLogging"))
{
    _ = app.UseRequestDiagnostics();
}

// ProblemDetails + correlation propagation middleware (error first)
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<ResponseCorrelationMiddleware>();

// Basic health & readiness endpoints aligned to common /healthz, /readyz, /livez conventions
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
    .WithTags("Infrastructure", "Health");
app.MapGet("/health", async (HealthCheckService healthCheckService) =>
{
    var report = await healthCheckService.CheckHealthAsync();
    var response = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            data = e.Value.Data,
            duration = e.Value.Duration.TotalMilliseconds
        }),
        totalDuration = report.TotalDuration.TotalMilliseconds
    };
    return report.Status == HealthStatus.Healthy
        ? Results.Ok(response)
        : Results.Json(response, statusCode: 503);
})
    .WithTags("Infrastructure", "Health");
app.MapGet("/readyz", async (IMetricsService m, HealthCheckService healthCheckService) =>
{
    var report = await healthCheckService.CheckHealthAsync(check => check.Tags.Contains("ready"));
    var response = new
    {
        status = report.Status.ToString(),
        streamMetricsCount = m.Snapshot().Counters.Count,
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description
        })
    };
    return report.Status == HealthStatus.Healthy
        ? Results.Ok(response)
        : Results.Json(response, statusCode: 503);
})
    .WithTags("Infrastructure", "Health");
app.MapGet("/livez", () => Results.Ok(new { status = "live" }))
    .WithTags("Infrastructure", "Health");
// Lightweight diagnostics (independent of versioned groups)
app.MapGet("/api/pingz", (CosmosClient? cosmos) => Results.Ok(new { status = "ok", cosmosReady = cosmos is not null, time = DateTime.UtcNow }))
    .WithTags("Debug", "Diagnostics");

// API index + version metadata
IReadOnlyList<string> ApiResources() =>
    ["children", "wishlist-items", "jobs", "reports", "elf-agents", "notifications", "orchestrator", "stream", "copilot"];
app.MapGet("/api", () => Results.Ok(new { version = "v1", resources = ApiResources() }))
    .WithTags("Documentation");
app.MapGet("/api/version", () => Results.Ok(new { semantic = "1.0.0", deprecated = Array.Empty<string>() }))
    .WithTags("Documentation");

// Versioned feature endpoints under /api/v1 (Azure API Guidelines compliant)
var v1 = app.MapGroup("/api/v1");
v1.MapJobsApi();
v1.MapReportsApi();
v1.MapChildrenApi();
v1.MapAgUi();
v1.MapElfAgentsApi();
v1.MapEnhancedAgentApi();
v1.MapOrchestratorApi();
v1.MapDrasiStreamApi();
v1.MapDrasiInsightsApi();
v1.MapHistoricalTrendsApi();
v1.MapCopilotChatApi();
v1.MapDrasiDaprSubscriptions();
v1.MapGet("ping", () => Results.Ok(new { status = "ok" }))
    .WithTags("Debug");

// Temporary diagnostics: ensure insights path resolves without colliding with MapDrasiInsightsApi
// Use /api/v1/diagnostics/insights to validate SWA forwarding and backend routing
v1.MapGet("diagnostics/insights", () => Results.Ok(new
{
    trending = Array.Empty<object>(),
    duplicates = Array.Empty<object>(),
    inactiveChildren = Array.Empty<object>(),
    stats = new { }
})).WithTags("Diagnostics", "Insights");

// Diagnostic echo endpoint to validate POST body handling in Container Apps
app.MapPost("/api/v1/test-echo", async (HttpRequest req, ILogger<Program> logger) =>
{
    try
    {
        using var reader = new StreamReader(req.Body);
        var raw = await reader.ReadToEndAsync();
        logger.LogInformation("[Echo] Received body length {Len}", raw.Length);
        return Results.Ok(new { received = raw, length = raw.Length });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[Echo] Failed to read body");
        return Results.Problem("echo read failed");
    }
}).WithTags("Debug");

// Wishlist submission with idempotency + 201 Created
// Manual JSON parsing variant for wishlist-items to avoid intermittent model binding 400s
// (binding of WishlistDto was returning 400 with empty body in Container Apps). This manual approach
// ensures we always process valid JSON and provide clearer validation errors.
// NOTE: Wishlist storage is the primary operation - recommendation generation is secondary and
// should not cause the request to fail. AI operations use fallback on error.
v1.MapPost("children/{childId}/wishlist-items", async (string childId, HttpRequest request,
    IWishlistService wishlist,
    IRecommendationOrchestrator orchestrator,
    IMetricsService metrics,
    IStreamBroadcaster broadcaster,
    IProfileSnapshotRepository profileRepo,
    IRecommendationRepository recRepo,
    INotificationRepository notifRepo,
    IEventPublisher eventPublisher,
    InMemoryIdempotencyStore idemStore,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    logger.LogInformation("[WishlistItems] Handling POST for child {ChildId}", childId);
    System.Text.Json.Nodes.JsonNode? body;
    try
    {
        body = await request.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>(cancellationToken: ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        logger.LogWarning(ex, "[WishlistItems] Invalid JSON for child {ChildId}", childId);
        return Results.BadRequest(new { error = "invalid json", detail = ex.Message });
    }

    if (body is null)
    {
        logger.LogWarning("[WishlistItems] Null body for child {ChildId}", childId);
        return Results.BadRequest(new { error = "invalid json" });
    }

    string? text = (string?)body["text"];
    string? category = (string?)body["category"];
    double? budgetEstimate = null;
    try
    { budgetEstimate = (double?)body["budgetEstimate"]; }
    catch { /* ignore parse issues */ }

    // Support behavior updates with requestType and statusChange
    string? requestType = (string?)body["requestType"];
    string? statusChange = (string?)body["statusChange"];

    if (string.IsNullOrWhiteSpace(text))
    {
        logger.LogWarning("[WishlistItems] Missing text field for child {ChildId}", childId);
        return Results.BadRequest(new { error = "text required" });
    }

    // Validate that behavior-related text is not submitted as wishlist item
    // This prevents confusion where "needs to improve behavior" is treated as a gift request
    var behaviorKeywords = new[] { "behavior", "behaviour", "naughty", "nice", "improve", "BEHAVIOR REPORT" };
    if (string.IsNullOrWhiteSpace(requestType) && behaviorKeywords.Any(keyword => 
        text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
    {
        logger.LogWarning("[WishlistItems] Behavior-related text detected without requestType for child {ChildId}: {Text}", childId, text);
        return Results.BadRequest(new { 
            error = "Invalid wishlist item",
            detail = "This appears to be a behavior update, not a wishlist item. Use the /api/v1/children/{childId}/letters/behavior endpoint for behavior reports.",
            hint = "Wishlist items should be gift requests (toys, books, games), not behavior descriptions."
        });
    }

    var idemKey = request.Headers["Idempotency-Key"].ToString();
    if (!string.IsNullOrWhiteSpace(idemKey) && idemStore.TryGet(idemKey, out var existing))
    {
        return Results.Ok(existing); // replay original response body (200 OK per guideline for idempotent replay)
    }

    var sw = System.Diagnostics.Stopwatch.StartNew();

    // Primary operation: Store the wishlist item - this MUST succeed
    // Use AddLetterAsync for behavior updates, AddAsync for regular wishlist items
    string itemId;
    object responseItem; // For response body
    try
    {
        if (!string.IsNullOrWhiteSpace(requestType))
        {
            // Behavior update or custom request type
            var letter = await wishlist.AddLetterAsync(childId, requestType, text!, category, budgetEstimate, statusChange, ct);
            itemId = letter.Id;
            responseItem = new { Id = letter.Id, ItemName = letter.ItemName, Category = letter.Category, RequestType = letter.RequestType, StatusChange = letter.StatusChange };
        }
        else
        {
            // Regular wishlist item
            var item = await wishlist.AddAsync(childId, text!, category, budgetEstimate, ct);
            itemId = item.Id;
            responseItem = new { Id = item.Id, ItemName = item.ItemName, Category = item.Category };
        }
    }
    catch (OperationCanceledException)
    {
        throw; // Let cancellation propagate
    }
    catch (CosmosException cex)
    {
        logger.LogError(cex, "[WishlistItems] Cosmos error while storing item for child {ChildId}", childId);
        return Results.Json(new
        {
            title = "Wishlist storage failed",
            status = StatusCodes.Status503ServiceUnavailable,
            detail = cex.Message,
            code = cex.StatusCode.ToString(),
            activityId = cex.ActivityId
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[WishlistItems] Unexpected error while storing item for child {ChildId}", childId);
        return Results.Json(new
        {
            title = "Wishlist storage failed",
            status = StatusCodes.Status500InternalServerError,
            detail = ex.Message
        }, statusCode: StatusCodes.Status500InternalServerError);
    }
    logger.LogInformation("[WishlistItems] Stored wishlist item {ItemId} for child {ChildId}", itemId, childId);

    // Secondary operations: These are best-effort and should not fail the request
    IReadOnlyList<Recommendation> recs;
    string? recommendationSetId = null;
    bool fallbackUsed = false;

    try
    {
        recs = await orchestrator.GenerateAsync(childId, topN: 3, ct);
    }
    catch (OperationCanceledException)
    {
        throw; // Let cancellation propagate
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "[WishlistItems] Failed to generate recommendations for child {ChildId}, using fallback", childId);
        fallbackUsed = true;
        // Use canonical fallback recommendations
        recs = RecommendationService.GetDefaultRecommendations(childId);
    }

    try
    {
        var snapshot = new ProfileSnapshotEntity { ChildId = childId };
        var prefs = new List<string>();
        if (!string.IsNullOrWhiteSpace(category))
        {
            prefs.Add(category!);
        }
        snapshot.Preferences = prefs;
        await profileRepo.StoreAsync(snapshot);

        var set = new RecommendationSetEntity
        {
            ChildId = childId,
            ProfileSnapshotId = snapshot.id,
            FallbackUsed = fallbackUsed,
            GenerationSource = fallbackUsed ? "fallback" : "mixed",
            Items = recs.Select(r => new RecommendationItemEntity
            {
                Id = r.Id,
                Suggestion = r.Suggestion,
                Rationale = r.Rationale,
                BudgetFit = r.BudgetFit,
                Availability = r.Availability?.InStock is null ? "unknown" : (r.Availability.InStock.Value ? "in_stock" : "limited")
            }).ToList()
        };
        await recRepo.StoreAsync(set);
        recommendationSetId = set.id;

        // Extract dynamic properties for broadcaster
        var itemData = responseItem.GetType().GetProperty("Id")?.GetValue(responseItem);
        var itemName = responseItem.GetType().GetProperty("ItemName")?.GetValue(responseItem);
        var itemCategory = responseItem.GetType().GetProperty("Category")?.GetValue(responseItem);

        await broadcaster.PublishAsync(childId, "wishlist-item", new { Id = itemData, ItemName = itemName, Category = itemCategory }, ct);
        await broadcaster.PublishAsync(childId, "recommendation-update", new { recommendationSetId = set.id, items = recs.Select(r => new { r.Id, r.Suggestion, r.Rationale }) }, ct);

        var notif = new NotificationEntity
        {
            ChildId = childId,
            Type = "recommendation",
            Message = fallbackUsed ? "Recommendations ready (using fallback)" : "New recommendations generated",
            RelatedId = set.id,
            State = "unread"
        };
        await notifRepo.StoreAsync(notif);
        await broadcaster.PublishAsync(childId, "notification", new { notif.id, notif.Type, notif.Message, notif.RelatedId, notif.State }, ct);
    }
    catch (OperationCanceledException)
    {
        throw; // Let cancellation propagate
    }
    catch (Exception ex)
    {
        // Log but don't fail the request - the wishlist item was already saved
        logger.LogWarning(ex, "[WishlistItems] Failed to save recommendations/notifications for child {ChildId}", childId);
    }

    sw.Stop();
    metrics.ObserveLatency("wishlist_submit", sw.Elapsed);
    var responseBody = new { wishlistItem = responseItem, recommendationSetId, recommendations = recs, fallbackUsed };
    if (!string.IsNullOrWhiteSpace(idemKey))
    {
        idemStore.Set(idemKey, responseBody);
    }
    return Results.Created($"/api/v1/children/{childId}/wishlist-items/{itemId}", responseBody);
});

// DEBUG: Test endpoint to diagnose change feed processor
app.MapGet("/api/debug/changefeed-test", async (ICosmosRepository cosmos, IEventPublisher publisher, IConfiguration config, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("🔍 Testing change feed processor initialization");
        string wish = config["Cosmos:Containers:Wishlists"] ?? "wishlists";
        string leases = config["Cosmos:Containers:Leases"] ?? "leases";
        logger.LogInformation("📦 Container names: wishlists={Wish}, leases={Leases}", wish, leases);

        var monitored = cosmos.GetContainer(wish);
        var lease = cosmos.GetContainer(leases);
        logger.LogInformation("✅ Got container references");

        return Results.Ok(new { status = "Containers accessible", wishlists = wish, leases });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Change feed test failed");
        return Results.Problem(detail: ex.ToString());
    }
});

// Configure Kestrel to listen on http://localhost:8080 if not already specified
var urls = Drasicrhsit.Infrastructure.ConfigurationHelper.GetOptionalValue(
    app.Configuration,
    "Urls",
    "ASPNETCORE_URLS");
if (string.IsNullOrWhiteSpace(urls))
{
    app.Urls.Add("http://localhost:8080");
}

// Swagger in all environments for now (could restrict to Development)
app.UseSwagger();
app.UseSwaggerUI();

// Serve static frontend files from wwwroot
app.UseDefaultFiles(); // Serves index.html as default
app.UseStaticFiles();

// SPA fallback: serve index.html for client-side routing
app.MapFallbackToFile("index.html");

app.Run();
