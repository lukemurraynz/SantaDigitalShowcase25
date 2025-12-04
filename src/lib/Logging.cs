using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Drasicrhsit.Infrastructure;

public static class Logging
{
    public static IApplicationBuilder UseCorrelationIds(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            var headerName = "x-correlation-id";
            var correlationId = ctx.Request.Headers.TryGetValue(headerName, out var values)
                ? values.ToString()
                : Guid.NewGuid().ToString();

            using (ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Request").BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId
            }))
            {
                ctx.Response.Headers[headerName] = correlationId;
                await next();
            }
        });
    }
}
