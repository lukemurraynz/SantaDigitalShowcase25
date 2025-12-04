using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Services;

namespace Services;

public static class ElfAgentsApi
{
    public static IEndpointRouteBuilder MapElfAgentsApi(this IEndpointRouteBuilder app)
    {
        // Elf Agents readiness endpoint (mirrored under /api via Program.cs route group)
        app.MapGet("elf-agents/readiness", (IAvailabilityService availability, CancellationToken ct) =>
        {
            // Placeholder readiness: service is considered ready if availability service is available.
            return Results.Ok(new { status = "ready" });
        })
        // Removed .WithName to prevent duplicate endpoint name conflicts across multiple route group registrations
        .WithTags("ElfAgents");

        // Elf Agents status endpoint (summary view for observability)
        app.MapGet("elf-agents/status", () =>
        {
            var response = new
            {
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "unknown",
                backlogDepth = 0,
                p95ProcessingLatencySeconds = 0,
                errorRate = 0,
                roles = Array.Empty<object>()
            };

            return Results.Ok(response);
        })
        // Removed .WithName to avoid duplicate global endpoint names when mapped multiple times
        .WithTags("ElfAgents");

        // Azure API Guidelines: Return ETag for conditional requests support
        app.MapGet("children/{childId}/profile", async (string childId, IChildProfileService profiles, IETagService etagService, HttpContext context, CancellationToken ct) =>
        {
            var profile = await profiles.GetChildProfileAsync(childId, ct);
            if (profile is null)
                return Results.NotFound();

            // Generate and include ETag in response header
            var etag = etagService.GenerateETag(profile);
            context.Response.Headers.Append("ETag", etag);

            return Results.Ok(profile);
        })
        .WithTags("Frontend", "Children");

        // Create a profile snapshot (deterministic enrichment baseline)
        // Azure API Guidelines: Return ETag on resource creation
        app.MapPost("children/{childId}/profile", async (string childId,
            bool? ai,
            IProfilePreferenceExtractor extractor,
            IAiProfileAgent aiAgent,
            IProfileSnapshotRepository profiles,
            IETagService etagService,
            HttpContext context,
            CancellationToken ct) =>
        {
            var prefs = await extractor.ExtractAsync(childId, ct);
            string enrichmentSource = "deterministic";
            bool fallback = false;
            string? behaviorSummary = null;
            decimal? budgetCeiling = null;
            if (ai == true)
            {
                var (summary, budget, fb) = await aiAgent.EnrichAsync(childId, prefs, ct);
                behaviorSummary = summary;
                budgetCeiling = budget;
                fallback = fb;
                enrichmentSource = fb ? "fallback" : "ai";
            }
            var snapshot = new ProfileSnapshotEntity
            {
                ChildId = childId,
                Preferences = prefs.ToList(),
                BehaviorSummary = behaviorSummary,
                BudgetCeiling = budgetCeiling.HasValue ? (double?)budgetCeiling.Value : null,
                EnrichmentSource = enrichmentSource,
                FallbackUsed = fallback
            };
            await profiles.StoreAsync(snapshot);

            // Generate and include ETag in response
            var etag = etagService.GenerateETag(snapshot);
            context.Response.Headers.Append("ETag", etag);

            return Results.Created($"/api/v1/children/{childId}/profile/{snapshot.id}", snapshot);
        })
        .WithTags("Frontend", "Children");

        app.MapGet("children/{childId}/recommendations", async (string childId, IElfRecommendationService recs, int? limit, CancellationToken ct) =>
        {
            var recommendations = await recs.GetRecommendationsForChildAsync(childId, ct);
            var list = (limit.HasValue && limit.Value > 0) ? recommendations.Take(limit.Value).ToList() : recommendations.ToList();
            return Results.Ok(new { items = list, count = list.Count });
        })
        .WithTags("Frontend", "Recommendations");

        app.MapPost("children/{childId}/logistics", async (string childId,
            ILogisticsAssessmentService logistics,
            ILogisticsAssessmentRepository assessments,
            INotificationRepository notifications,
            IStreamBroadcaster broadcaster,
            CancellationToken ct) =>
        {
            var assessment = await logistics.RunAssessmentAsync(childId, ct);
            if (assessment is null)
                return Results.NotFound();

            var entity = new LogisticsAssessmentEntity
            {
                ChildId = assessment.ChildId,
                RecommendationSetId = assessment.RecommendationSetId,
                CheckedAt = assessment.CheckedAt,
                OverallStatus = assessment.OverallStatus,
                FallbackUsed = false,
                Items = assessment.Items.Select(i => new LogisticsAssessmentItemEntity
                {
                    RecommendationItemId = i.RecommendationId,
                    Feasible = i.Feasible,
                    Reason = i.Reason
                }).ToList()
            };
            await assessments.StoreAsync(entity);

            var notif = new NotificationEntity
            {
                ChildId = childId,
                Type = "logistics",
                Message = $"Logistics assessment {entity.OverallStatus}",
                RelatedId = entity.id,
                State = "unread"
            };
            await notifications.StoreAsync(notif);
            await broadcaster.PublishAsync(childId, "notification", new { notif.id, notif.Type, notif.Message, notif.RelatedId, notif.State }, ct);

            return Results.Ok(entity);
        })
        .WithTags("Frontend", "Logistics");

        app.MapGet("notifications", async (string? state, int? limit, INotificationService notifications, CancellationToken ct) =>
        {
            var items = await notifications.GetNotificationsAsync(state, ct);
            var list = (limit.HasValue && limit.Value > 0) ? items.Take(limit.Value).ToList() : items.ToList();
            return Results.Ok(new { items = list, count = list.Count, continuationToken = (string?)null });
        })
        .WithTags("Frontend", "Notifications");

        // Seed or add a new notification (demo only). In real system, events would drive this.
        app.MapPost("notifications", async (HttpRequest req, INotificationMutator mutator, CancellationToken ct) =>
        {
            var node = await req.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>(cancellationToken: ct);
            if (node is null)
                return Results.BadRequest(new { error = "invalid json" });
            string childId = (string?)node["childId"] ?? "child-demo";
            string type = (string?)node["type"] ?? "info";
            string message = (string?)node["message"] ?? "Notification";
            string state = (string?)node["state"] ?? "new";
            var dto = await mutator.AddAsync(childId, type, message, state, ct);
            return Results.Created($"/notifications/{dto.Id}", dto);
        })
        .WithTags("Debug", "Notifications");

        // SSE stream for real-time notification updates
        // Moved to DrasiStreamApi to avoid duplicate route mapping and AmbiguousMatchException

        // Audit: Retrieve recommendation rationale history
        app.MapGet("audit/rationale/{childId}", async (string childId, string? setId, IRecommendationRepository repo, CancellationToken ct) =>
        {
            var entries = await repo.GetRationaleAuditAsync(childId, setId);
            return Results.Ok(new { childId, entries, count = entries.Count });
        })
        .WithTags("Debug", "Audit");

        // Audit: Retrieve logistics assessment history
        app.MapGet("audit/assessments/{childId}", async (string childId, string? recommendationSetId, ILogisticsAssessmentRepository repo, CancellationToken ct) =>
        {
            var entries = await repo.GetAssessmentHistoryAsync(childId, recommendationSetId);
            return Results.Ok(new { childId, entries, count = entries.Count });
        })
        .WithTags("Debug", "Audit");

        // Secure trigger endpoint for Drasi relay / external orchestrations
        // Azure API Guidelines: Use colon (:) for action operations
        app.MapPost("elf-agents:trigger", async (HttpRequest req,
            IElfAgentOrchestrator orchestrator,
            IConfiguration cfg,
            CancellationToken ct) =>
        {
            string provided = req.Headers["X-Agent-Secret"].ToString();
            string? expected = Environment.GetEnvironmentVariable("AGENT_TRIGGER_SECRET") ?? cfg["Agents:TriggerSecret"];
            if (string.IsNullOrWhiteSpace(expected) || provided != expected)
            {
                return Results.Unauthorized();
            }
            var payload = await req.ReadFromJsonAsync<TriggerPayload>(cancellationToken: ct);
            if (payload is null || string.IsNullOrWhiteSpace(payload.ChildId) || string.IsNullOrWhiteSpace(payload.Type))
            {
                return Results.BadRequest(new { error = "invalid payload" });
            }
            switch (payload.Type.ToLowerInvariant())
            {
                case "profile":
                    await orchestrator.RunProfileEnrichmentAsync(payload.ChildId, ct);
                    break;
                case "recommendation":
                    await orchestrator.RunRecommendationGenerationAsync(payload.ChildId, ct);
                    break;
                case "logistics":
                    await orchestrator.RunLogisticsAssessmentAsync(payload.ChildId, ct);
                    break;
                case "notification":
                    await orchestrator.RunNotificationAggregationAsync(payload.ChildId, ct);
                    break;
                default:
                    return Results.BadRequest(new { error = "unsupported type" });
            }
            return Results.Ok(new { status = "accepted", payload.ChildId, payload.Type });
        })
        // Removed .WithName to avoid duplicate global endpoint names when mapped multiple times
        .WithTags("ElfAgents");

        return app;
    }
}

internal sealed record TriggerPayload(string ChildId, string Type);
