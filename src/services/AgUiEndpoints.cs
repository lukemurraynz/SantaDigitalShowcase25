using System.Collections.Concurrent;
using System.Text.Json;
using Drasicrhsit.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Services;

public static class AgUiEndpoints
{
    // Active runs keyed by agentId to allow cancellation.
    private static readonly ConcurrentDictionary<string, (string runId, CancellationTokenSource cts)> _activeRuns = new();

    public static IEndpointRouteBuilder MapAgUi(this IEndpointRouteBuilder app)
    {
        app.MapPost("/agents/{agentId}/run", async (string agentId, HttpContext ctx, IDrasiViewClient drasiClient, IConfiguration config, IChildProfileService profileService, CancellationToken ct) =>
        {
            SseWriter.Prepare(ctx.Response);
            var runId = Guid.NewGuid().ToString("n");
            var threadId = Guid.NewGuid().ToString("n");
            var messageId = Guid.NewGuid().ToString("n");
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _activeRuns[agentId] = (runId, cts);

            // Parse AG-UI request body (expected: { threadId, runId, messages, tools, context, forwardedProps })
            AgUiRunRequest? request = null;
            try
            {
                request = await JsonSerializer.DeserializeAsync<AgUiRunRequest>(ctx.Request.Body, cancellationToken: ct);
            }
            catch
            {
                // Fallback for minimal/missing body
            }

            // Ensure request is never null and Messages is always an array
            request ??= new AgUiRunRequest { Messages = new List<AgUiMessage>() };
            request.Messages ??= new List<AgUiMessage>();

            // Extract childId from agentId (format: "elf-agent-{childId}")
            var childId = agentId.StartsWith("elf-agent-") ? agentId["elf-agent-".Length..] : "unknown";

            // Create agent based on agentId
            var agent = await CreateAgentForIdAsync(agentId, ctx.RequestServices);
            if (agent == null)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsJsonAsync(new { error = $"Unknown agent ID: {agentId}" }, ct);
                return;
            }

            try
            {
                // RUN_STARTED (include minimal input for schema compatibility)
                await Send(ctx.Response, new
                {
                    type = "RUN_STARTED",
                    threadId,
                    runId,
                    input = new
                    {
                        threadId,
                        runId,
                        messages = request.Messages ?? new List<AgUiMessage>(),
                        tools = Array.Empty<object>(),
                        context = Array.Empty<object>(),
                        forwardedProps = new { }
                    }
                }, cts.Token);

                // Build chat history from AG-UI messages
                // Microsoft Agent Framework AIAgent.RunAsync accepts string prompt, not ChatMessage list
                // We'll extract the last user message as the prompt

                // TEXT_MESSAGE_START
                await Send(ctx.Response, new { type = "TEXT_MESSAGE_START", messageId, role = "assistant" }, cts.Token);

                // Extract base prompt from messages
                var basePrompt = "Provide recommendations for the focused child.";
                if (request.Messages?.Count > 0)
                {
                    var lastUserMsg = request.Messages.LastOrDefault(m => m.Role?.ToLowerInvariant() == "user");
                    if (lastUserMsg?.Content is not null && lastUserMsg.Content.Length > 0)
                    {
                        basePrompt = lastUserMsg.Content;
                    }
                }

                // Extract Drasi context from request (sent by frontend)
                var drasiContext = await ExtractDrasiContextAsync(request.Context, childId, profileService, drasiClient, config, ct);

                // Build enriched prompt with Drasi real-time insights
                var prompt = BuildEnrichedPrompt(basePrompt, childId, drasiContext);

                // Log the prompt for debugging
                var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("[AgUI] Running agent {AgentId} for child {ChildId} with Drasi context: {HasContext}",
                    agentId, childId, !string.IsNullOrEmpty(drasiContext));
                logger.LogDebug("[AgUI] Prompt preview: {Prompt}", prompt.Length > 200 ? prompt.Substring(0, 200) + "..." : prompt);

                // Invoke AIAgent with streaming via RunAsync
                AgentRunResponse? run = null;
                try
                {
                    logger.LogInformation("[AgUI] Calling agent.RunAsync...");
                    run = await agent.RunAsync(prompt, cancellationToken: cts.Token);
                    logger.LogInformation("[AgUI] agent.RunAsync completed successfully");
                }
                catch (Azure.RequestFailedException azEx)
                {
                    logger.LogError(azEx, "[AgUI] Azure OpenAI request failed: {Message}, Status: {Status}", azEx.Message, azEx.Status);
                    await Send(ctx.Response, new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = $"❌ Azure OpenAI Error ({azEx.Status}): {azEx.Message}" }, cts.Token);
                    await Send(ctx.Response, new { type = "TEXT_MESSAGE_END", messageId }, cts.Token);
                    await Send(ctx.Response, new { type = "RUN_FINISHED", threadId, runId, result = new { status = "failed", error = azEx.Message } }, cts.Token);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "[AgUI] Agent execution failed: {Message}", ex.Message);
                    await Send(ctx.Response, new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = $"❌ Agent Error: {ex.Message}" }, cts.Token);
                    await Send(ctx.Response, new { type = "TEXT_MESSAGE_END", messageId }, cts.Token);
                    await Send(ctx.Response, new { type = "RUN_FINISHED", threadId, runId, result = new { status = "failed", error = ex.Message } }, cts.Token);
                    return;
                }

