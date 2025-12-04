using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Services
{
  // Prompt constants for the various Elf agents.
  public static class ElfAgentPrompts
{
    public const string ElfProfileAgentSystemPrompt = """
You are the Elf Profile Agent for Santa's workshop.
Your job is to build a consolidated child profile from the data you are given,
including wishlist items, behavior events, logistics-related metadata, and any
other child records.

Rules:
- Use only the data provided in tools and inputs; do NOT fabricate names, ages,
  preferences, or locations.
- Infer preferences from wishlist items, categories, tags, and repeated patterns.
- Infer constraints such as budget or special notes only when they are explicitly
  present or strongly implied; otherwise leave them null or "unknown".
- Produce a behaviorSummary that briefly characterizes the child's overall
  behavior (for example, "trending nice" or "needs monitoring") based only on
  the events you see.
- Keep the profile per-child and partitioned by childId; never mix data between
  children.

Output:
- Always produce a single ChildProfile object with fields:
  - id (string, required) – the childId provided to you.
  - name (string | null) – if explicitly available, otherwise null.
  - age (integer | null) – if explicitly available or directly computable,
    otherwise null.
  - location (string | null) – region or city, or null if unknown.
  - preferences (string[] | null) – concise labels like "LEGO", "books",
    "outdoor play"; null if you cannot infer any.
  - constraints (object | null) – at minimum budget (number | null).
  - behaviorSummary (string | null) – one short phrase summarizing behavior,
    or null if you cannot assess it.

Keep responses concise and structured. Assume the calling service will store
your output in Cosmos DB partitioned by childId, so be consistent and stable in
how you populate id and related fields.
""";

    public const string ElfRecommendationAgentSystemPrompt = """
You are the Elf Recommendation Agent for Santa's workshop.
Your job is to generate gift recommendations for a specific child based on
their ChildProfile, wishlist items, and any other context you are given. You
must always consider age-appropriateness, budget, and preferences.

Rules:
- Respect the child's constraints:
  - Do not recommend items that clearly exceed the budget unless explicitly
    asked to propose stretch options, and label them accordingly.
  - Avoid items that conflict with known constraints or notes (for example,
    safety issues or banned categories).
- Respect age and safety:
  - Recommendations must be safe and suitable for the child's age range.
- Use preferences and history:
  - Prefer categories and themes the child has shown interest in through
    wishlist items or events.
  - Avoid recommending the exact same item multiple times in a single
    recommendation set.
- Use only the data and tools you are given; do NOT fabricate inventory,
  prices, or capabilities beyond what tools provide.

Output:
- Produce a list of Recommendation objects, each with:
  - id (string) – a unique identifier for this recommendation in the context
    of the child.
  - childId (string) – the childId provided to you.
  - suggestion (string) – concise gift name.
  - rationale (string) – 1–3 sentences explaining why this gift is a good
    match, referencing age, interests, and constraints.
  - price (number | null) – use tool-provided data if available; otherwise null.
  - budgetFit (string) – one of: "within-budget", "stretch", or "unknown".
  - availability (object | null) – only if given by tools (for example,
    inStock, leadTimeDays); otherwise null.

Ensure at least one recommendation when possible; if you genuinely cannot
recommend anything, return an empty list and clearly explain why if the protocol
allows. Keep rationales honest and grounded in actual data.
""";

    public const string ElfLogisticsAgentSystemPrompt = """
You are the Elf Logistics Agent for Santa's workshop.
Your job is to evaluate the logistical feasibility of delivering previously
generated gift recommendations for a child. You focus on stock, lead time, and
deliverability, not on whether the gift is a good match.

Rules:
- Use only the recommendations and logistics-related data passed to you or
  exposed via tools (for example, availability checks, region constraints);
  do NOT fabricate inventory or shipping capabilities.
- Consider:
  - In-stock status and leadTimeDays relative to the holiday deadline.
  - Regional delivery constraints (for example, certain items not deliverable
    to the child's location).
  - Any special constraints from the child's profile (for example, fragile items
    for remote regions).
- Be conservative: if information is missing or ambiguous, treat feasibility as
  uncertain and explain why.

Output:
- Produce a single LogisticsAssessment object with fields:
  - id (string) – unique assessment identifier.
  - childId (string) – the childId being evaluated.
  - recommendationSetId (string) – identifier of the recommendation set you are
    assessing.
  - overallStatus (string) – one of: "feasible", "partial", "infeasible",
    or "unknown".
  - checkedAt (ISO 8601 date-time) – timestamp of the assessment.
  - items (array) – one entry per recommendation, each with:
    - recommendationId (string).
    - feasible (boolean) – whether this item can be delivered in time under
      current conditions.
    - reason (string) – brief explanation (for example, "in stock, 3-day lead
      time", "out of stock in region", "lead time exceeds holiday cutoff").

Align overallStatus with the item-level results:
- "feasible" if all items are feasible.
- "partial" if some are feasible and some are not.
- "infeasible" if none are feasible.
- "unknown" if you lack enough data to decide.

Keep explanations short, factual, and auditable, suitable for Santa's dashboard
and notifications.
""";
}

  // Lightweight orchestration wrapper for Elf-related agents.
  // This is intentionally minimal and keeps the rest of the codebase
  // decoupled from specific Microsoft Agent Framework types.
  public interface IElfAgentOrchestrator
  {
    Task<string> GenerateRecommendationRationaleAsync(string childId, string suggestion, CancellationToken ct = default);
    Task RunProfileEnrichmentAsync(string childId, CancellationToken ct = default);
    Task RunRecommendationGenerationAsync(string childId, CancellationToken ct = default);
    Task RunLogisticsAssessmentAsync(string childId, CancellationToken ct = default);
    Task RunNotificationAggregationAsync(string childId, CancellationToken ct = default);
  }

  public class ElfAgentOrchestrator : IElfAgentOrchestrator
  {
    private static readonly Action<ILogger, string, Exception?> _logAgentCallInfo =
      LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(1, nameof(ElfAgentOrchestrator)),
        "Calling Elf recommendation agent for child {ChildId}.");

    private static readonly Action<ILogger, string, Exception?> _logAgentEmptyWarning =
      LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(2, nameof(ElfAgentOrchestrator)),
        "Elf recommendation agent returned empty response for child {ChildId}; using fallback rationale.");

    private static readonly Action<ILogger, string, Exception?> _logAgentError =
      LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(3, nameof(ElfAgentOrchestrator)),
        "Elf recommendation agent call failed for child {ChildId}; using fallback rationale.");

    private readonly AIAgent _agent;
    private readonly ElfRecommendationAgentOptions _options;
    private readonly ILogger<ElfAgentOrchestrator> _logger;
    private readonly IProfileSnapshotRepository _profiles;
    private readonly IProfilePreferenceExtractor _prefExtractor;
    private readonly IAiProfileAgent _profileAi;
    private readonly IElfRecommendationService _recommendations;
    private readonly IRecommendationOrchestrator _recOrchestrator;
    private readonly IRecommendationRepository _recRepo;
    private readonly ILogisticsAssessmentService _logistics;
    private readonly ILogisticsAssessmentRepository _logisticsRepo;
    private readonly IAiLogisticsAgent _logisticsAi;
    private readonly INotificationRepository _notificationRepo;
    private readonly INotificationMutator _notifications;
    private readonly IStreamBroadcaster _broadcaster;
    private readonly IMetricsService _metrics;

    public ElfAgentOrchestrator(
      AIAgent agent,
      IOptions<ElfRecommendationAgentOptions> options,
      ILogger<ElfAgentOrchestrator> logger,
      IProfileSnapshotRepository profiles,
      IProfilePreferenceExtractor prefExtractor,
      IAiProfileAgent profileAi,
      IElfRecommendationService recommendations,
      IRecommendationOrchestrator recOrchestrator,
      IRecommendationRepository recRepo,
      ILogisticsAssessmentService logistics,
      ILogisticsAssessmentRepository logisticsRepo,
      IAiLogisticsAgent logisticsAi,
      INotificationRepository notificationRepo,
      INotificationMutator notifications,
      IStreamBroadcaster broadcaster,
      IMetricsService metrics)
    {
      _agent = agent ?? throw new ArgumentNullException(nameof(agent));
      _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
      _logger = logger ?? throw new ArgumentNullException(nameof(logger));
      _profiles = profiles;
      _prefExtractor = prefExtractor;
      _profileAi = profileAi;
      _recommendations = recommendations;
      _recOrchestrator = recOrchestrator;
      _recRepo = recRepo;
      _logistics = logistics;
      _logisticsRepo = logisticsRepo;
      _logisticsAi = logisticsAi;
      _notificationRepo = notificationRepo;
      _notifications = notifications;
      _broadcaster = broadcaster;
      _metrics = metrics;
    }

    public async Task<string> GenerateRecommendationRationaleAsync(string childId, string suggestion, CancellationToken ct = default)
    {
      if (string.IsNullOrWhiteSpace(childId)) throw new ArgumentException("ChildId is required.", nameof(childId));
      if (string.IsNullOrWhiteSpace(suggestion)) throw new ArgumentException("Suggestion is required.", nameof(suggestion));

      var prompt = $"""
ChildId: {childId}
Suggestion: {suggestion}

You are the Elf Recommendation Agent. Produce a short, honest rationale (1-3 sentences) explaining why this suggestion is a good match for the child, referencing age, interests, and constraints when known. Do not invent personal details that were not provided.
""";

      try
      {
        _logAgentCallInfo(_logger, childId, null);
        var run = await _agent.RunAsync(prompt, cancellationToken: ct).ConfigureAwait(false);
        var text = run?.ToString();
        _metrics.Increment("agent_recommendation_rationale_runs");
        if (string.IsNullOrWhiteSpace(text))
        {
          _logAgentEmptyWarning(_logger, childId, null);
          return GetFallbackRationale(childId, suggestion);
        }

        return text.Trim();
      }
      catch (Exception ex) when (!(ex is OperationCanceledException))
      {
        _logAgentError(_logger, childId, ex);
        _metrics.Increment("agent_recommendation_rationale_fallbacks");
        return GetFallbackRationale(childId, suggestion);
      }
    }

    private static string GetFallbackRationale(string childId, string suggestion) =>
      $"Recommended '{suggestion}' for child '{childId}' based on their profile and wishlist context.";

    public async Task RunProfileEnrichmentAsync(string childId, CancellationToken ct = default)
    {
      var sw = System.Diagnostics.Stopwatch.StartNew();
      var prefs = await _prefExtractor.ExtractAsync(childId, ct);
      var (behaviorSummary, budgetCeiling, fallback) = await _profileAi.EnrichAsync(childId, prefs, ct);
      var snapshot = new ProfileSnapshotEntity
      {
        ChildId = childId,
        Preferences = prefs.ToList(),
        BehaviorSummary = behaviorSummary,
        BudgetCeiling = budgetCeiling is null ? null : (double?)budgetCeiling,
        EnrichmentSource = fallback ? "fallback" : "ai",
        FallbackUsed = fallback
      };
      await _profiles.StoreAsync(snapshot);
      await _broadcaster.PublishAsync(childId, "profile-updated", new { snapshot.id, snapshot.BehaviorSummary, snapshot.BudgetCeiling }, ct);
      _metrics.Increment("agent_profile_runs");
      if (fallback) _metrics.Increment("agent_profile_fallback");
      _metrics.ObserveLatency("agent_profile_duration", sw.Elapsed);
    }

    public async Task RunRecommendationGenerationAsync(string childId, CancellationToken ct = default)
    {
      var recs = await _recOrchestrator.GenerateAsync(childId, topN: 3, ct);
      var set = new RecommendationSetEntity
      {
        ChildId = childId,
        ProfileSnapshotId = string.Empty,
        FallbackUsed = false,
        GenerationSource = "mixed",
        Items = recs.Select(r => new RecommendationItemEntity
        {
          Id = r.Id,
          Suggestion = r.Suggestion,
          Rationale = r.Rationale,
          BudgetFit = r.BudgetFit,
          Availability = r.Availability?.InStock is null ? "unknown" : (r.Availability.InStock.Value ? "in_stock" : "limited")
        }).ToList()
      };
      await _recRepo.StoreAsync(set);
      await _broadcaster.PublishAsync(childId, "recommendations-generated", new { set.id, items = recs.Select(r => new { r.Id, r.Suggestion }) }, ct);
      _metrics.Increment("agent_recommendation_runs");
    }

    public async Task RunLogisticsAssessmentAsync(string childId, CancellationToken ct = default)
    {
      var sw = System.Diagnostics.Stopwatch.StartNew();
      var assessment = await _logistics.RunAssessmentAsync(childId, ct);
      if (assessment is null) return;
      var entity = new LogisticsAssessmentEntity
      {
        ChildId = assessment.ChildId,
        RecommendationSetId = assessment.RecommendationSetId,
        CheckedAt = assessment.CheckedAt,
        OverallStatus = assessment.OverallStatus,
        FallbackUsed = assessment.FallbackUsed,
        Items = assessment.Items.Select(i => new LogisticsAssessmentItemEntity
        {
          RecommendationItemId = i.RecommendationId,
          Feasible = i.Feasible,
          Reason = i.Reason
        }).ToList()
      };
      await _logisticsRepo.StoreAsync(entity);
      await _broadcaster.PublishAsync(childId, "logistics-assessed", new { entity.id, entity.OverallStatus }, ct);
      _metrics.Increment("agent_logistics_runs");
      if (assessment.FallbackUsed) _metrics.Increment("agent_logistics_fallback");
      _metrics.ObserveLatency("agent_logistics_duration", sw.Elapsed);
    }

    public async Task RunNotificationAggregationAsync(string childId, CancellationToken ct = default)
    {
      var list = new List<NotificationEntity>();
      await foreach (var n in _notificationRepo.ListAsync(childId))
      {
        list.Add(n);
      }
      var summary = new
      {
        childId,
        total = list.Count,
        unread = list.Count(n => n.State == "new" || n.State == "unread"),
        types = list.GroupBy(n => n.Type).Select(g => new { type = g.Key, count = g.Count() })
      };
      var dto = await _notifications.AddAsync(childId, "summary", $"{summary.total} notifications ({summary.unread} unread)", ct: ct);
      await _broadcaster.PublishAsync(childId, "notification-summary", new { dto.Id, summary.total, summary.unread }, ct);
      _metrics.Increment("agent_notification_summary_runs");
    }
  }
}
