using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TheDiscussion.Api.Health;

/// <summary>
/// Writes the <c>GET /health</c> body, for example
/// <c>{"status":"healthy","storage":{"provider":"InMemory","reachable":true}}</c>.
/// The status code is 200 when healthy and 503 when storage is unreachable.
/// </summary>
internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        report.Entries.TryGetValue("storage", out var storage);
        var body = new
        {
            status = StatusName(report.Status),
            storage = new
            {
                provider = storage.Data?.GetValueOrDefault("provider"),
                reachable = storage.Data?.GetValueOrDefault("reachable") as bool? ?? false,
                description = storage.Description,
            },
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
