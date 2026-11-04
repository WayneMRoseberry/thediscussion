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

    public Task<HttpResponseMessage> PatchDiscussionAsync(string discussionId, object body) =>
        Http.PatchAsJsonAsync($"{DiscussionsPath}/{discussionId}", body);

    public Task<HttpResponseMessage> DeleteDiscussionAsync(string discussionId) =>
        Http.DeleteAsync($"{DiscussionsPath}/{discussionId}");

    public Task<HttpResponseMessage> PatchStatementAsync(string discussionId, string statementId, object body) =>
        Http.PatchAsJsonAsync($"{DiscussionsPath}/{discussionId}/statements/{statementId}", body);

    public Task<HttpResponseMessage> DeleteStatementAsync(string discussionId, string statementId) =>
        Http.DeleteAsync($"{DiscussionsPath}/{discussionId}/statements/{statementId}");

    public async Task<string> StatusAsync(string discussionId) =>
        (await GetDiscussionAsync(discussionId)).GetProperty("status").GetString()!;

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Asserts a 400 RFC 7807 validation problem whose <c>errors</c> name <paramref name="field"/>.</summary>
    public static async Task<JsonElement> AssertValidationProblemAsync(HttpResponseMessage response, string field, string scenario)
    {
        var problem = await ReadJsonAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{scenario}: expected 400, got {(int)response.StatusCode} {problem}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("type").GetString()));
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out var messages), $"{scenario}: expected an error for '{field}' in {errors}");
        Assert.NotEqual(0, messages.GetArrayLength());
        return problem;
    }

    /// <summary>Asserts a 404 RFC 7807 problem.</summary>
    public static async Task AssertNotFoundAsync(HttpResponseMessage response, string scenario)
    {
        var problem = await ReadJsonAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{scenario}: expected 404, got {(int)response.StatusCode} {problem}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }

    public static string Id(JsonElement element) => element.GetProperty("id").GetString()!;

    public static string ThesisId(JsonElement discussion) => discussion.GetProperty("thesisId").GetString()!;

    public static JsonElement Statement(JsonElement discussion, string id) =>
        discussion.GetProperty("statements").EnumerateArray().Single(statement => statement.GetProperty("id").GetString() == id);
}
