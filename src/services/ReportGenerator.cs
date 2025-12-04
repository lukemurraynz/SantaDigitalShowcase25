using Models;

namespace Services;

public interface IReportGenerator
{
    Task<string> GenerateMarkdownAsync(string childId, IEnumerable<Recommendation> recs, string label, string summary, CancellationToken ct = default);
}

public class ReportGenerator : IReportGenerator
{
    public async Task<string> GenerateMarkdownAsync(string childId, IEnumerable<Recommendation> recs, string label, string summary, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recs);

        var repoRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
        var reportsDir = Path.Combine(repoRoot, "reports");
        Directory.CreateDirectory(reportsDir);

        var path = Path.Combine(reportsDir, $"{childId}.md");
        using var sw = new StreamWriter(path, false);
        await sw.WriteLineAsync($"# Naughty & Nice Gift Report for {childId}");
        await sw.WriteLineAsync("");
        await sw.WriteLineAsync($"Label: **{label}**  ");
        await sw.WriteLineAsync($"Generated: {DateTime.UtcNow:O}  ");
        await sw.WriteLineAsync("");
        await sw.WriteLineAsync($"{summary}");
        await sw.WriteLineAsync("");
        await sw.WriteLineAsync("## Top Recommendations");
        await sw.WriteLineAsync("");
        await sw.WriteLineAsync("| Suggestion | Price | Budget Fit | Rationale |");
        await sw.WriteLineAsync("|-----------|-------:|------------|-----------|");
        foreach (var r in recs)
        {
            var price = r.Price.HasValue ? r.Price.Value.ToString("C2") : "—";
            await sw.WriteLineAsync($"| {r.Suggestion} | {price} | {r.BudgetFit} | {r.Rationale} |");
        }
        await sw.FlushAsync(ct);
        return path;
    }
}
