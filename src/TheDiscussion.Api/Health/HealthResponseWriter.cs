using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TheDiscussion.Api.Health;

/// <summary>Writes the <c>GET /health</c> body as JSON: overall status plus one entry per check.</summary>
internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var body = new
        {
            status = StatusName(report.Status),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = StatusName(entry.Value.Status),
                    description = entry.Value.Description,
                    data = entry.Value.Data,
                }),
        };

        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }

    private static string StatusName(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "healthy",
        HealthStatus.Degraded => "degraded",
        _ => "unhealthy",
    };
}
