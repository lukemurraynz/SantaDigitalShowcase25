using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using Drasicrhsit.Infrastructure;

namespace Services;

public static class CopilotChatApi
{
    public static IEndpointRouteBuilder MapCopilotChatApi(this IEndpointRouteBuilder app)
    {
        // Pseudo streaming via Microsoft Agent Framework AIAgent (RunAsync returns full result).
        // When upstream exposes true token streaming, replace chunking logic with native stream.
        app.MapGet("copilot/chat/stream", async (HttpContext ctx, string message, Microsoft.Agents.AI.AIAgent agent, CancellationToken ct) =>
        {
            SseWriter.Prepare(ctx.Response);
            string sanitized = (message ?? string.Empty).Trim();
            if (sanitized.Length > 2000)
                sanitized = sanitized[..2000]; // basic guardrail
            await SseWriter.WriteEventAsync(ctx.Response, "chat-start", JsonSerializer.Serialize(new { user = sanitized }), ct);

            string prompt = string.IsNullOrWhiteSpace(sanitized)
                ? "User has not provided a message. Provide brief guidance on available actions (create_child, add_wishlist_item, run_logistics_assessment)."
                : sanitized;
            string? full;
            try
            {
                var run = await agent.RunAsync(prompt, cancellationToken: ct).ConfigureAwait(false);
                full = run?.ToString();
            }
            catch (OperationCanceledException)
            {
                await SseWriter.WriteEventAsync(ctx.Response, "chat-complete", JsonSerializer.Serialize(new { text = "(cancelled)" }), CancellationToken.None);
                return;
            }
            catch (Exception ex)
            {
                string fallback = "Temporary error calling agent. You can still use actions: create_child, add_wishlist_item, run_logistics_assessment.";
                await SseWriter.WriteEventAsync(ctx.Response, "chat-error", JsonSerializer.Serialize(new { error = ex.Message }), ct);
                full = fallback;
            }

            string reply = (full ?? "No response.").Trim();
            if (reply.Length == 0)
                reply = "(empty response)";

            var startTime = DateTime.UtcNow;
            int sentChars = 0;
            foreach (string chunk in Chunk(reply, 60))
            {
                sentChars += chunk.Length;
                await SseWriter.WriteEventAsync(ctx.Response, "chat-delta", JsonSerializer.Serialize(new { delta = chunk }), ct);
                if (ct.IsCancellationRequested)
                    break;
            }
            var durationMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
            double cps = durationMs > 0 ? Math.Round(sentChars / (durationMs / 1000.0), 2) : sentChars;
            await SseWriter.WriteEventAsync(ctx.Response, "chat-complete", JsonSerializer.Serialize(new { text = reply, metrics = new { chars = sentChars, ms = (int)durationMs, cps } }), ct);
        })
        .WithTags("Frontend", "Copilot");
        return app;
    }

    private static IEnumerable<string> Chunk(string text, int size)
    {
        for (int i = 0; i < text.Length; i += size)
        {
            yield return text.Substring(i, Math.Min(size, text.Length - i));
        }
    }
}
