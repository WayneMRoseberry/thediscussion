using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using static TheDiscussion.Api.Tests.DiscussionApiClient;

namespace TheDiscussion.Api.Tests;

/// <summary>AC-3: creating discussions and adding every statement kind through the REST API (in-memory provider).</summary>
public class DiscussionEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly DiscussionApiClient api = new(factory.CreateClient());

    [Fact]
    public async Task Creating_a_discussion_returns_201_with_location_and_the_thesis()
    {
        using var response = await api.Http.PostAsJsonAsync(
            DiscussionsPath,
            new { title = "Remote work boosts productivity", topicTag = "work.remote", thesis = "Remote work boosts productivity" });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"{DiscussionsPath}/{Id(body)}", response.Headers.Location?.OriginalString);
        Assert.True(Guid.TryParse(Id(body), out _));
        Assert.Equal("Remote work boosts productivity", body.GetProperty("title").GetString());
        Assert.Equal("work.remote", body.GetProperty("topicTag").GetString());
        Assert.Equal("unsupported", body.GetProperty("status").GetString());
        var thesis = Assert.Single(body.GetProperty("statements").EnumerateArray());
        Assert.Equal("thesis", thesis.GetProperty("kind").GetString());
        Assert.Equal(ThesisId(body), Id(thesis));
        Assert.Equal(JsonValueKind.Null, thesis.GetProperty("parentId").ValueKind);
        Assert.Equal("Remote work boosts productivity", thesis.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Listing_returns_created_discussions()
    {
        var created = await api.CreateDiscussionAsync(title: "Listed discussion", topicTag: "api.listing");

        using var response = await api.Http.GetAsync(DiscussionsPath);
        var list = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = list.EnumerateArray().Single(item => Id(item) == Id(created));
        Assert.Equal("Listed discussion", entry.GetProperty("title").GetString());
        Assert.Equal("api.listing", entry.GetProperty("topicTag").GetString());
    }

    [Fact]
    public async Task Every_statement_kind_is_stored_and_returned_in_the_tree()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var thesis = ThesisId(discussion);

        var allRequired = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired");
        var anySufficient = await api.AddAsync(id, "supportGroup", thesis, groupType: "anySufficient");
        var support = await api.AddAsync(id, "supportingStatement", allRequired, "Fewer interruptions");
        var otherSupport = await api.AddAsync(id, "supportingStatement", anySufficient, "Studies show +13% output");
        var thesisIllustration = await api.AddAsync(id, "illustration", thesis, "e.g. my team shipped early");
        var supportIllustration = await api.AddAsync(id, "illustration", support, "No more desk drive-bys");
        var thesisCounter = await api.AddAsync(id, "counterArgument", thesis, "Collaboration suffers");
        var supportCounter = await api.AddAsync(id, "counterArgument", support, "Slack pings replace them");
        var rebuttal = await api.AddAsync(id, "rebuttal", thesisCounter, "Async docs offset this");
        var deeper = await api.AddAsync(id, "rebuttal", rebuttal, "Docs go stale");
        var deepest = await api.AddAsync(id, "rebuttal", deeper, "Docs are reviewed weekly");

        var tree = await api.GetDiscussionAsync(id);
        var statements = tree.GetProperty("statements").EnumerateArray().ToList();

        Assert.Equal(12, statements.Count);
        Assert.Equal(
            [thesis, allRequired, anySufficient, support, otherSupport, thesisIllustration, supportIllustration, thesisCounter, supportCounter, rebuttal, deeper, deepest],
            statements.Select(Id));
        AssertStatement(tree, allRequired, "supportGroup", thesis);
        Assert.Equal("allRequired", Statement(tree, allRequired).GetProperty("groupType").GetString());
        Assert.Equal("anySufficient", Statement(tree, anySufficient).GetProperty("groupType").GetString());
        Assert.False(Statement(tree, allRequired).TryGetProperty("text", out _));
        AssertStatement(tree, support, "supportingStatement", allRequired, "Fewer interruptions");
        AssertStatement(tree, otherSupport, "supportingStatement", anySufficient, "Studies show +13% output");
        AssertStatement(tree, thesisIllustration, "illustration", thesis, "e.g. my team shipped early");
        AssertStatement(tree, supportIllustration, "illustration", support, "No more desk drive-bys");
        AssertStatement(tree, thesisCounter, "counterArgument", thesis, "Collaboration suffers");
        AssertStatement(tree, supportCounter, "counterArgument", support, "Slack pings replace them");
        AssertStatement(tree, rebuttal, "rebuttal", thesisCounter, "Async docs offset this");
        AssertStatement(tree, deeper, "rebuttal", rebuttal, "Docs go stale");
        AssertStatement(tree, deepest, "rebuttal", deeper, "Docs are reviewed weekly");

        // Computed markers: stands on supports, refuted on counters and rebuttals, nothing on illustrations.
        Assert.False(Statement(tree, support).GetProperty("stands").GetBoolean());
        Assert.True(Statement(tree, otherSupport).GetProperty("stands").GetBoolean());
        Assert.False(Statement(tree, supportCounter).GetProperty("refuted").GetBoolean());
        Assert.False(Statement(tree, deepest).GetProperty("refuted").GetBoolean());
        Assert.True(Statement(tree, deeper).GetProperty("refuted").GetBoolean());
        Assert.False(Statement(tree, rebuttal).GetProperty("refuted").GetBoolean());
        Assert.True(Statement(tree, thesisCounter).GetProperty("refuted").GetBoolean());
        Assert.False(Statement(tree, thesisIllustration).TryGetProperty("stands", out _));
        Assert.False(Statement(tree, thesisIllustration).TryGetProperty("refuted", out _));
        Assert.False(Statement(tree, allRequired).GetProperty("holds").GetBoolean());
        Assert.True(Statement(tree, anySufficient).GetProperty("holds").GetBoolean());
        Assert.Equal("countered", tree.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Adding_a_statement_returns_201_with_the_statement()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);

        using var response = await api.PostStatementAsync(id, new { kind = "counterArgument", parentId = ThesisId(discussion), text = "  Collaboration suffers  " });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("counterArgument", body.GetProperty("kind").GetString());
        Assert.Equal(ThesisId(discussion), body.GetProperty("parentId").GetString());
        Assert.Equal("Collaboration suffers", body.GetProperty("text").GetString());
        Assert.False(body.GetProperty("refuted").GetBoolean());
    }

    // ---- Rejections: 400 problem details naming the field, nothing stored ----

    public static TheoryData<string, string?, string?, string?, string> InvalidDiscussions => new()
    {
        { "missing title", null, "work.remote", "Thesis", "title" },
        { "blank title", "   ", "work.remote", "Thesis", "title" },
        { "title over 200", new string('t', 201), "work.remote", "Thesis", "title" },
        { "missing thesis", "Title", "work.remote", null, "thesis" },
        { "thesis over 5000", "Title", "work.remote", new string('x', 5001), "thesis" },
        { "missing topic tag", "Title", null, "Thesis", "topicTag" },
        { "uppercase topic tag", "Title", "Work.Remote", "Thesis", "topicTag" },
        { "empty segment", "Title", "work..remote", "Thesis", "topicTag" },
        { "six segments", "Title", "a.b.c.d.e.f", "Thesis", "topicTag" },
        { "underscore", "Title", "work_remote", "Thesis", "topicTag" },
    };

    [Theory]
    [MemberData(nameof(InvalidDiscussions))]
    public async Task Invalid_discussion_is_rejected_with_400_naming_the_field(string scenario, string? title, string? topicTag, string? thesis, string field)
    {
        var before = await CountDiscussionsAsync();

        using var response = await api.Http.PostAsJsonAsync(DiscussionsPath, new { title, topicTag, thesis });

        await AssertValidationProblemAsync(response, field, scenario);
        Assert.Equal(before, await CountDiscussionsAsync());
    }

    [Theory]
    [InlineData("missing text", "counterArgument", "thesis", null, null, "text")]
    [InlineData("blank text", "illustration", "thesis", "   ", null, "text")]
    [InlineData("text over 5000", "counterArgument", "thesis", "LONG", null, "text")]
    [InlineData("missing kind", null, "thesis", "Text", null, "kind")]
    [InlineData("unknown kind", "opinion", "thesis", "Text", null, "kind")]
    [InlineData("second thesis", "thesis", "thesis", "Text", null, "kind")]
    [InlineData("missing parent", "counterArgument", null, "Text", null, "parentId")]
    [InlineData("parent not a guid", "counterArgument", "not-a-guid", "Text", null, "parentId")]
    [InlineData("unknown parent", "counterArgument", "unknown", "Text", null, "parentId")]
    [InlineData("parent from another discussion", "counterArgument", "foreign", "Text", null, "parentId")]
    [InlineData("rebuttal targeting an illustration", "rebuttal", "illustration", "Text", null, "parentId")]
    [InlineData("counter targeting an illustration", "counterArgument", "illustration", "Text", null, "parentId")]
    [InlineData("rebuttal targeting the thesis", "rebuttal", "thesis", "Text", null, "parentId")]
    [InlineData("rebuttal targeting a supporting statement", "rebuttal", "support", "Text", null, "parentId")]
    [InlineData("supporting statement under the thesis", "supportingStatement", "thesis", "Text", null, "parentId")]
    [InlineData("support group under a supporting statement", "supportGroup", "support", null, "allRequired", "parentId")]
    [InlineData("counter targeting a support group", "counterArgument", "group", "Text", null, "parentId")]
    [InlineData("illustration targeting a counter", "illustration", "counter", "Text", null, "parentId")]
    [InlineData("support group without group type", "supportGroup", "thesis", null, null, "groupType")]
    [InlineData("support group with unknown group type", "supportGroup", "thesis", null, "someRequired", "groupType")]
    [InlineData("support group with text", "supportGroup", "thesis", "Text", "allRequired", "text")]
    [InlineData("group type on a counter", "counterArgument", "thesis", "Text", "allRequired", "groupType")]
    public async Task Invalid_statement_is_rejected_with_400_naming_the_field(
        string scenario, string? kind, string? parent, string? text, string? groupType, string field)
    {
        var (id, targets) = await CreateDiscussionWithEveryKindAsync();
        var otherDiscussion = await api.CreateDiscussionAsync(title: "Other");
        var parentId = parent switch
        {
            null => null,
            "unknown" => Guid.NewGuid().ToString(),
            "foreign" => ThesisId(otherDiscussion),
            "not-a-guid" => "not-a-guid",
            _ => targets[parent],
        };
        var before = await CountStatementsAsync(id);

        using var response = await api.PostStatementAsync(id, new
        {
            kind,
            parentId,
            text = text == "LONG" ? new string('x', 5001) : text,
            groupType,
        });

        await AssertValidationProblemAsync(response, field, scenario);
        Assert.Equal(before, await CountStatementsAsync(id));
    }

    [Fact]
    public async Task Statement_text_of_exactly_5000_characters_is_accepted()
    {
        var discussion = await api.CreateDiscussionAsync();

        await api.AddAsync(Id(discussion), "counterArgument", ThesisId(discussion), new string('x', 5000));
    }

    [Fact]
    public async Task Malformed_json_is_rejected_with_400_problem_details()
    {
        var discussion = await api.CreateDiscussionAsync();
        using var content = new StringContent("{\"kind\": ", Encoding.UTF8, "application/json");

        using var response = await api.Http.PostAsync($"{DiscussionsPath}/{Id(discussion)}/statements", content);

        await AssertValidationProblemAsync(response, "body", "malformed JSON");
    }

    [Fact]
    public async Task Wrong_json_type_is_rejected_with_400_naming_the_field()
    {
        using var content = new StringContent("{\"title\": 42, \"topicTag\": \"a.b\", \"thesis\": \"x\"}", Encoding.UTF8, "application/json");

        using var response = await api.Http.PostAsync(DiscussionsPath, content);

        await AssertValidationProblemAsync(response, "title", "number instead of string");
    }

    // ---- Not found ----

    [Fact]
    public async Task Unknown_discussion_returns_404_problem_details()
    {
        using var response = await api.Http.GetAsync($"{DiscussionsPath}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJsonAsync(response);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Adding_to_an_unknown_discussion_returns_404()
    {
        using var response = await api.PostStatementAsync(
            Guid.NewGuid().ToString(),
            new { kind = "counterArgument", parentId = Guid.NewGuid().ToString(), text = "Text" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // ---- Status through the API ----

    [Fact]
    public async Task Status_changes_as_statements_are_added()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var thesis = ThesisId(discussion);
        Assert.Equal("unsupported", (await api.GetDiscussionAsync(id)).GetProperty("status").GetString());

        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired");
        Assert.Equal("unsupported", (await api.GetDiscussionAsync(id)).GetProperty("status").GetString());

        var support = await api.AddAsync(id, "supportingStatement", group, "Fewer interruptions");
        Assert.Equal("open", (await api.GetDiscussionAsync(id)).GetProperty("status").GetString());

        var counter = await api.AddAsync(id, "counterArgument", support, "Slack pings replace them");
        var countered = await api.GetDiscussionAsync(id);
        Assert.Equal("countered", countered.GetProperty("status").GetString());
        Assert.False(Statement(countered, support).GetProperty("stands").GetBoolean());

        await api.AddAsync(id, "rebuttal", counter, "Notifications can be paused");
        var tested = await api.GetDiscussionAsync(id);
        Assert.Equal("tested", tested.GetProperty("status").GetString());
        Assert.True(Statement(tested, counter).GetProperty("refuted").GetBoolean());
        Assert.True(Statement(tested, support).GetProperty("stands").GetBoolean());
    }

    private async Task<(string Id, Dictionary<string, string> Targets)> CreateDiscussionWithEveryKindAsync()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var thesis = ThesisId(discussion);
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired");
        var support = await api.AddAsync(id, "supportingStatement", group, "Support");
        var illustration = await api.AddAsync(id, "illustration", thesis, "Illustration");
        var counter = await api.AddAsync(id, "counterArgument", thesis, "Counter");
        var rebuttal = await api.AddAsync(id, "rebuttal", counter, "Rebuttal");
        return (id, new Dictionary<string, string>
        {
            ["thesis"] = thesis,
            ["group"] = group,
            ["support"] = support,
            ["illustration"] = illustration,
            ["counter"] = counter,
            ["rebuttal"] = rebuttal,
        });
    }

    private async Task<int> CountDiscussionsAsync()
    {
        using var response = await api.Http.GetAsync(DiscussionsPath);
        return (await ReadJsonAsync(response)).GetArrayLength();
    }

    private async Task<int> CountStatementsAsync(string id) =>
        (await api.GetDiscussionAsync(id)).GetProperty("statements").GetArrayLength();

    private static void AssertStatement(JsonElement tree, string id, string kind, string parentId, string? text = null)
    {
        var statement = Statement(tree, id);
        Assert.Equal(kind, statement.GetProperty("kind").GetString());
        Assert.Equal(parentId, statement.GetProperty("parentId").GetString());
        if (text is not null)
        {
            Assert.Equal(text, statement.GetProperty("text").GetString());
        }
    }
}
