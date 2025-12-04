using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Services;

public static class ReportsApi
{
    public static IEndpointRouteBuilder MapReportsApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("reports/{childId}", async (string childId, IReportRepository repo, CancellationToken ct) =>
        {
            var r = await repo.GetAsync(childId, ct);
            if (r is null)
                return Results.NotFound();
            return Results.Ok(new
            {
                childId = r.ChildId,
                path = r.Path,
                createdAt = r.CreatedAt,
                label = r.Label,
                topN = r.Recommendations.Count()
            });
        })
        .WithTags("Frontend", "Reports");
        return app;
    }
}
