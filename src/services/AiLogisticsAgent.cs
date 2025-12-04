using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Drasicrhsit.Infrastructure;

namespace Services;

public interface IAiLogisticsAgent
{
    Task<(string reason, bool fallback)> ExplainAsync(string suggestion, CancellationToken ct = default);
}

public sealed class AiLogisticsAgent : IAiLogisticsAgent
{
    private readonly AIAgent _agent;
    private readonly IFallbackUtils _fallback;
    public AiLogisticsAgent(AIAgent agent, IFallbackUtils fallback)
    {
        _agent = agent;
        _fallback = fallback;
    }

    public async Task<(string reason, bool fallback)> ExplainAsync(string suggestion, CancellationToken ct = default)
    {
        string prompt = $"Provide a short feasibility reason (<=15 words) for shipping the item '{suggestion}'. If unknown availability, say 'Stock or delivery uncertain'.";
        try
        {
            var run = await _agent.RunAsync(prompt, cancellationToken: ct).ConfigureAwait(false);
            var text = (run?.ToString() ?? string.Empty).Trim();
            return (string.IsNullOrWhiteSpace(text) ? "Stock or delivery uncertain" : text, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (_fallback.Rationale("logistics-enrichment-error"), true);
        }
    }
}