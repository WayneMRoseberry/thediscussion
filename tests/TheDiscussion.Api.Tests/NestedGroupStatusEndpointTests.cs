using System.Net;
using System.Text.Json;
using static TheDiscussion.Api.Tests.DiscussionApiClient;

namespace TheDiscussion.Api.Tests;

/// <summary>
/// nested-named-groups AC-2 (UC-2, Cluster A "holds presence" and "Thesis status"): status over nested groups and
/// sub-arguments as returned by <c>GET /api/v1/discussions/{id}</c> (in-memory provider).
/// </summary>
public class NestedGroupStatusEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly DiscussionApiClient api = new(factory.CreateClient());

    [Fact]
    public async Task Holds_stands_and_refuted_are_returned_at_every_depth()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var thesis = ThesisId(discussion);

        // depth 1 (anySufficient) > depth 2 (allRequired) > depth 3 (allRequired): holds true at every level.
        var depth1 = await api.AddAsync(id, "supportGroup", thesis, groupType: "anySufficient");
        var depth2 = await api.AddAsync(id, "supportGroup", depth1, groupType: "allRequired");
        var depth3 = await api.AddAsync(id, "supportGroup", depth2, groupType: "allRequired");
        var deepSupport = await api.AddAsync(id, "supportingStatement", depth3, "Deep support");
        var deepCounter = await api.AddAsync(id, "counterArgument", deepSupport, "Deep counter");
        var deepRebuttal = await api.AddAsync(id, "rebuttal", deepCounter, "Deep rebuttal");

        // A sub-argument under the deep support that holds, and an empty sub-argument next to it.
        var subArgument = await api.AddAsync(id, "supportGroup", deepSupport, groupType: "anySufficient");
        var subSupport = await api.AddAsync(id, "supportingStatement", subArgument, "Sub-argument support");
        var emptySubArgument = await api.AddAsync(id, "supportGroup", deepSupport, groupType: "allRequired");

        // An empty nested group at depth 3 and a failing nested group at depth 2 (rescued by depth 1 being anySufficient).
        var emptyNested = await api.AddAsync(id, "supportGroup", depth2, groupType: "anySufficient");
        var failingDepth2 = await api.AddAsync(id, "supportGroup", depth1, groupType: "allRequired");
        var failingSupport = await api.AddAsync(id, "supportingStatement", failingDepth2, "Countered support");
        var failingCounter = await api.AddAsync(id, "counterArgument", failingSupport, "Unrefuted counter");

        var read = await api.GetDiscussionAsync(id);

        Assert.True(Holds(read, depth1));
        Assert.True(Holds(read, depth2));
        Assert.True(Holds(read, depth3));
        Assert.True(Holds(read, subArgument));
        Assert.False(Holds(read, failingDepth2));
        Assert.False(Statement(read, emptyNested).TryGetProperty("holds", out _));
        Assert.False(Statement(read, emptySubArgument).TryGetProperty("holds", out _));

        Assert.True(Statement(read, deepSupport).GetProperty("stands").GetBoolean());
        Assert.True(Statement(read, subSupport).GetProperty("stands").GetBoolean());
        Assert.False(Statement(read, failingSupport).GetProperty("stands").GetBoolean());
        Assert.True(Statement(read, deepCounter).GetProperty("refuted").GetBoolean());
        Assert.False(Statement(read, deepRebuttal).GetProperty("refuted").GetBoolean());
        Assert.False(Statement(read, failingCounter).GetProperty("refuted").GetBoolean());

        // Only non-empty groups carry holds; statuses never leak onto the wrong kinds.
        foreach (var statement in read.GetProperty("statements").EnumerateArray())
        {
            var kind = statement.GetProperty("kind").GetString();
            Assert.Equal(kind == "supportingStatement", statement.TryGetProperty("stands", out _));
            Assert.Equal(kind is "counterArgument" or "rebuttal", statement.TryGetProperty("refuted", out _));
            if (kind != "supportGroup")
            {
                Assert.False(statement.TryGetProperty("holds", out _));
            }
        }

        Assert.Equal("tested", read.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_sub_argument_that_does_not_hold_knocks_down_its_supporting_statement()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var group = await api.AddAsync(id, "supportGroup", ThesisId(discussion), groupType: "allRequired");
        var support = await api.AddAsync(id, "supportingStatement", group, "Support without a counter of its own");
        var subArgument = await api.AddAsync(id, "supportGroup", support, groupType: "allRequired");
        var inner = await api.AddAsync(id, "supportingStatement", subArgument, "Inner support");
        await api.AddAsync(id, "counterArgument", inner, "Unrefuted counter");

        var read = await api.GetDiscussionAsync(id);

        Assert.False(Holds(read, subArgument));
        Assert.False(Statement(read, support).GetProperty("stands").GetBoolean());
        Assert.False(Holds(read, group));
        Assert.Equal("countered", read.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Status_of_a_nested_tree_progresses_as_statements_are_added()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var group = await api.AddAsync(id, "supportGroup", ThesisId(discussion), groupType: "allRequired");
        var subGroup = await api.AddAsync(id, "supportGroup", group, groupType: "allRequired");
        var unsupported = await api.GetDiscussionAsync(id);
        Assert.Equal("unsupported", unsupported.GetProperty("status").GetString());
        Assert.False(Statement(unsupported, group).TryGetProperty("holds", out _));
        Assert.False(Statement(unsupported, subGroup).TryGetProperty("holds", out _));

        var support = await api.AddAsync(id, "supportingStatement", subGroup, "Fewer interruptions");
        var open = await api.GetDiscussionAsync(id);
        Assert.Equal("open", open.GetProperty("status").GetString());
        Assert.True(Holds(open, subGroup));
        Assert.True(Holds(open, group));

        var counter = await api.AddAsync(id, "counterArgument", support, "Slack pings replace them");
        var countered = await api.GetDiscussionAsync(id);
        Assert.Equal("countered", countered.GetProperty("status").GetString());
        Assert.False(Statement(countered, support).GetProperty("stands").GetBoolean());
        Assert.False(Holds(countered, subGroup));
        Assert.False(Holds(countered, group));

        await api.AddAsync(id, "rebuttal", counter, "Notifications can be paused");
        var tested = await api.GetDiscussionAsync(id);
        Assert.Equal("tested", tested.GetProperty("status").GetString());
        Assert.True(Statement(tested, counter).GetProperty("refuted").GetBoolean());
        Assert.True(Statement(tested, support).GetProperty("stands").GetBoolean());
        Assert.True(Holds(tested, subGroup));
        Assert.True(Holds(tested, group));
    }

    [Fact]
    public async Task Reading_an_unknown_discussion_still_returns_404()
    {
        using var response = await api.Http.GetAsync($"{DiscussionsPath}/{Guid.NewGuid()}");

        await AssertNotFoundAsync(response, "UC-2 E1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static bool Holds(JsonElement discussion, string groupId) =>
        Statement(discussion, groupId).GetProperty("holds").GetBoolean();
}
