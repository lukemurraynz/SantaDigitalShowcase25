# Drasi Client Enhancements Recommendations

## Enhancement Plan

### 1. Add Retry Logic with Polly

**Status**: Ready to implement  
**Files**: `DrasiViewClient.cs`, `Program.cs`

#### Changes Needed:

**Program.cs** - Add resilience pipeline:

```csharp
using Polly;
using Polly.Timeout;

// Configure DrasiViewClient with Polly resilience
builder.Services.AddHttpClient<IDrasiViewClient, DrasiViewClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddResilienceHandler("drasi-pipeline", resilienceBuilder =>
{
    // Retry with exponential backoff
    resilienceBuilder.AddRetry(new()
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<TimeoutRejectedException>()
    });

    // Timeout per attempt
    resilienceBuilder.AddTimeout(TimeSpan.FromSeconds(10));

    // Circuit breaker to prevent cascading failures
    resilienceBuilder.AddCircuitBreaker(new()
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 5,
        BreakDuration = TimeSpan.FromSeconds(15)
    });
});
```

### 2. Add Health Check Registration

**Status**: Health check class created ✅  
**Files**: `Program.cs`

**Program.cs** - Register health check:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<DrasiHealthCheck>(
        "drasi",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "ready", "drasi" });

// Add health check endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

### 3. Add Telemetry & Metrics

**Status**: Recommended  
**Files**: New `DrasiTelemetry.cs`

Create metrics for Drasi operations:

```csharp
public class DrasiTelemetry
{
    private readonly Counter<long> _queriesTotal;
    private readonly Counter<long> _queriesFailedTotal;
    private readonly Histogram<double> _queryDurationMs;

    public DrasiTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("Drasi.Client");

        _queriesTotal = meter.CreateCounter<long>(
            "drasi.queries.total",
            "count",
            "Total number of Drasi queries executed");

        _queriesFailedTotal = meter.CreateCounter<long>(
            "drasi.queries.failed.total",
            "count",
            "Total number of failed Drasi queries");

        _queryDurationMs = meter.CreateHistogram<double>(
            "drasi.query.duration",
            "ms",
            "Duration of Drasi query execution");
    }

    public void RecordQuery(string queryId, double durationMs, bool success)
    {
        _queriesTotal.Add(1, new KeyValuePair<string, object?>("query_id", queryId));
        _queryDurationMs.Record(durationMs, new KeyValuePair<string, object?>("query_id", queryId));

        if (!success)
        {
            _queriesFailedTotal.Add(1, new KeyValuePair<string, object?>("query_id", queryId));
        }
    }
}
```

### 4. Add Caching Layer

**Status**: Recommended for high-load scenarios  
**Files**: New `CachedDrasiViewClient.cs`

```csharp
public class CachedDrasiViewClient : IDrasiViewClient
{
    private readonly IDrasiViewClient _innerClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CachedDrasiViewClient> _logger;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(5);

    public async Task<List<JsonNode>> GetCurrentResultAsync(
        string queryContainerId,
        string queryId,
        CancellationToken ct = default)
    {
        var cacheKey = $"drasi:{queryContainerId}:{queryId}";

        if (_cache.TryGetValue<List<JsonNode>>(cacheKey, out var cached))
        {
            _logger.LogDebug("Cache hit for Drasi query {QueryId}", queryId);
            return cached;
        }

        var results = await _innerClient.GetCurrentResultAsync(
            queryContainerId,
            queryId,
            ct);

        _cache.Set(cacheKey, results, _cacheDuration);
        return results;
    }
}
```

**Note**: Only use caching if you can tolerate 5-10 second data staleness.

### 5. Enhance apply-drasi-resources.ps1

**Status**: Already good, minor enhancements available

#### Add Environment Variable Validation

```powershell
function Test-RequiredEnvironment {
    $required = @(
        'AZURE_SUBSCRIPTION_ID',
        'AZURE_RESOURCE_GROUP',
        'AZURE_ENV_NAME'
    )

    $missing = @()
    foreach ($var in $required) {
        if ([string]::IsNullOrWhiteSpace($env:$var)) {
            $value = azd env get-value $var 2>$null
            if ([string]::IsNullOrWhiteSpace($value)) {
                $missing += $var
            }
        }
    }

    if ($missing.Count -gt 0) {
        Write-Error "Missing required environment variables: $($missing -join ', ')"
        return $false
    }
    return $true
}

if (-not (Test-RequiredEnvironment)) {
    exit 1
}
```

#### Add Deployment Summary Report

