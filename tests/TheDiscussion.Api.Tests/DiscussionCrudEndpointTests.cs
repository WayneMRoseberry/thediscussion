using System.Net;
using System.Text;
using System.Text.Json;
using static TheDiscussion.Api.Tests.DiscussionApiClient;

namespace TheDiscussion.Api.Tests;

/// <summary>
/// discussion-crud-and-tags AC-1 to AC-3: updating and deleting discussions and statements through the REST API
/// (in-memory provider).
/// </summary>
public class DiscussionCrudEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly DiscussionApiClient api = new(factory.CreateClient());

    // ---- AC-1: PATCH and DELETE /api/v1/discussions/{id} ----

    [Fact]
    public async Task Patching_only_the_title_changes_the_title_and_keeps_everything_else()
    {
        var discussion = await api.CreateDiscussionAsync(title: "Before", topicTag: "api.patch");
        var id = Id(discussion);
        var counter = await api.AddAsync(id, "counterArgument", ThesisId(discussion), "Counter");

        using var response = await api.PatchDiscussionAsync(id, new { title = "  After  " });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("After", body.GetProperty("title").GetString());
        Assert.Equal(["api.patch"], Tags(body));
        Assert.Equal(discussion.GetProperty("createdAt").GetString(), body.GetProperty("createdAt").GetString());
        Assert.Equal([ThesisId(discussion), counter], body.GetProperty("statements").EnumerateArray().Select(Id));
        Assert.Equal("unsupported", body.GetProperty("status").GetString());

        var read = await api.GetDiscussionAsync(id);
        Assert.Equal("After", read.GetProperty("title").GetString());
        Assert.Equal(["api.patch"], Tags(read));
    }

    [Fact]
    public async Task Patching_only_the_tag_keeps_the_title()
    {
        var discussion = await api.CreateDiscussionAsync(title: "Keep me", topicTag: "api.patch");

        using var response = await api.PatchDiscussionAsync(Id(discussion), new { topicTags = new[] { "api.patch.tag", "api.other" } });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Keep me", body.GetProperty("title").GetString());
        Assert.Equal(["api.patch.tag", "api.other"], Tags(body));
    }

    [Fact]
    public async Task Patching_both_fields_changes_both_and_the_list_shows_them()
    {
        var discussion = await api.CreateDiscussionAsync(title: "Old", topicTag: "api.patch");

        using var response = await api.PatchDiscussionAsync(Id(discussion), new { title = "New", topicTags = new[] { "api.patch.both" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var list = await api.Http.GetAsync(DiscussionsPath);
        var entry = (await ReadJsonAsync(list)).EnumerateArray().Single(item => Id(item) == Id(discussion));
        Assert.Equal("New", entry.GetProperty("title").GetString());
        Assert.Equal(["api.patch.both"], Tags(entry));
    }

    public static TheoryData<string, string, string> InvalidDiscussionPatches => new()
    {
        { "empty title", """{"title": ""}""", "title" },
        { "blank title", """{"title": "   "}""", "title" },
        { "null title", """{"title": null}""", "title" },
        { "title over 200", $$"""{"title": "{{new string('t', 201)}}"}""", "title" },
        { "title of the wrong type", """{"title": 42}""", "title" },
        { "null tags", """{"topicTags": null}""", "topicTags" },
        { "malformed tag", """{"topicTags": ["Work..Remote"]}""", "topicTags" },
        { "valid title with a malformed tag", """{"title": "Fine", "topicTags": ["a.b.c.d.e.f"]}""", "topicTags" },
        { "no fields", """{}""", "body" },
        { "not an object", """["title"]""", "body" },
        { "malformed JSON", """{"title": """, "body" },
    };

    [Theory]
    [MemberData(nameof(InvalidDiscussionPatches))]
    public async Task Invalid_discussion_patch_returns_400_naming_the_field_and_changes_nothing(string scenario, string json, string field)
    {
        var discussion = await api.CreateDiscussionAsync(title: "Unchanged", topicTag: "api.patch.invalid");
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await api.Http.PatchAsync($"{DiscussionsPath}/{Id(discussion)}", content);

        await AssertValidationProblemAsync(response, field, scenario);
        var read = await api.GetDiscussionAsync(Id(discussion));
        Assert.Equal("Unchanged", read.GetProperty("title").GetString());
        Assert.Equal(["api.patch.invalid"], Tags(read));
    }

    [Fact]
    public async Task Patching_an_unknown_discussion_returns_404()
    {
        using var response = await api.PatchDiscussionAsync(Guid.NewGuid().ToString(), new { title = "Nobody" });

        await AssertNotFoundAsync(response, "unknown discussion");
    }

    [Fact]
    public async Task Deleting_a_discussion_returns_204_and_removes_it_from_get_and_the_list()
    {
        var discussion = await api.CreateDiscussionAsync(title: "Doomed", topicTag: "api.delete");
        var id = Id(discussion);
        var counter = await api.AddAsync(id, "counterArgument", ThesisId(discussion), "Counter");
        await api.AddAsync(id, "rebuttal", counter, "Rebuttal");

        using var response = await api.DeleteDiscussionAsync(id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        using var get = await api.Http.GetAsync($"{DiscussionsPath}/{id}");
        await AssertNotFoundAsync(get, "deleted discussion");
        using var list = await api.Http.GetAsync(DiscussionsPath);
        Assert.DoesNotContain(id, (await ReadJsonAsync(list)).EnumerateArray().Select(Id));
        using var edit = await api.PatchStatementAsync(id, counter, new { text = "Too late" });
        await AssertNotFoundAsync(edit, "statement of a deleted discussion");
        using var again = await api.DeleteDiscussionAsync(id);
        await AssertNotFoundAsync(again, "second delete");
    }

    [Fact]
    public async Task Deleting_an_unknown_discussion_returns_404()
    {
        using var response = await api.DeleteDiscussionAsync(Guid.NewGuid().ToString());

        await AssertNotFoundAsync(response, "unknown discussion");
    }

    // ---- AC-2: PATCH /api/v1/discussions/{id}/statements/{statementId} ----

    [Fact]
    public async Task Statements_report_edited_at_as_null_until_edited()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);

        var added = await api.AddStatementAsync(id, new { kind = "counterArgument", parentId = ThesisId(discussion), text = "Counter" });
        var read = await api.GetDiscussionAsync(id);

        Assert.Equal(JsonValueKind.Null, Statement(discussion, ThesisId(discussion)).GetProperty("editedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, added.GetProperty("editedAt").ValueKind);
        Assert.All(read.GetProperty("statements").EnumerateArray(), s => Assert.Equal(JsonValueKind.Null, s.GetProperty("editedAt").ValueKind));
    }

    [Fact]
    public async Task Editing_a_countered_statement_with_counters_beneath_replaces_its_text_and_marks_it_edited()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var group = await api.AddAsync(id, "supportGroup", ThesisId(discussion), groupType: "allRequired");
        var support = await api.AddAsync(id, "supportingStatement", group, "Fewer interruptions");
        var counter = await api.AddAsync(id, "counterArgument", support, "Slack pings replace them");
        var before = await api.GetDiscussionAsync(id);

        using var response = await api.PatchStatementAsync(id, support, new { text = "  Fewer meetings  " });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(support, Id(body));
        Assert.Equal("Fewer meetings", body.GetProperty("text").GetString());
        Assert.Equal("supportingStatement", body.GetProperty("kind").GetString());
        Assert.Equal(group, body.GetProperty("parentId").GetString());
        Assert.False(body.GetProperty("stands").GetBoolean());
        var editedAt = body.GetProperty("editedAt").GetDateTimeOffset();
        Assert.True(editedAt >= Statement(before, support).GetProperty("createdAt").GetDateTimeOffset());

        var after = await api.GetDiscussionAsync(id);
        var stored = Statement(after, support);
        Assert.Equal("Fewer meetings", stored.GetProperty("text").GetString());
        Assert.Equal(editedAt, stored.GetProperty("editedAt").GetDateTimeOffset());
        Assert.Equal(Statement(before, support).GetProperty("createdAt").GetString(), stored.GetProperty("createdAt").GetString());
        Assert.Equal(JsonValueKind.Null, Statement(after, counter).GetProperty("editedAt").ValueKind);
        Assert.Equal(counter, Id(after.GetProperty("statements").EnumerateArray().Last()));
        Assert.Equal(support, Statement(after, counter).GetProperty("parentId").GetString());
    }

    [Theory]
    [InlineData("thesis")]
    [InlineData("support")]
    [InlineData("illustration")]
    [InlineData("counter")]
    [InlineData("rebuttal")]
    public async Task Every_kind_with_text_can_be_edited(string target)
    {
        var (id, targets) = await CreateDiscussionWithEveryKindAsync();

        using var response = await api.PatchStatementAsync(id, targets[target], new { text = $"Edited {target}" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = Statement(await api.GetDiscussionAsync(id), targets[target]);
        Assert.Equal($"Edited {target}", stored.GetProperty("text").GetString());
        Assert.NotEqual(JsonValueKind.Null, stored.GetProperty("editedAt").ValueKind);
    }

    [Fact]
    public async Task Editing_text_of_exactly_5000_characters_is_accepted()
    {
        var discussion = await api.CreateDiscussionAsync();

        using var response = await api.PatchStatementAsync(Id(discussion), ThesisId(discussion), new { text = new string('x', 5000) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static TheoryData<string, string, string, string> InvalidStatementEdits => new()
    {
        { "support group", "group", """{"text": "Groups have no text"}""", "text" },
        { "missing text", "counter", """{}""", "text" },
        { "null text", "counter", """{"text": null}""", "text" },
        { "empty text", "counter", """{"text": ""}""", "text" },
        { "blank text", "thesis", """{"text": "   "}""", "text" },
        { "text over 5000", "counter", $$"""{"text": "{{new string('x', 5001)}}"}""", "text" },
        { "text of the wrong type", "counter", """{"text": ["a"]}""", "text" },
        { "changing the kind", "counter", """{"text": "New", "kind": "rebuttal"}""", "kind" },
        { "moving the statement", "rebuttal", """{"text": "New", "parentId": "00000000-0000-0000-0000-000000000000"}""", "parentId" },
        { "malformed JSON", "counter", """{"text": """, "body" },
    };

    [Theory]
    [MemberData(nameof(InvalidStatementEdits))]
    public async Task Invalid_statement_edit_returns_400_naming_the_field_and_changes_nothing(string scenario, string target, string json, string field)
    {
        var (id, targets) = await CreateDiscussionWithEveryKindAsync();
        var before = await api.GetDiscussionAsync(id);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await api.Http.PatchAsync($"{DiscussionsPath}/{id}/statements/{targets[target]}", content);

        await AssertValidationProblemAsync(response, field, scenario);
        Assert.Equal(before.GetProperty("statements").ToString(), (await api.GetDiscussionAsync(id)).GetProperty("statements").ToString());
    }

    [Fact]
    public async Task Editing_in_an_unknown_discussion_or_an_unknown_statement_returns_404()
    {
        var discussion = await api.CreateDiscussionAsync();
        var other = await api.CreateDiscussionAsync(title: "Other");

        using var unknownDiscussion = await api.PatchStatementAsync(Guid.NewGuid().ToString(), ThesisId(discussion), new { text = "Text" });
        using var unknownStatement = await api.PatchStatementAsync(Id(discussion), Guid.NewGuid().ToString(), new { text = "Text" });
        using var foreignStatement = await api.PatchStatementAsync(Id(discussion), ThesisId(other), new { text = "Text" });

        await AssertNotFoundAsync(unknownDiscussion, "unknown discussion");
        await AssertNotFoundAsync(unknownStatement, "unknown statement");
        await AssertNotFoundAsync(foreignStatement, "statement of another discussion");
        Assert.Equal("Remote work boosts productivity", Statement(await api.GetDiscussionAsync(Id(other)), ThesisId(other)).GetProperty("text").GetString());
    }

    [Fact]
    public async Task Editing_text_leaves_the_computed_status_unchanged()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var group = await api.AddAsync(id, "supportGroup", ThesisId(discussion), groupType: "allRequired");
        var support = await api.AddAsync(id, "supportingStatement", group, "Fewer interruptions");
        var counter = await api.AddAsync(id, "counterArgument", support, "Slack pings replace them");
        var before = await api.GetDiscussionAsync(id);
        Assert.Equal("countered", before.GetProperty("status").GetString());

        foreach (var (statement, text) in new[] { (support, "Edited support"), (counter, "Edited counter"), (ThesisId(discussion), "Edited thesis") })
        {
            using var response = await api.PatchStatementAsync(id, statement, new { text });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var after = await api.GetDiscussionAsync(id);
        Assert.Equal("countered", after.GetProperty("status").GetString());
        Assert.False(Statement(after, support).GetProperty("stands").GetBoolean());
        Assert.False(Statement(after, counter).GetProperty("refuted").GetBoolean());
        Assert.False(Statement(after, group).GetProperty("holds").GetBoolean());
    }

    // ---- AC-3: DELETE /api/v1/discussions/{id}/statements/{statementId} ----

    [Fact]
    public async Task Deleting_an_unrefuted_counter_returns_204_and_the_status_goes_from_countered_to_open()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var group = await api.AddAsync(id, "supportGroup", ThesisId(discussion), groupType: "allRequired");
        var support = await api.AddAsync(id, "supportingStatement", group, "Fewer interruptions");
        var counter = await api.AddAsync(id, "counterArgument", support, "Slack pings replace them");
        var rebuttal = await api.AddAsync(id, "rebuttal", counter, "Notifications can be paused");
        var deeper = await api.AddAsync(id, "rebuttal", rebuttal, "Nobody pauses them");
        Assert.Equal("countered", await api.StatusAsync(id));

        using var response = await api.DeleteStatementAsync(id, counter);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await api.GetDiscussionAsync(id);
        Assert.Equal("open", after.GetProperty("status").GetString());
        Assert.Equal([ThesisId(discussion), group, support], after.GetProperty("statements").EnumerateArray().Select(Id));
        Assert.True(Statement(after, support).GetProperty("stands").GetBoolean());
        foreach (var gone in new[] { counter, rebuttal, deeper })
        {
            using var edit = await api.PatchStatementAsync(id, gone, new { text = "Gone" });
            await AssertNotFoundAsync(edit, "deleted statement");
        }
    }

    [Fact]
    public async Task Deleting_a_support_group_removes_its_supports_and_everything_beneath_them()
    {
        var (id, targets) = await CreateDiscussionWithEveryKindAsync();

        using var response = await api.DeleteStatementAsync(id, targets["group"]);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await api.GetDiscussionAsync(id);
        Assert.Equal(
            [targets["thesis"], targets["illustration"], targets["counter"], targets["rebuttal"]],
            after.GetProperty("statements").EnumerateArray().Select(Id));
        Assert.Equal("unsupported", after.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Deleting_the_thesis_returns_400_naming_statementId_and_deletes_nothing()
    {
        var (id, targets) = await CreateDiscussionWithEveryKindAsync();
        var before = await api.GetDiscussionAsync(id);

        using var response = await api.DeleteStatementAsync(id, targets["thesis"]);

        var problem = await AssertValidationProblemAsync(response, "statementId", "deleting the thesis");
        Assert.Contains("delete the discussion", problem.GetProperty("errors").GetProperty("statementId")[0].GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before.GetProperty("statements").ToString(), (await api.GetDiscussionAsync(id)).GetProperty("statements").ToString());
    }

    [Fact]
    public async Task Deleting_in_an_unknown_discussion_or_an_unknown_statement_returns_404()
    {
        var discussion = await api.CreateDiscussionAsync();
        var other = await api.CreateDiscussionAsync(title: "Other");
        var otherCounter = await api.AddAsync(Id(other), "counterArgument", ThesisId(other), "Counter");

        using var unknownDiscussion = await api.DeleteStatementAsync(Guid.NewGuid().ToString(), otherCounter);
        using var unknownStatement = await api.DeleteStatementAsync(Id(discussion), Guid.NewGuid().ToString());
        using var foreignStatement = await api.DeleteStatementAsync(Id(discussion), otherCounter);

        await AssertNotFoundAsync(unknownDiscussion, "unknown discussion");
        await AssertNotFoundAsync(unknownStatement, "unknown statement");
        await AssertNotFoundAsync(foreignStatement, "statement of another discussion");
        Assert.Equal(2, (await api.GetDiscussionAsync(Id(other))).GetProperty("statements").GetArrayLength());
    }

    private async Task<(string Id, Dictionary<string, string> Targets)> CreateDiscussionWithEveryKindAsync()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var thesis = ThesisId(discussion);
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired");
        var support = await api.AddAsync(id, "supportingStatement", group, "Support");
        var supportCounter = await api.AddAsync(id, "counterArgument", support, "Support counter");
        var illustration = await api.AddAsync(id, "illustration", thesis, "Illustration");
        var counter = await api.AddAsync(id, "counterArgument", thesis, "Counter");
        var rebuttal = await api.AddAsync(id, "rebuttal", counter, "Rebuttal");
        return (id, new Dictionary<string, string>
        {
            ["thesis"] = thesis,
            ["group"] = group,
            ["support"] = support,
            ["supportCounter"] = supportCounter,
            ["illustration"] = illustration,
            ["counter"] = counter,
            ["rebuttal"] = rebuttal,
        });
    }
}
