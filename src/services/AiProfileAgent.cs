using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Drasicrhsit.Infrastructure;

namespace Services;

public interface IAiProfileAgent
{
    Task<(string? behaviorSummary, decimal? budgetCeiling, bool fallback)> EnrichAsync(string childId, IReadOnlyList<string> preferences, CancellationToken ct = default);
}

public sealed class AiProfileAgent : IAiProfileAgent
{
    private readonly AIAgent _agent;
    private readonly IFallbackUtils _fallback;
    public AiProfileAgent(AIAgent agent, IFallbackUtils fallback)
    {
        _agent = agent;
        _fallback = fallback;
    }

    public async Task<(string? behaviorSummary, decimal? budgetCeiling, bool fallback)> EnrichAsync(string childId, IReadOnlyList<string> preferences, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preferences); // CA1062/CA1510
        if (preferences.Count == 0)
        {
            return ("Insufficient data for enrichment; minimal profile only.", null, false);
        }
        string prefList = string.Join(", ", preferences);
        string prompt = $"Given child wishlist category preferences: {prefList}. Provide a ONE sentence behavior summary and an estimated budget ceiling number only if clearly implied (else say 'unknown'). Format as 'summary: <text>'; 'budget: <number or unknown>'.";
        try
        {
            var run = await _agent.RunAsync(prompt, cancellationToken: ct).ConfigureAwait(false);
            string content = run?.ToString() ?? string.Empty;
            string? summary = null;
            decimal? budget = null;
            foreach (string line in content.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("summary:", StringComparison.OrdinalIgnoreCase))
                {
                    summary = trimmed.Substring(8).Trim();
                }
                else if (trimmed.StartsWith("budget:", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = trimmed.Substring(7).Trim();
                    if (decimal.TryParse(raw, out var val)) budget = val;
                }
            }
            return (summary ?? "Profile enrichment incomplete", budget, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (_fallback.Rationale("profile-enrichment-error"), null, true);
        }
    }
}