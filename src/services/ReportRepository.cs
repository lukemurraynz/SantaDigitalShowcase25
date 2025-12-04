using Models;

namespace Services;

public interface IReportRepository
{
    Task<Report?> GetAsync(string childId, CancellationToken ct = default);
    Task UpsertAsync(Report report, CancellationToken ct = default);
}

public class ReportRepository : IReportRepository
{
    private static readonly InMemoryRepository<string, Report> Store = new();

    public Task<Report?> GetAsync(string childId, CancellationToken ct = default)
    {
        return Store.GetAsync(childId, ct);
    }

    public Task UpsertAsync(Report report, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        return Store.UpsertAsync(report.ChildId, report, ct);
    }
}
