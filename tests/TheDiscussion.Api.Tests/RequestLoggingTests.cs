using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace TheDiscussion.Api.Tests;

/// <summary>AC-7: every request produces a structured JSON log entry with method, route, status code and duration.</summary>
public class RequestLoggingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RequestLogCategory = "TheDiscussion.Api.Logging.RequestLoggingMiddleware";

    [Fact]
    public void Console_logs_use_the_json_formatter()
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;

        Assert.Equal(ConsoleFormatterNames.Json, options.FormatterName);
    }

    [Fact]
    public async Task Each_request_writes_a_json_entry_with_method_route_status_and_duration()
    {
        using var client = factory.CreateClient();
        var marker = Guid.NewGuid().ToString("N");

        using var response = await client.GetAsync($"/health?probe={marker}");

        var entry = await WaitForRequestEntry(entry =>
            Equals(entry.Fields.GetValueOrDefault("Route"), "/health") && entry.Fields.ContainsKey("DurationMs"));

        var jsonFormatter = factory.Services.GetServices<ConsoleFormatter>()
            .Single(formatter => formatter.Name == ConsoleFormatterNames.Json);
        var line = entry.Render(jsonFormatter).Trim();

        using var json = JsonDocument.Parse(line);
        var state = json.RootElement.GetProperty("State");
        Assert.Equal("GET", state.GetProperty("Method").GetString());
        Assert.Equal("/health", state.GetProperty("Route").GetString());
        Assert.Equal(((int)response.StatusCode).ToString(), state.GetProperty("StatusCode").ToString());
        Assert.True(double.TryParse(state.GetProperty("DurationMs").ToString(), System.Globalization.CultureInfo.InvariantCulture, out var duration));
        Assert.True(duration >= 0);
        Assert.Equal(RequestLogCategory, json.RootElement.GetProperty("Category").GetString());
    }

    [Fact]
    public async Task Request_entry_uses_the_route_template_not_the_raw_path()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        using var response = await client.GetAsync($"/api/v1/discussions/{id}");

        var entry = await WaitForRequestEntry(entry =>
            Equals(entry.Fields.GetValueOrDefault("Route"), "/api/v1/discussions/{id:guid}")
            && Equals(entry.Fields.GetValueOrDefault("StatusCode"), 404));
        Assert.Equal("GET", entry.Fields["Method"]);
        Assert.DoesNotContain(id.ToString(), (string)entry.Fields["Route"]!);
    }

    private async Task<CapturedLogEntry> WaitForRequestEntry(Func<CapturedLogEntry, bool> predicate)
    {
        // The middleware logs in a finally block after the response is produced; allow it a moment.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var match = factory.Logs.Entries.LastOrDefault(entry => entry.Category == RequestLogCategory && predicate(entry));
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(20);
        }

        throw new Xunit.Sdk.XunitException("No matching request log entry was written.");
    }
}
