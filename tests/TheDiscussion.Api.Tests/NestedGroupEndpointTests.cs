using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Storage;
using static TheDiscussion.Api.Tests.DiscussionApiClient;

namespace TheDiscussion.Api.Tests;

/// <summary>
/// nested-named-groups AC-1 (UC-1, Cluster A): a support group can be added under the thesis, another support group or
/// a supporting statement, at any depth, while every other parent rule stays as it was (in-memory provider).
/// Status values of nested trees belong to AC-2 and are deliberately not asserted here.
/// </summary>
public class NestedGroupEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly DiscussionApiClient api = new(factory.CreateClient());

    // ---- UC-1 main flow, A1 and A2: the newly allowed parents (Cluster A "Parent kind", "Nesting depth") ----

    [Theory]
    [InlineData("the thesis (unchanged)", "thesis", "allRequired")]
    [InlineData("a top-level group (A1)", "topGroup", "anySufficient")]
    [InlineData("a nested group, so the new group is at depth 3 (A1)", "nestedGroup", "allRequired")]
    [InlineData("a supporting statement in a top-level group (A2, sub-argument)", "topSupport", "anySufficient")]
    [InlineData("a supporting statement in a nested group", "nestedSupport", "allRequired")]
    [InlineData("a supporting statement in a sub-argument group (group > support > group > support)", "subArgumentSupport", "anySufficient")]
    public async Task A_support_group_is_accepted_under_a_newly_allowed_parent(string scenario, string parent, string groupType)
    {
        var (id, targets) = await CreateNestedDiscussionAsync();
        var before = await StatementIdsAsync(id);

        var created = await api.AddStatementAsync(id, new { kind = "supportGroup", parentId = targets[parent], name = "Nested group", groupType });

        AssertNewEmptyGroup(created, targets[parent], groupType, scenario);
        var read = await api.GetDiscussionAsync(id);
        Assert.Equal([.. before, Id(created)], read.GetProperty("statements").EnumerateArray().Select(Id));
        AssertListed(read, Id(created), "supportGroup", targets[parent]);
        Assert.Equal(groupType, Statement(read, Id(created)).GetProperty("groupType").GetString());
    }

    // ---- Cluster A "Member mix of a group" (AC-1 part: mixing is allowed) ----

    [Fact]
    public async Task A_group_may_mix_supporting_statements_and_sub_groups()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var group = await api.AddAsync(id, "supportGroup", ThesisId(discussion), groupType: "allRequired");

        var firstSupport = await api.AddAsync(id, "supportingStatement", group, "First support");
        var firstSubGroup = await api.AddAsync(id, "supportGroup", group, groupType: "anySufficient");
        var secondSupport = await api.AddAsync(id, "supportingStatement", group, "Second support");
        var secondSubGroup = await api.AddAsync(id, "supportGroup", group, groupType: "allRequired");
        var supportInSubGroup = await api.AddAsync(id, "supportingStatement", firstSubGroup, "Support in a sub-group");

        var read = await api.GetDiscussionAsync(id);
        Assert.Equal(
            [ThesisId(discussion), group, firstSupport, firstSubGroup, secondSupport, secondSubGroup, supportInSubGroup],
            read.GetProperty("statements").EnumerateArray().Select(Id));
        AssertListed(read, firstSupport, "supportingStatement", group);
        AssertListed(read, firstSubGroup, "supportGroup", group);
        AssertListed(read, secondSupport, "supportingStatement", group);
        AssertListed(read, secondSubGroup, "supportGroup", group);
        AssertListed(read, supportInSubGroup, "supportingStatement", firstSubGroup);
    }

    // ---- UC-1 A3 and A4: the unchanged rules, now exercised at depth ----

    [Theory]
    [InlineData("supporting statement under a nested group (A3)", "supportingStatement", "nestedGroup")]
    [InlineData("supporting statement under a sub-argument group (A3)", "supportingStatement", "subArgumentGroup")]
    [InlineData("counter argument under a supporting statement in a nested group (A4)", "counterArgument", "nestedSupport")]
    [InlineData("illustration under a supporting statement in a nested group (A4)", "illustration", "nestedSupport")]
    [InlineData("counter argument under a supporting statement in a sub-argument group (A4)", "counterArgument", "subArgumentSupport")]
    [InlineData("illustration under a supporting statement in a sub-argument group (A4)", "illustration", "subArgumentSupport")]
    [InlineData("rebuttal under a counter argument on a nested supporting statement (A4)", "rebuttal", "nestedCounter")]
    [InlineData("rebuttal under a rebuttal on a nested supporting statement (A4)", "rebuttal", "nestedRebuttal")]
    public async Task Unchanged_rules_accept_statements_at_depth(string scenario, string kind, string parent)
    {
        var (id, targets) = await CreateNestedDiscussionAsync();

        var created = await api.AddStatementAsync(id, new { kind, parentId = targets[parent], text = "Deep statement" });

        Assert.True(kind == created.GetProperty("kind").GetString(), scenario);
        Assert.Equal(targets[parent], created.GetProperty("parentId").GetString());
        AssertListed(await api.GetDiscussionAsync(id), Id(created), kind, targets[parent]);
    }

    // ---- UC-1 E3: parents that are still rejected ----

    [Theory]
    [InlineData("group under an illustration", "supportGroup", "illustration")]
    [InlineData("group under an illustration on a nested support", "supportGroup", "nestedIllustration")]
    [InlineData("group under a counter argument", "supportGroup", "counter")]
    [InlineData("group under a counter argument on a nested support", "supportGroup", "nestedCounter")]
    [InlineData("group under a rebuttal", "supportGroup", "rebuttal")]
    [InlineData("group under a rebuttal on a nested support", "supportGroup", "nestedRebuttal")]
    [InlineData("supporting statement under the thesis", "supportingStatement", "thesis")]
    [InlineData("supporting statement under a top-level supporting statement", "supportingStatement", "topSupport")]
    [InlineData("supporting statement under a nested supporting statement", "supportingStatement", "nestedSupport")]
    [InlineData("supporting statement under a sub-argument supporting statement", "supportingStatement", "subArgumentSupport")]
    [InlineData("supporting statement under an illustration", "supportingStatement", "illustration")]
    [InlineData("supporting statement under a counter argument", "supportingStatement", "counter")]
    [InlineData("supporting statement under a rebuttal", "supportingStatement", "rebuttal")]
    [InlineData("counter argument under a top-level group", "counterArgument", "topGroup")]
    [InlineData("counter argument under a nested group", "counterArgument", "nestedGroup")]
    [InlineData("counter argument under a sub-argument group", "counterArgument", "subArgumentGroup")]
    [InlineData("illustration under a top-level group", "illustration", "topGroup")]
    [InlineData("illustration under a nested group", "illustration", "nestedGroup")]
    [InlineData("illustration under a sub-argument group", "illustration", "subArgumentGroup")]
    [InlineData("rebuttal under a top-level group", "rebuttal", "topGroup")]
    [InlineData("rebuttal under a nested group", "rebuttal", "nestedGroup")]
    [InlineData("rebuttal under a top-level supporting statement", "rebuttal", "topSupport")]
    [InlineData("rebuttal under a nested supporting statement", "rebuttal", "nestedSupport")]
    public async Task A_wrong_parent_kind_is_rejected_with_400_naming_parentId_and_nothing_is_stored(string scenario, string kind, string parent)
    {
        var (id, targets) = await CreateNestedDiscussionAsync();
        var before = await RawDiscussionAsync(id);
        object body = kind == "supportGroup"
            ? new { kind, parentId = targets[parent], groupType = "allRequired" }
            : new { kind, parentId = targets[parent], text = "Misplaced" };

        using var response = await api.PostStatementAsync(id, body);

        await AssertValidationProblemAsync(response, "parentId", scenario);
        Assert.Equal(before, await RawDiscussionAsync(id));
    }

    // ---- UC-1 E1 and E2: parentId validity for a support group (Cluster A "parentId validity") ----

    [Theory]
    [InlineData("parentId missing", "omit")]
    [InlineData("parentId null", null)]
    [InlineData("parentId empty", "")]
    [InlineData("parentId blank", "   ")]
    [InlineData("parentId not a GUID", "not-a-guid")]
    [InlineData("parentId matches no statement", "unknown")]
    [InlineData("parentId is the thesis of another discussion", "foreignThesis")]
    [InlineData("parentId is a group in another discussion", "foreignGroup")]
    [InlineData("parentId is a supporting statement in another discussion", "foreignSupport")]
    public async Task An_invalid_parentId_for_a_support_group_is_rejected_with_400_and_nothing_is_stored(string scenario, string? parent)
    {
        var (id, _) = await CreateNestedDiscussionAsync();
        var (_, foreign) = await CreateNestedDiscussionAsync();
        var before = await RawDiscussionAsync(id);
        var body = new Dictionary<string, object?> { ["kind"] = "supportGroup", ["groupType"] = "anySufficient" };
        if (parent != "omit")
        {
            body["parentId"] = parent switch
            {
                "unknown" => Guid.NewGuid().ToString(),
                "foreignThesis" => foreign["thesis"],
                "foreignGroup" => foreign["nestedGroup"],
                "foreignSupport" => foreign["nestedSupport"],
                _ => parent,
            };
        }

        using var response = await api.PostStatementAsync(id, body);

        await AssertValidationProblemAsync(response, "parentId", scenario);
        Assert.Equal(before, await RawDiscussionAsync(id));
    }

    // ---- UC-1 E4 ----

    [Fact]
    public async Task Adding_a_group_to_an_unknown_discussion_returns_404()
    {
        var (_, targets) = await CreateNestedDiscussionAsync();

        using var response = await api.PostStatementAsync(
            Guid.NewGuid().ToString(),
            new { kind = "supportGroup", parentId = targets["nestedGroup"], groupType = "allRequired" });

        await AssertNotFoundAsync(response, "unknown discussion");
    }

    // ---- Cluster A "Kind being added" (boundary) and today's groupType rule on the new parents ----

    [Theory]
    [InlineData("second thesis under a nested group", "thesis", "nestedGroup", "Text", null, "kind")]
    [InlineData("unknown kind under a nested group", "subGroup", "nestedGroup", null, "allRequired", "kind")]
    [InlineData("unknown kind under a supporting statement", "opinion", "topSupport", "Text", null, "kind")]
    [InlineData("nested group without a group type", "supportGroup", "nestedGroup", null, null, "groupType")]
    [InlineData("nested group with an unknown group type", "supportGroup", "nestedGroup", null, "someRequired", "groupType")]
    [InlineData("sub-argument group without a group type", "supportGroup", "topSupport", null, null, "groupType")]
    [InlineData("sub-argument group with an unknown group type", "supportGroup", "topSupport", null, "mostRequired", "groupType")]
    [InlineData("nested group with text", "supportGroup", "nestedGroup", "Text", "allRequired", "text")]
    public async Task Other_invalid_fields_on_nested_parents_are_rejected_with_400_naming_the_field(
        string scenario, string kind, string parent, string? text, string? groupType, string field)
    {
        var (id, targets) = await CreateNestedDiscussionAsync();
        var before = await RawDiscussionAsync(id);

        using var response = await api.PostStatementAsync(id, new { kind, parentId = targets[parent], text, groupType });

        await AssertValidationProblemAsync(response, field, scenario);
        Assert.Equal(before, await RawDiscussionAsync(id));
    }

    // ---- Cluster A "Nesting depth": 1, 2, 3, 1,000 and 1,001; "Alternation of group and support levels" ----

    [Fact]
    public async Task Groups_are_accepted_at_depth_1_2_and_3()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var parent = ThesisId(discussion);
        var chain = new List<string> { parent };

        for (var depth = 1; depth <= 3; depth++)
        {
            var groupType = depth % 2 == 0 ? "anySufficient" : "allRequired";
            var created = await api.AddStatementAsync(id, new { kind = "supportGroup", parentId = parent, name = $"Group at depth {depth}", groupType });
            AssertNewEmptyGroup(created, parent, groupType, $"depth {depth}");
            parent = Id(created);
            chain.Add(parent);
        }

        var read = await api.GetDiscussionAsync(id);
        Assert.Equal(chain, read.GetProperty("statements").EnumerateArray().Select(Id));
        for (var depth = 1; depth <= 3; depth++)
        {
            AssertListed(read, chain[depth], "supportGroup", chain[depth - 1]);
        }
    }

    [Fact]
    public async Task A_group_is_accepted_under_a_group_at_depth_1000_so_it_sits_at_depth_1001()
    {
        await AssertGroupAcceptedAtTheEndOfASeededChainAsync(levels: 1000, alternating: false);
    }

    [Fact]
    public async Task A_group_is_accepted_at_the_end_of_a_1000_level_alternating_group_and_support_chain()
    {
        await AssertGroupAcceptedAtTheEndOfASeededChainAsync(levels: 1000, alternating: true);
    }

    [Fact]
    public async Task A_group_is_accepted_at_the_end_of_a_10000_level_alternating_chain()
    {
        await AssertGroupAcceptedAtTheEndOfASeededChainAsync(levels: 10_000, alternating: true);
    }

    /// <summary>
    /// Seeds a chain of <paramref name="levels"/> statements below the thesis through the store (for speed), adds one
    /// more group under its deepest statement through the API, and checks that <c>GET</c> returns every statement.
    /// An alternating chain is group, support, group, support, ... so its deepest statement (an even level) is a
    /// supporting statement; a non-alternating chain is all groups.
    /// </summary>
    private async Task AssertGroupAcceptedAtTheEndOfASeededChainAsync(int levels, bool alternating)
    {
        var discussion = await api.CreateDiscussionAsync(title: $"Chain of {levels}");
        var id = Id(discussion);
        var discussionId = Guid.Parse(id);
        var store = factory.Services.GetRequiredService<IDiscussionStore>();
        var chain = new List<Statement>(levels);
        var parentId = Guid.Parse(ThesisId(discussion));
        for (var depth = 1; depth <= levels; depth++)
        {
            var isGroup = !alternating || depth % 2 == 1;
            var statement = isGroup
                ? new Statement(Guid.NewGuid(), discussionId, StatementKind.SupportGroup, parentId, null, SupportGroupType.AllRequired, DateTimeOffset.UtcNow)
                : new Statement(Guid.NewGuid(), discussionId, StatementKind.SupportingStatement, parentId, $"Support at depth {depth}", null, DateTimeOffset.UtcNow);
            await store.AddStatementAsync(statement);
            chain.Add(statement);
            parentId = statement.Id;
        }

        var deepest = chain[^1].Id.ToString();
        Assert.Equal(alternating ? StatementKind.SupportingStatement : StatementKind.SupportGroup, chain[^1].Kind);

        var created = await api.AddStatementAsync(id, new { kind = "supportGroup", parentId = deepest, name = "Deepest group", groupType = "anySufficient" });

        AssertNewEmptyGroup(created, deepest, "anySufficient", $"depth {levels + 1}");
        using var response = await api.Http.GetAsync($"{DiscussionsPath}/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var statements = (await ReadJsonAsync(response)).GetProperty("statements").EnumerateArray().ToList();
        Assert.Equal(levels + 2, statements.Count);
        Assert.Equal(ThesisId(discussion), Id(statements[0]));
        for (var depth = 1; depth <= levels; depth++)
        {
            Assert.Equal(chain[depth - 1].Id.ToString(), Id(statements[depth]));
            Assert.Equal(statements[depth - 1].GetProperty("id").GetString(), statements[depth].GetProperty("parentId").GetString());
        }

        Assert.Equal(Id(created), Id(statements[^1]));
        Assert.Equal(deepest, statements[^1].GetProperty("parentId").GetString());
        Assert.Equal("supportGroup", statements[^1].GetProperty("kind").GetString());
    }

    /// <summary>
    /// A discussion with groups nested under groups and under supporting statements, plus one statement of every other
    /// kind at the top level and at depth. Keys name each statement's role.
    /// <code>
    /// thesis
    ///   topGroup
    ///     topSupport
    ///       subArgumentGroup            (a group under a supporting statement)
    ///         subArgumentSupport
    ///     nestedGroup                   (a group under a group)
    ///       nestedSupport
    ///         nestedIllustration
    ///         nestedCounter
    ///           nestedRebuttal
    ///   illustration
    ///   counter
    ///     rebuttal
    /// </code>
    /// </summary>
    private async Task<(string Id, Dictionary<string, string> Targets)> CreateNestedDiscussionAsync()
    {
        var discussion = await api.CreateDiscussionAsync(title: "Nested groups", topicTag: "api.nested-groups");
        var id = Id(discussion);
        var thesis = ThesisId(discussion);
        var topGroup = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired");
        var topSupport = await api.AddAsync(id, "supportingStatement", topGroup, "Top support");
        var subArgumentGroup = await api.AddAsync(id, "supportGroup", topSupport, groupType: "anySufficient");
        var subArgumentSupport = await api.AddAsync(id, "supportingStatement", subArgumentGroup, "Sub-argument support");
        var nestedGroup = await api.AddAsync(id, "supportGroup", topGroup, groupType: "anySufficient");
        var nestedSupport = await api.AddAsync(id, "supportingStatement", nestedGroup, "Nested support");
        var nestedIllustration = await api.AddAsync(id, "illustration", nestedSupport, "Nested illustration");
        var nestedCounter = await api.AddAsync(id, "counterArgument", nestedSupport, "Nested counter");
        var nestedRebuttal = await api.AddAsync(id, "rebuttal", nestedCounter, "Nested rebuttal");
        var illustration = await api.AddAsync(id, "illustration", thesis, "Illustration");
        var counter = await api.AddAsync(id, "counterArgument", thesis, "Counter");
        var rebuttal = await api.AddAsync(id, "rebuttal", counter, "Rebuttal");
        return (id, new Dictionary<string, string>
        {
            ["thesis"] = thesis,
            ["topGroup"] = topGroup,
            ["topSupport"] = topSupport,
            ["subArgumentGroup"] = subArgumentGroup,
            ["subArgumentSupport"] = subArgumentSupport,
            ["nestedGroup"] = nestedGroup,
            ["nestedSupport"] = nestedSupport,
            ["nestedIllustration"] = nestedIllustration,
            ["nestedCounter"] = nestedCounter,
            ["nestedRebuttal"] = nestedRebuttal,
            ["illustration"] = illustration,
            ["counter"] = counter,
            ["rebuttal"] = rebuttal,
        });
    }

    private async Task<string[]> StatementIdsAsync(string id) =>
        (await api.GetDiscussionAsync(id)).GetProperty("statements").EnumerateArray().Select(Id).ToArray();

    /// <summary>The raw <c>GET</c> body, so "nothing is stored" compares the whole discussion, not just a count.</summary>
    private async Task<string> RawDiscussionAsync(string id)
    {
        using var response = await api.Http.GetAsync($"{DiscussionsPath}/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static void AssertNewEmptyGroup(JsonElement created, string parentId, string groupType, string scenario)
    {
        Assert.True(created.GetProperty("kind").GetString() == "supportGroup", $"{scenario}: {created}");
        Assert.Equal(parentId, created.GetProperty("parentId").GetString());
        Assert.Equal(groupType, created.GetProperty("groupType").GetString());
        Assert.True(Guid.TryParse(Id(created), out _));
        Assert.Equal(JsonValueKind.Null, created.GetProperty("editedAt").ValueKind);
        Assert.False(created.TryGetProperty("holds", out _), $"{scenario}: a new, empty group has no holds: {created}");
    }

    private static void AssertListed(JsonElement discussion, string id, string kind, string parentId)
    {
        var statement = Statement(discussion, id);
        Assert.Equal(kind, statement.GetProperty("kind").GetString());
        Assert.Equal(parentId, statement.GetProperty("parentId").GetString());
    }
}
