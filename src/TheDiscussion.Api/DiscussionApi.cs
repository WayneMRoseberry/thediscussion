using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using TheDiscussion.Api.Discussions;
using TheDiscussion.Api.Health;
using TheDiscussion.Api.Logging;
using TheDiscussion.Api.Topics;
using TheDiscussion.Core.Discussions;

namespace TheDiscussion.Api;

/// <summary>Service registration and request pipeline for the REST API.</summary>
public static class DiscussionApi
{
    public static WebApplicationBuilder AddDiscussionApi(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
        builder.Services.AddHealthChecks()
            .AddCheck<StorageHealthCheck>("storage", timeout: TimeSpan.FromSeconds(5));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<DiscussionService>();

        return builder;
    }

    public static WebApplication UseDiscussionApi(this WebApplication app)
    {
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync });
        app.MapDiscussionEndpoints();
        app.MapTopicEndpoints();

        return app;
    }
}
