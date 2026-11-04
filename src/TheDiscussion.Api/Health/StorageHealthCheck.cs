using Microsoft.Extensions.Diagnostics.HealthChecks;
using TheDiscussion.Core.Storage;

namespace TheDiscussion.Api.Health;

/// <summary>Reports whether the configured storage tier is reachable.</summary>
internal sealed class StorageHealthCheck(IDiscussionStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var reachable = await store.CanConnectAsync(cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["provider"] = store.ProviderName,
            ["reachable"] = reachable,
        };

        return reachable
            ? HealthCheckResult.Healthy("Storage is reachable.", data)
            : HealthCheckResult.Unhealthy("Storage is not reachable.", data: data);
    }
}