```powershell
function Write-DeploymentSummary {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "   DRASI DEPLOYMENT SUMMARY" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    # Control Plane Status
    $apiPod = kubectl get pods -n $cpNs -l app=drasi-api --no-headers 2>$null
    $rpPod = kubectl get pods -n $cpNs -l app=drasi-resource-provider --no-headers 2>$null

    Write-Host "`n📦 Control Plane:" -ForegroundColor Yellow
    Write-Host "   API: $(if ($apiPod -match '2/2.*Running') { '✅ Running' } else { '❌ Not Ready' })"
    Write-Host "   Resource Provider: $(if ($rpPod -match '2/2.*Running') { '✅ Running' } else { '❌ Not Ready' })"

    # Sources
    $sources = drasi list source -n $cpNs 2>$null
    Write-Host "`n📡 Sources:" -ForegroundColor Yellow
    if ($sources) {
        Write-Host "   $sources"
    } else {
        Write-Host "   ⚠️  No sources deployed"
    }

    # Queries
    $queries = drasi list query -n $cpNs 2>$null
    Write-Host "`n🔍 Continuous Queries:" -ForegroundColor Yellow
    if ($queries) {
        Write-Host "   $queries"
    } else {
        Write-Host "   ⚠️  No queries deployed"
    }

    # View Service
    $viewIp = kubectl get svc default-view-svc -n $cpNs -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>$null
    Write-Host "`n🌐 View Service:" -ForegroundColor Yellow
    if ($viewIp) {
        Write-Host "   Endpoint: http://$viewIp" -ForegroundColor Green
        Write-Host "   Test: curl http://$viewIp/wishlist-trending-1h" -ForegroundColor DarkGray
    } else {
        Write-Host "   ⚠️  LoadBalancer IP not assigned"
    }

    Write-Host "`n========================================`n" -ForegroundColor Cyan
}

# Call at end of script
Write-DeploymentSummary
```

### 6. Frontend Enhancements

#### Add Exponential Backoff for SSE Reconnection

**File**: `frontend/src/components/DrasiInsightsPanel.tsx`

```typescript
const connectSSE = () => {
  let retryCount = 0;
  const MAX_RETRIES = 5;
  const BASE_DELAY = 1000; // 1 second
  const MAX_DELAY = 30000; // 30 seconds

  const connect = () => {
    const es = new EventSource(`${API_URL}/api/v1/drasi/insights/stream`);

    es.onmessage = (e) => {
      try {
        const data = JSON.parse(e.data);
        setLiveUpdate(`🎄 ${data.item} (${data.frequency} requests)`);
        setTimeout(() => setLiveUpdate(""), 5000);
        retryCount = 0; // Reset on success
      } catch {}
    };

    es.onerror = () => {
      es.close();

      if (retryCount >= MAX_RETRIES) {
        console.warn("DrasiInsights: Max SSE retries reached");
        return;
      }

      // Exponential backoff with jitter
      const delay = Math.min(BASE_DELAY * Math.pow(2, retryCount) + Math.random() * 1000, MAX_DELAY);

      retryCount++;
      console.log(`Reconnecting SSE in ${delay}ms (attempt ${retryCount}/${MAX_RETRIES})`);
      setTimeout(connect, delay);
    };

    return es;
  };

  const eventSource = connect();
  return () => eventSource?.close();
};
```

## Priority Ranking

### High Priority (Implement Now)

1. ✅ **Health Check** - Already created, just needs registration
2. **Retry Logic with Polly** - Critical for production reliability
3. **Deployment Summary** - Improves debugging experience

### Medium Priority (Next Sprint)

4. **Telemetry/Metrics** - Important for monitoring
5. **Frontend SSE Backoff** - Improves resilience
6. **Environment Validation** - Catches config errors early

### Low Priority (Nice to Have)

7. **Caching Layer** - Only if you have performance issues
8. **Advanced Observability** - Distributed tracing, APM integration

## Implementation Steps

1. Add Polly resilience pipeline to Program.cs
2. Register DrasiHealthCheck
3. Test health endpoint: `curl http://localhost:5000/health`
4. Add deployment summary function to apply-drasi-resources.ps1
5. Update frontend SSE reconnection logic
6. Deploy and monitor

## Testing

```bash
# Test health check
curl http://localhost:5000/health/ready

# Test with Drasi offline (should show degraded)
kubectl scale deployment default-view-svc -n drasi-system --replicas=0
curl http://localhost:5000/health/ready

# Test retry logic (simulate intermittent failures)
# Check logs for retry attempts

# Restore Drasi
kubectl scale deployment default-view-svc -n drasi-system --replicas=1
```