                // Extract text response from agent
                // Microsoft.Agents.AI.AgentRunResponse has a ToString() method that returns the response content
                var responseText = run?.ToString() ?? "";
                logger.LogInformation("[AgUI] Agent {AgentId} response length: {ResponseLength} chars", agentId, responseText.Length);

                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    await Send(ctx.Response, new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = responseText }, cts.Token);
                }
                else
                {
                    // Send a message indicating no response
                    logger.LogWarning("[AgUI] Agent {AgentId} returned empty response", agentId);
                    await Send(ctx.Response, new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = "⚠️ Agent returned no response. Please check the prompt or agent configuration." }, cts.Token);
                }

                // TEXT_MESSAGE_END
                await Send(ctx.Response, new { type = "TEXT_MESSAGE_END", messageId }, cts.Token);

                // RUN_FINISHED
                await Send(ctx.Response, new { type = "RUN_FINISHED", threadId, runId, result = new { status = "succeeded" } }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogWarning("[AgUI] Agent {AgentId} run was cancelled", agentId);
                // RUN_FINISHED (cancelled)
                if (!ctx.Response.HasStarted)
                {
                    SseWriter.Prepare(ctx.Response);
                }
                await Send(ctx.Response, new { type = "RUN_FINISHED", threadId, runId, result = new { status = "cancelled" } }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "[AgUI] Unhandled exception in agent {AgentId}: {Message}", agentId, ex.Message);
                if (!ctx.Response.HasStarted)
                {
                    SseWriter.Prepare(ctx.Response);
                }
                await Send(ctx.Response, new { type = "ERROR", runId, error = ex.Message, details = ex.ToString() }, CancellationToken.None);
            }
            finally
            {
                _activeRuns.TryRemove(agentId, out _);
            }
        })
        .WithTags("Frontend", "AgUI");

        app.MapPost("/agents/{agentId}/run/raw", async (string agentId, HttpContext ctx, CancellationToken ct) =>
        {
            SseWriter.Prepare(ctx.Response);
            var runId = Guid.NewGuid().ToString("n");
            var threadId = Guid.NewGuid().ToString("n");
            await Send(ctx.Response, new { type = "RUN_STARTED", threadId, runId }, ct);
            await Task.Delay(200, ct);
            await Send(ctx.Response, new { type = "RUN_FINISHED", threadId, runId, result = new { status = "succeeded" } }, ct);
        })
        .WithTags("Debug", "AgUI");

        app.MapDelete("/agents/{agentId}/cancel", (string agentId) =>
        {
            if (_activeRuns.TryRemove(agentId, out var tuple))
            {
                tuple.cts.Cancel();
            }
            return Results.NoContent();
        })
        .WithTags("Debug", "AgUI");

        return app;
    }

    private static Task Send(HttpResponse response, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        return SseWriter.WriteEventAsync(response, "message", json, ct);
    }

    /// <summary>
    /// Create appropriate AIAgent based on agentId
    /// </summary>
    private static Task<AIAgent?> CreateAgentForIdAsync(string agentId, IServiceProvider services)
    {
        // Normalize agentId - strip "elf-agent-" prefix if present
        var normalizedId = agentId.StartsWith("elf-agent-") ? "elf" : agentId;

        // Get Azure OpenAI configuration
        var config = services.GetRequiredService<IConfiguration>();
        var endpoint = ConfigurationHelper.GetRequiredValue(
            config,
            "AzureOpenAI:Endpoint",
            "AZURE_OPENAI_ENDPOINT");
        var deploymentName = ConfigurationHelper.GetRequiredValue(
            config,
            "AzureOpenAI:DeploymentName",
            "AZURE_OPENAI_DEPLOYMENT_NAME");

        var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(endpoint), new Azure.Identity.DefaultAzureCredential());
        var chatClient = azureClient.GetChatClient(deploymentName).AsIChatClient();

        // Create agent with appropriate system prompt
        return Task.FromResult<AIAgent?>(normalizedId switch
        {
            "elf" => chatClient.CreateAIAgent(
                name: "ElfRecommendationAgent",
                instructions: ElfAgentPrompts.ElfRecommendationAgentSystemPrompt),

            "santa" => chatClient.CreateAIAgent(
                name: "SantaAnalysisAgent",
                instructions: SantaAgentSystemPrompt),

            _ => null
        });
    }

    /// <summary>
    /// System prompt for Santa Agent - analyzes trends and provides strategic insights
    /// </summary>
    private const string SantaAgentSystemPrompt = """
You are Santa Claus, using your vast experience and real-time workshop intelligence to provide strategic insights.

Your role is to:
- Analyze overall workshop trends and patterns from real-time Drasi Event Graph data
- Identify potential issues before they become problems (budget overruns, inventory concerns, inactive children)
- Provide strategic recommendations for resource allocation and prioritization
- Offer executive-level summaries suitable for dashboard display
- Consider the bigger picture across all children, not just individual recommendations

When provided with Drasi insights, you should:
- Reference specific data points (trending items, duplicate counts, inactive children stats)
- Identify correlations and patterns across the workshop
- Flag concerns that require attention (e.g., high duplicate counts indicating urgent requests)
- Suggest proactive measures based on the data
- Keep responses concise yet insightful - aim for 2-4 paragraphs

Tone: Warm but authoritative, like a wise CEO reviewing business intelligence.
Style: Strategic and data-driven, with specific numbers and clear recommendations.
""";

    /// <summary>
    /// Extract Drasi context from AG-UI request context array and enrich with child profile data
    /// </summary>
    private static async Task<string> ExtractDrasiContextAsync(List<object>? contextList, string? childId, IChildProfileService profileService, IDrasiViewClient drasiClient, IConfiguration config, CancellationToken ct)
    {
        var sections = new List<string>();
        bool hasDrasiContext = false;

        // Extract Drasi insights from context
        if (contextList != null && contextList.Count > 0)
        {
            try
            {
                // Context is sent as JSON objects with type "drasi-insights"
                foreach (var ctx in contextList)
                {
                    var json = JsonSerializer.Serialize(ctx);
                    var node = JsonSerializer.Deserialize<JsonElement>(json);

                    if (node.TryGetProperty("type", out var typeNode) &&
                        typeNode.GetString() == "drasi-insights" &&
                        node.TryGetProperty("data", out var dataNode))
                    {
                        sections.Add(FormatDrasiInsights(dataNode));
                        hasDrasiContext = true;
                        break;
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        // If no Drasi context was provided in the request, fetch it directly from Drasi
        // This ensures Santa Agent always has access to trending data even when no child is selected
        if (!hasDrasiContext)
        {
            try
            {
                var queryContainerId = ConfigurationHelper.GetOptionalValue(config, "Drasi:QueryContainerId", "DRASI_QUERY_CONTAINER_ID") ?? "default";

                // Fetch trending items
                var trendingResults = await drasiClient.GetCurrentResultAsync(queryContainerId, "wishlist-trending-1h", ct);

                // Fetch duplicates
                var duplicatesResults = await drasiClient.GetCurrentResultAsync(queryContainerId, "wishlist-duplicates-by-child", ct);

                // Build insights object
                var insights = new
                {
                    trending = trendingResults.Select(r => new
                    {
                        item = r["item"]?.GetValue<string>(),
                        frequency = r["frequency"]?.GetValue<int>() ?? 0
                    }).ToList(),
                    duplicates = duplicatesResults.Select(r => new
                    {
                        childId = r["childId"]?.GetValue<string>(),
                        item = r["item"]?.GetValue<string>(),
                        count = r["duplicateCount"]?.GetValue<int>() ?? 0
                    }).ToList()
                };

                var insightsJson = JsonSerializer.Serialize(insights);
                var insightsData = JsonSerializer.Deserialize<JsonElement>(insightsJson);
                sections.Add(FormatDrasiInsights(insightsData));
            }
            catch (Exception ex)
            {
                // Log but don't fail - continue without Drasi context
                sections.Add($"⚠️ Unable to fetch real-time Drasi insights: {ex.Message}");
            }
        }

        // Fetch and add child profile data if childId is provided
        if (!string.IsNullOrEmpty(childId) && childId != "unknown")
        {
            try
            {
                var profile = await profileService.GetChildProfileAsync(childId, ct);
                if (profile != null)
                {
                    var profileLines = new List<string>
                    {
                        "",
                        "=== FOCUSED CHILD PROFILE ===",
                        ""
                    };

                    if (!string.IsNullOrEmpty(profile.Id))
                    {
                        profileLines.Add($"Child ID: {profile.Id}");
                    }

                    if (!string.IsNullOrEmpty(profile.Name))
                    {
                        profileLines.Add($"Name: {profile.Name}");
                    }

                    // Behavior status - this is the critical piece for the agent
                    profileLines.Add($"Behavior Status: {profile.Status}");

                    if (profile.Age.HasValue)
                    {
                        profileLines.Add($"Age: {profile.Age}");
                    }

                    if (profile.Preferences != null && profile.Preferences.Length > 0)
                    {
                        profileLines.Add($"Preferences: {string.Join(", ", profile.Preferences)}");
                    }

                    if (profile.Constraints?.Budget != null)
                    {
                        profileLines.Add($"Budget Constraint: ${profile.Constraints.Budget}");
                    }

                    sections.Add(string.Join("\n", profileLines));
                }
            }
            catch
            {
                // Ignore profile fetch errors
            }
        }

        return string.Join("\n", sections);
    }

    /// <summary>
    /// Format Drasi insights as readable context for the agent
    /// </summary>
    private static string FormatDrasiInsights(JsonElement data)
    {
        var lines = new List<string>
        {
            "=== REAL-TIME DRASI INSIGHTS ===",
            ""
        };

        // Trending items
        if (data.TryGetProperty("trending", out var trending) && trending.ValueKind == JsonValueKind.Array)
        {
            lines.Add("🔥 TRENDING GIFTS (Past Hour):");
            var items = trending.EnumerateArray().Take(5).ToList();
            if (items.Count > 0)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item.TryGetProperty("item", out var itemName) &&
                        item.TryGetProperty("frequency", out var freq))
                    {
                        lines.Add($"   {i + 1}. {itemName.GetString()} - {freq.GetInt32()} requests");
                    }
                }
            }
            else
            {
                lines.Add("   (No trending data available)");
            }
            lines.Add("");
        }

        // Duplicates
        if (data.TryGetProperty("duplicates", out var duplicates) && duplicates.ValueKind == JsonValueKind.Array)
        {
            var dupList = duplicates.EnumerateArray().ToList();
            if (dupList.Count > 0)
            {
                lines.Add("⚠️ DUPLICATE REQUESTS (High Priority Items):");
                foreach (var dup in dupList.Take(5))
                {
                    if (dup.TryGetProperty("childId", out var childId) &&
                        dup.TryGetProperty("item", out var item) &&
                        dup.TryGetProperty("count", out var count))
                    {
                        lines.Add($"   - Child {childId.GetString()}: {item.GetString()} ({count.GetInt32()}x)");
                    }
                }
                lines.Add("");
            }
        }

        // Inactive children
        if (data.TryGetProperty("inactiveChildren", out var inactive) && inactive.ValueKind == JsonValueKind.Array)
        {
            var inactiveList = inactive.EnumerateArray().ToList();
            if (inactiveList.Count > 0)
            {
                lines.Add("😴 INACTIVE CHILDREN (3+ Days No Activity):");
                foreach (var child in inactiveList.Take(3))
                {
                    if (child.TryGetProperty("childId", out var childId) &&
                        child.TryGetProperty("lastEventDays", out var days))
                    {
                        lines.Add($"   - {childId.GetString()} (last seen {days.GetInt32()} days ago)");
                    }
                }
                lines.Add("");
            }
        }

        // Behavior status changes (naughty/nice)
        if (data.TryGetProperty("behaviorChanges", out var behaviorChanges) && behaviorChanges.ValueKind == JsonValueKind.Array)
        {
            var changesList = behaviorChanges.EnumerateArray().ToList();
            if (changesList.Count > 0)
            {
                lines.Add("🎅 NAUGHTY/NICE STATUS CHANGES:");
                foreach (var change in changesList.Take(5))
                {
                    if (change.TryGetProperty("childId", out var childId) &&
                        change.TryGetProperty("oldStatus", out var oldStatus) &&
                        change.TryGetProperty("newStatus", out var newStatus))
                    {
                        var emoji = newStatus.GetString() == "Nice" ? "😇" :
                                   newStatus.GetString() == "Naughty" ? "😈" : "❓";
                        var arrow = oldStatus.GetString() == "Nice" ? "📉" : "📈";
                        lines.Add($"   {emoji} {childId.GetString()}: {oldStatus.GetString()} → {newStatus.GetString()} {arrow}");

                        if (change.TryGetProperty("reason", out var reason) && !string.IsNullOrEmpty(reason.GetString()))
                        {
                            lines.Add($"      Reason: {reason.GetString()}");
                        }
                    }
                }
                lines.Add("");
            }
        }

        // Stats
        if (data.TryGetProperty("stats", out var stats))
        {
            lines.Add("📊 WORKSHOP METRICS:");
            if (stats.TryGetProperty("totalEvents", out var totalEvents))
            {
                lines.Add($"   - Total events processed: {totalEvents.GetInt32()}");
            }
            if (stats.TryGetProperty("activeQueries", out var activeQueries))
            {
                lines.Add($"   - Active continuous queries: {activeQueries.GetInt32()}");
            }
            lines.Add("");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Build enriched prompt with Drasi context and user request
    /// </summary>
    private static string BuildEnrichedPrompt(string basePrompt, string childId, string drasiContext)
    {
        if (string.IsNullOrWhiteSpace(drasiContext))
        {
            // No Drasi context available - use base prompt with child context
            return $"""
You are Santa's Chief Elf providing gift recommendations.

CHILD: {childId}

USER REQUEST:
{basePrompt}

Please provide thoughtful recommendations based on the request.
""";
        }

        // Enriched prompt with Drasi real-time insights
        return $"""
You are Santa's Chief Elf analyzing real-time workshop data powered by Drasi Event Graph.

{drasiContext}

FOCUSED CHILD: {childId}

USER REQUEST:
{basePrompt}

INSTRUCTIONS:
- Use the real-time Drasi insights above to inform your recommendations
- **CRITICAL**: Check if the focused child has recent behavior status changes (Naughty/Nice)
  - If child moved from Naughty → Nice: Acknowledge improvement and suggest rewarding items
  - If child moved from Nice → Naughty: Suggest items that encourage better behavior
- If the focused child has duplicate requests, those items are HIGH PRIORITY
- Consider trending items when making suggestions
- Reference specific data points from the Drasi insights in your rationale
- Provide 2-3 specific gift recommendations with clear reasoning

Please provide recommendations based on both the live Drasi insights and the user's request.
""";
    }

    // AG-UI request body schema
    private class AgUiRunRequest
    {
        public string? ThreadId { get; set; }
        public string? RunId { get; set; }
        public List<AgUiMessage>? Messages { get; set; }
        public List<object>? Tools { get; set; }
        public List<object>? Context { get; set; }
        public object? ForwardedProps { get; set; }
    }

    private class AgUiMessage
    {
        public string? Role { get; set; }
        public string? Content { get; set; }
    }
}
