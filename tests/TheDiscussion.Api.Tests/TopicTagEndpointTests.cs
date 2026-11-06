using System.Net;
using System.Net.Http.Json;
using System.Text;
using static TheDiscussion.Api.Tests.DiscussionApiClient;

namespace TheDiscussion.Api.Tests;

/// <summary>discussion-crud-and-tags AC-4: 1 to 5 distinct topic tags per discussion, as <c>topicTags</c> (in-memory provider).</summary>
public class TopicTagEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly DiscussionApiClient api = new(factory.CreateClient());

    [Fact]
    public async Task A_discussion_is_created_with_several_tags_in_the_order_given()
    {
        string[] tags = ["work.remote", "management", "a", "b.c", "d-e.f.g.h.i"];

        var created = await api.CreateDiscussionAsync(topicTags: [.. tags.Select(tag => $" {tag} ")]);

        Assert.Equal(tags, Tags(created));
        Assert.Equal(tags, Tags(await api.GetDiscussionAsync(Id(created))));
        using var list = await api.Http.GetAsync(DiscussionsPath);
        Assert.Equal(tags, Tags((await ReadJsonAsync(list)).EnumerateArray().Single(item => Id(item) == Id(created))));
    }

    [Fact]
    public async Task Responses_carry_topicTags_and_no_longer_topicTag()
    {
        var created = await api.CreateDiscussionAsync();
        using var list = await api.Http.GetAsync(DiscussionsPath);
        var entry = (await ReadJsonAsync(list)).EnumerateArray().Single(item => Id(item) == Id(created));

        Assert.False(created.TryGetProperty("topicTag", out _));
        Assert.False(entry.TryGetProperty("topicTag", out _));
        Assert.Equal(["work.remote"], Tags(entry));
    }

    public static TheoryData<string, string> InvalidTagLists => new()
    {
        { "missing", """{"title": "T", "thesis": "Thesis"}""" },
        { "null", """{"title": "T", "thesis": "Thesis", "topicTags": null}""" },
        { "empty list", """{"title": "T", "thesis": "Thesis", "topicTags": []}""" },
        { "six tags", """{"title": "T", "thesis": "Thesis", "topicTags": ["a", "b", "c", "d", "e", "f"]}""" },
        { "duplicate", """{"title": "T", "thesis": "Thesis", "topicTags": ["work.remote", "management", "work.remote"]}""" },
        { "duplicate after trimming", """{"title": "T", "thesis": "Thesis", "topicTags": ["work", " work "]}""" },
        { "malformed tag", """{"title": "T", "thesis": "Thesis", "topicTags": ["work", "Work.Remote"]}""" },
        { "six segments", """{"title": "T", "thesis": "Thesis", "topicTags": ["a.b.c.d.e.f"]}""" },
        { "blank tag", """{"title": "T", "thesis": "Thesis", "topicTags": ["work", "  "]}""" },
        { "null tag", """{"title": "T", "thesis": "Thesis", "topicTags": ["work", null]}""" },
        { "a string instead of a list", """{"title": "T", "thesis": "Thesis", "topicTags": "work"}""" },
        { "a number in the list", """{"title": "T", "thesis": "Thesis", "topicTags": ["work", 7]}""" },
        { "the old single field only", """{"title": "T", "thesis": "Thesis", "topicTag": "work"}""" },
    };

    [Theory]
    [MemberData(nameof(InvalidTagLists))]
    public async Task Creating_with_invalid_tags_returns_400_naming_topicTags_and_stores_nothing(string scenario, string json)
    {
        var before = await CountDiscussionsAsync();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await api.Http.PostAsync(DiscussionsPath, content);

        await AssertValidationProblemAsync(response, "topicTags", scenario);
        Assert.Equal(before, await CountDiscussionsAsync());
    }

    [Theory]
    [MemberData(nameof(InvalidTagLists))]
    public async Task Patching_with_invalid_tags_returns_400_naming_topicTags_and_changes_nothing(string scenario, string json)
    {
        if (scenario is "missing" or "the old single field only")
        {
            // Without topicTags a PATCH simply leaves the tags alone (covered by the title-only patch test).
            return;
        }

        var discussion = await api.CreateDiscussionAsync(title: "Tagged", topicTags: ["api.tags.kept", "management"]);
        using var content = new StringContent(json.Replace("\"title\": \"T\", \"thesis\": \"Thesis\", ", string.Empty), Encoding.UTF8, "application/json");

        using var response = await api.Http.PatchAsync($"{DiscussionsPath}/{Id(discussion)}", content);

        await AssertValidationProblemAsync(response, "topicTags", scenario);
        var read = await api.GetDiscussionAsync(Id(discussion));
        Assert.Equal(["api.tags.kept", "management"], Tags(read));
        Assert.Equal("Tagged", read.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Patching_tags_replaces_the_whole_list()
    {
        var discussion = await api.CreateDiscussionAsync(topicTags: ["api.tags.a", "api.tags.b"]);

        using var response = await api.PatchDiscussionAsync(Id(discussion), new { topicTags = new[] { "api.tags.b", "api.tags.c", "api.tags.d" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["api.tags.b", "api.tags.c", "api.tags.d"], Tags(await ReadJsonAsync(response)));
        Assert.Equal(["api.tags.b", "api.tags.c", "api.tags.d"], Tags(await api.GetDiscussionAsync(Id(discussion))));
    }

    [Fact]
    public async Task An_invalid_title_with_valid_tags_changes_neither()
    {
        var discussion = await api.CreateDiscussionAsync(title: "Original", topicTags: ["api.tags.original"]);

        using var response = await api.PatchDiscussionAsync(Id(discussion), new { title = "", topicTags = new[] { "api.tags.new" } });

        await AssertValidationProblemAsync(response, "title", "empty title with valid tags");
        var read = await api.GetDiscussionAsync(Id(discussion));
        Assert.Equal("Original", read.GetProperty("title").GetString());
        Assert.Equal(["api.tags.original"], Tags(read));
    }

    private async Task<int> CountDiscussionsAsync()
    {
        using var response = await api.Http.GetAsync(DiscussionsPath);
        return (await ReadJsonAsync(response)).GetArrayLength();
    }
}
