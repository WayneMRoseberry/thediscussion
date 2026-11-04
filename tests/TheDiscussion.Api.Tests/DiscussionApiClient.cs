using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TheDiscussion.Api.Tests;

/// <summary>Thin helper over the REST API that returns raw JSON so tests assert on the wire format.</summary>
internal sealed class DiscussionApiClient(HttpClient http)
{
    public const string DiscussionsPath = "/api/v1/discussions";

    public HttpClient Http { get; } = http;

    public async Task<JsonElement> CreateDiscussionAsync(
        string title = "Remote work boosts productivity",
        string topicTag = "work.remote",
        string thesis = "Remote work boosts productivity")
    {
        using var response = await Http.PostAsJsonAsync(DiscussionsPath, new { title, topicTag, thesis });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    public async Task<JsonElement> GetDiscussionAsync(string id)
    {
        using var response = await Http.GetAsync($"{DiscussionsPath}/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    public Task<HttpResponseMessage> PostStatementAsync(string discussionId, object body) =>
        Http.PostAsJsonAsync($"{DiscussionsPath}/{discussionId}/statements", body);

    public async Task<JsonElement> AddStatementAsync(string discussionId, object body)
    {
        using var response = await PostStatementAsync(discussionId, body);
        var json = await ReadJsonAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Expected 201 but got {(int)response.StatusCode}: {json}");
        return json;
    }

    public async Task<string> AddAsync(string discussionId, string kind, string parentId, string? text = null, string? groupType = null)
    {
        var created = await AddStatementAsync(discussionId, new { kind, parentId, text, groupType });
        return created.GetProperty("id").GetString()!;
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    public static string Id(JsonElement element) => element.GetProperty("id").GetString()!;

    public static string ThesisId(JsonElement discussion) => discussion.GetProperty("thesisId").GetString()!;

    public static JsonElement Statement(JsonElement discussion, string id) =>
        discussion.GetProperty("statements").EnumerateArray().Single(statement => statement.GetProperty("id").GetString() == id);
}
