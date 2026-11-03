using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace TheDiscussion.Api.Tests;

/// <summary>Hosts the real API in memory, on the in-memory storage provider, and captures its log entries.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Storage:Provider", "InMemory");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }
}
