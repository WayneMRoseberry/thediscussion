using System.Diagnostics;

namespace TheDiscussion.Api.Logging;

/// <summary>
/// Writes one structured log entry per request with the method, route template, status code and duration.
/// With the JSON console formatter (configured in appsettings.json) each entry becomes one JSON line.
/// </summary>
internal sealed partial class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        catch
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }

            throw;
        }
        finally
        {
            var duration = Stopwatch.GetElapsedTime(started);
            RequestCompleted(
                logger,
                context.Request.Method,
                RouteOf(context),
                context.Response.StatusCode,
                Math.Round(duration.TotalMilliseconds, 3));
        }
    }

    /// <summary>The matched route template (for example <c>/api/v1/discussions/{id:guid}</c>), or the raw path when no endpoint matched.</summary>
    private static string RouteOf(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText is { } template
            ? (template.StartsWith('/') ? template : "/" + template)
            : context.Request.Path.HasValue ? context.Request.Path.Value! : "/";

    [LoggerMessage(
        EventId = 1000,
        EventName = "RequestCompleted",
        Level = LogLevel.Information,
        Message = "HTTP {Method} {Route} responded {StatusCode} in {DurationMs} ms")]
    private static partial void RequestCompleted(ILogger logger, string method, string route, int statusCode, double durationMs);
}
