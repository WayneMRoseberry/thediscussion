using System.Net;
using System.Text.Json;
using static TheDiscussion.Api.Tests.DiscussionApiClient;

namespace TheDiscussion.Api.Tests;

/// <summary>
/// discussion-crud-and-tags AC-5: <c>GET /api/v1/discussions?tag=</c> and <c>GET /api/v1/topics</c> (in-memory provider).
/// Tests in this class share one store, so each builds its discussions under its own top-level tag.
/// </summary>
public class TopicNavigationEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string TopicsPath = "/api/v1/topics";

    private readonly DiscussionApiClient api = new(factory.CreateClient());

    [Fact]
    public async Task Listing_by_tag_returns_the_tag_and_its_sub_tags_once_each_newest_first()
    {
        var root = UniqueRoot();
        var exact = Id(await api.CreateDiscussionAsync(title: "Exact", topicTags: [root]));
        var child = Id(await api.CreateDiscussionAsync(title: "Child", topicTags: [$"{root}.remote"]));
        var both = Id(await api.CreateDiscussionAsync(title: "Both", topicTags: [$"{root}.remote", $"{root}.remote.tools", "management"]));
        await api.CreateDiscussionAsync(title: "Longer first segment", topicTags: [$"{root}s"]);
        await api.CreateDiscussionAsync(title: "Elsewhere", topicTags: [$"other.{root}"]);

        var underRoot = await ListAsync($"?tag={root}");
        var underRemote = await ListAsync($"?tag={root}.remote");

        Assert.Equal([both, child, exact], underRoot.Select(Id));
        Assert.Equal([both, child], underRemote.Select(Id));
        var entry = underRoot.First();
        Assert.Equal("Both", entry.GetProperty("title").GetString());
        Assert.Equal([$"{root}.remote", $"{root}.remote.tools", "management"], Tags(entry));
    }

    [Fact]
    public async Task A_prefix_that_is_not_a_whole_segment_does_not_match()
    {
        var root = UniqueRoot();
        await api.CreateDiscussionAsync(topicTags: [$"{root}.work"]);

        Assert.Empty(await ListAsync($"?tag={root}.wor"));
        Assert.Empty(await ListAsync($"?tag={root[..^1]}"));
    }

    [Fact]
    public async Task A_valid_tag_with_no_matches_returns_an_empty_list()
    {
        Assert.Empty(await ListAsync($"?tag={UniqueRoot()}.nothing.here"));
    }

    [Fact]
    public async Task Deleted_and_retagged_discussions_leave_the_tag_results()
    {
        var root = UniqueRoot();
        var deleted = Id(await api.CreateDiscussionAsync(topicTags: [root]));
        var retagged = Id(await api.CreateDiscussionAsync(topicTags: [root]));
        var kept = Id(await api.CreateDiscussionAsync(topicTags: [root]));

        (await api.DeleteDiscussionAsync(deleted)).Dispose();
        (await api.PatchDiscussionAsync(retagged, new { topicTags = new[] { $"{root}x" } })).Dispose();

        Assert.Equal([kept], (await ListAsync($"?tag={root}")).Select(Id));
    }

    [Theory]
    [InlineData("?tag=Work")]
    [InlineData("?tag=work..remote")]
    [InlineData("?tag=.work")]
    [InlineData("?tag=a.b.c.d.e.f")]
    [InlineData("?tag=work%20remote")]
    [InlineData("?tag=")]
    [InlineData("?tag=work&tag=science")]
    public async Task An_invalid_tag_returns_400_naming_tag(string query)
    {
        using var response = await api.Http.GetAsync(DiscussionsPath + query);

        await AssertValidationProblemAsync(response, "tag", query);
    }

    [Fact]
    public async Task Topics_form_a_tree_with_distinct_discussion_counts_and_children_sorted_by_name()
    {
        var root = UniqueRoot();
        await api.CreateDiscussionAsync(topicTags: [$"{root}.remote", $"{root}.remote.tools"]);
        await api.CreateDiscussionAsync(topicTags: [$"{root}.remote"]);
        await api.CreateDiscussionAsync(topicTags: [$"{root}.hours", $"{root}.remote"]);
        await api.CreateDiscussionAsync(topicTags: [$"{root}.hours"]);
        await api.CreateDiscussionAsync(topicTags: [$"{root}.archive.old"]);

        var topics = await TopicsAsync();
        var node = topics.EnumerateArray().Single(topic => topic.GetProperty("tag").GetString() == root);

        Assert.Equal(
            $"{root}:{root}(5)[archive:{root}.archive(1)[old:{root}.archive.old(1)[]],hours:{root}.hours(2)[],remote:{root}.remote(3)[tools:{root}.remote.tools(1)[]]]",
            Describe(node));
    }

    [Fact]
    public async Task A_discussion_with_two_tags_under_the_same_parent_is_counted_once()
    {
        var root = UniqueRoot();
        await api.CreateDiscussionAsync(topicTags: [$"{root}.remote", $"{root}.hours"]);

        var node = (await TopicsAsync()).EnumerateArray().Single(topic => topic.GetProperty("tag").GetString() == root);

        Assert.Equal($"{root}:{root}(1)[hours:{root}.hours(1)[],remote:{root}.remote(1)[]]", Describe(node));
    }

    [Fact]
    public async Task Top_level_topics_are_sorted_by_name_and_follow_deletes()
    {
        var root = UniqueRoot();
        var only = Id(await api.CreateDiscussionAsync(topicTags: [$"{root}.solo"]));
        Assert.Contains(root, (await TopicsAsync()).EnumerateArray().Select(topic => topic.GetProperty("tag").GetString()));

        (await api.DeleteDiscussionAsync(only)).Dispose();
        var names = (await TopicsAsync()).EnumerateArray().Select(topic => topic.GetProperty("name").GetString()!).ToList();

        Assert.DoesNotContain(root, names);
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
    }

    [Fact]
    public async Task Topics_response_shape_is_tag_name_discussionCount_and_children()
    {
        var root = UniqueRoot();
        await api.CreateDiscussionAsync(topicTags: [$"{root}.leaf"]);

        using var response = await api.Http.GetAsync(TopicsPath);
        var node = (await ReadJsonAsync(response)).EnumerateArray().Single(topic => topic.GetProperty("tag").GetString() == root);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["tag", "name", "discussionCount", "children"], node.EnumerateObject().Select(property => property.Name));
        Assert.Equal(JsonValueKind.Array, node.GetProperty("children")[0].GetProperty("children").ValueKind);
    }

    private static string UniqueRoot() => $"t{Guid.NewGuid():N}";

    private static string Describe(JsonElement node) =>
        $"{node.GetProperty("name").GetString()}:{node.GetProperty("tag").GetString()}({node.GetProperty("discussionCount").GetInt32()})" +
        $"[{string.Join(",", node.GetProperty("children").EnumerateArray().Select(Describe))}]";

    private async Task<List<JsonElement>> ListAsync(string query)
    {
        using var response = await api.Http.GetAsync(DiscussionsPath + query);
        var body = await ReadJsonAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{query}: expected 200, got {(int)response.StatusCode} {body}");
        return body.EnumerateArray().ToList();
    }

    private async Task<JsonElement> TopicsAsync()
    {
        using var response = await api.Http.GetAsync(TopicsPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }
}
