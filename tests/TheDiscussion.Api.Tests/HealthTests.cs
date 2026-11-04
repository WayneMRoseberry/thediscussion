using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Storage;

namespace TheDiscussion.Api.Tests;

/// <summary>AC-7: <c>GET /health</c> returns 200 with storage reachability.</summary>
public class HealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_returns_200_with_storage_reachability()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("healthy", body.RootElement.GetProperty("status").GetString());
        var storage = body.RootElement.GetProperty("storage");
        Assert.True(storage.GetProperty("reachable").GetBoolean());
        Assert.Equal("InMemory", storage.GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Health_returns_503_when_storage_is_unreachable()
    {
        using var unreachable = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IDiscussionStore, UnreachableStore>())));
        using var client = unreachable.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.False(body.RootElement.GetProperty("storage").GetProperty("reachable").GetBoolean());
    }

    private sealed class UnreachableStore : IDiscussionStore
    {
        public string ProviderName => "Unreachable";

        public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task CreateDiscussionAsync(DiscussionSummary discussion, Statement thesis, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DiscussionSummary>> ListDiscussionsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Discussion?> GetDiscussionAsync(Guid discussionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddStatementAsync(Statement statement, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UpdateDiscussionAsync(Guid discussionId, string title, string topicTag, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteDiscussionAsync(Guid discussionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdateStatementTextAsync(
            Guid discussionId,
            Guid statementId,
            string text,
            DateTimeOffset editedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteStatementAsync(Guid discussionId, Guid statementId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
