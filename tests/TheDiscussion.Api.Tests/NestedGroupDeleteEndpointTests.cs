using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Storage;
using static TheDiscussion.Api.Tests.DiscussionApiClient;

namespace TheDiscussion.Api.Tests;

/// <summary>
/// nested-named-groups AC-5 (UC-5 E1, E2 and A2; Cluster C "Status after delete" and "Tree depth"; Cluster A "holds
/// presence", boundary "group that becomes empty after its only member is deleted"):
/// <c>DELETE /api/v1/discussions/{id}/statements/{statementId}</c> on nested trees, and the status recomputed on the next
/// <c>GET</c> (in-memory provider).
/// </summary>
public class NestedGroupDeleteEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly DiscussionApiClient api = new(factory.CreateClient());

    // ---- UC-5 E1 and E2 ----

    [Fact]
    public async Task Deleting_the_thesis_of_a_nested_tree_returns_400_naming_statementId_and_deletes_nothing()
    {
        var (id, targets) = await CreateNestedDiscussionAsync();
        var before = await RawDiscussionAsync(id);

        using var response = await api.DeleteStatementAsync(id, targets["thesis"]);

        await AssertValidationProblemAsync(response, "statementId", "UC-5 E1: deleting the thesis");
        Assert.Equal(before, await RawDiscussionAsync(id));
    }

    [Fact]
    public async Task Deleting_an_unknown_statement_or_in_an_unknown_discussion_returns_404_and_deletes_nothing()
    {
        var (id, targets) = await CreateNestedDiscussionAsync();
        var before = await RawDiscussionAsync(id);

        using var unknownStatement = await api.DeleteStatementAsync(id, Guid.NewGuid().ToString());
        using var unknownDiscussion = await api.DeleteStatementAsync(Guid.NewGuid().ToString(), targets["nestedGroup"]);

        await AssertNotFoundAsync(unknownStatement, "UC-5 E2: unknown statement");
        await AssertNotFoundAsync(unknownDiscussion, "UC-5 E2: unknown discussion");
        Assert.Equal(before, await RawDiscussionAsync(id));
    }

    [Fact]
    public async Task Statements_deleted_with_a_nested_group_return_404_afterwards()
    {
        var (id, targets) = await CreateNestedDiscussionAsync();

        using var deleted = await api.DeleteStatementAsync(id, targets["topGroup"]);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        foreach (var gone in new[] { "topGroup", "subArgumentGroup", "subArgumentSupport", "nestedGroup", "nestedCounter", "nestedRebuttal" })
        {
            using var again = await api.DeleteStatementAsync(id, targets[gone]);
            await AssertNotFoundAsync(again, $"deleting {gone} again");
            using var under = await api.PostStatementAsync(id, new { kind = "supportGroup", parentId = targets[gone], name = "Orphan", groupType = "allRequired" });
            await AssertValidationProblemAsync(under, "parentId", $"adding under the deleted {gone}");
        }

        Assert.Equal(
            [targets["thesis"], targets["illustration"], targets["counter"], targets["rebuttal"]],
            (await api.GetDiscussionAsync(id)).GetProperty("statements").EnumerateArray().Select(Id));
    }

    // ---- UC-5 A2: status is recomputed after a delete ----

    [Fact]
    public async Task Deleting_statements_outside_the_support_tree_leaves_every_status_unchanged()
    {
        var (id, targets) = await CreateNestedDiscussionAsync();
        var before = await api.GetDiscussionAsync(id);
        Assert.Equal("tested", before.GetProperty("status").GetString());

        // Illustrations never affect status, at the top level or deep in a nested group.
        foreach (var illustration in new[] { "nestedIllustration", "illustration" })
        {
            using var response = await api.DeleteStatementAsync(id, targets[illustration]);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var after = await api.GetDiscussionAsync(id);
        Assert.Equal("tested", after.GetProperty("status").GetString());
        var remaining = after.GetProperty("statements").EnumerateArray().ToList();
        Assert.Equal(before.GetProperty("statements").GetArrayLength() - 2, remaining.Count);
        foreach (var statement in remaining)
        {
            // Every remaining statement is returned exactly as before: the same parent, stands, refuted and holds.
            Assert.Equal(Statement(before, Id(statement)).ToString(), statement.ToString());
        }
    }

    [Theory]
    [InlineData(true, "tested")]
    [InlineData(false, "open")]
    public async Task Deleting_a_failing_nested_sub_group_makes_its_all_required_parent_hold(bool anotherCounterRemains, string statusAfter)
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var thesis = ThesisId(discussion);
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired");
        var standing = await api.AddAsync(id, "supportingStatement", group, "Standing support");
        var failing = await api.AddAsync(id, "supportGroup", group, groupType: "allRequired");
        var countered = await api.AddAsync(id, "supportingStatement", failing, "Countered support");
        await api.AddAsync(id, "counterArgument", countered, "Unrefuted counter");
        if (anotherCounterRemains)
        {
            // A refuted counter outside the deleted subtree keeps the thesis Tested rather than Open.
            var thesisCounter = await api.AddAsync(id, "counterArgument", thesis, "Refuted counter");
            await api.AddAsync(id, "rebuttal", thesisCounter, "Rebuttal");
        }

        var before = await api.GetDiscussionAsync(id);
        Assert.Equal("countered", before.GetProperty("status").GetString());
        Assert.False(Holds(before, failing));
        Assert.False(Holds(before, group));

        using var response = await api.DeleteStatementAsync(id, failing);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await api.GetDiscussionAsync(id);
        Assert.True(Holds(after, group));
        Assert.True(Statement(after, standing).GetProperty("stands").GetBoolean());
        Assert.Equal(statusAfter, after.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Deleting_the_only_member_of_a_nested_group_leaves_it_empty_with_no_holds()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var group = await api.AddAsync(id, "supportGroup", ThesisId(discussion), groupType: "allRequired");
        var support = await api.AddAsync(id, "supportingStatement", group, "Kept support");
        var nested = await api.AddAsync(id, "supportGroup", group, groupType: "anySufficient");
        var onlyNestedMember = await api.AddAsync(id, "supportingStatement", nested, "Only member of the nested group");
        await api.AddAsync(id, "counterArgument", onlyNestedMember, "Unrefuted counter");
        var subArgument = await api.AddAsync(id, "supportGroup", support, groupType: "allRequired");
        var onlySubArgumentMember = await api.AddAsync(id, "supportingStatement", subArgument, "Only member of the sub-argument");

        var before = await api.GetDiscussionAsync(id);
        Assert.False(Holds(before, nested));
        Assert.True(Holds(before, subArgument));
        Assert.Equal("countered", before.GetProperty("status").GetString());

        foreach (var member in new[] { onlyNestedMember, onlySubArgumentMember })
        {
            using var response = await api.DeleteStatementAsync(id, member);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var after = await api.GetDiscussionAsync(id);
        Assert.False(Statement(after, nested).TryGetProperty("holds", out _), "the emptied nested group has no holds");
        Assert.False(Statement(after, subArgument).TryGetProperty("holds", out _), "the emptied sub-argument group has no holds");
        Assert.True(Statement(after, support).GetProperty("stands").GetBoolean());
        Assert.True(Holds(after, group));
        Assert.Equal("open", after.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Removing_the_last_non_empty_nested_group_makes_the_thesis_unsupported()
    {
        var discussion = await api.CreateDiscussionAsync();
        var id = Id(discussion);
        var thesis = ThesisId(discussion);
        var emptyTop = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired");
        var emptyNested = await api.AddAsync(id, "supportGroup", emptyTop, groupType: "anySufficient");
        var top = await api.AddAsync(id, "supportGroup", thesis, groupType: "anySufficient");
        var nested = await api.AddAsync(id, "supportGroup", top, groupType: "allRequired");
        var support = await api.AddAsync(id, "supportingStatement", nested, "The only support");
        await api.AddAsync(id, "supportGroup", support, groupType: "allRequired");
        await api.AddAsync(id, "illustration", thesis, "Illustration");
        Assert.Equal("open", await api.StatusAsync(id));

        using var response = await api.DeleteStatementAsync(id, nested);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await api.GetDiscussionAsync(id);
        Assert.Equal("unsupported", after.GetProperty("status").GetString());
        foreach (var group in new[] { emptyTop, emptyNested, top })
        {
            Assert.False(Statement(after, group).TryGetProperty("holds", out _), $"group {group} is empty and has no holds");
        }
    }

    // ---- Cluster C "Tree depth": delete part of a deep nested tree, then read it ----

    [Theory]
    [InlineData(1000)]
    [InlineData(10_000)]
    public async Task Get_after_deleting_part_of_a_deep_nested_tree_returns_200_with_the_remaining_statements(int levels)
    {
        var discussion = await api.CreateDiscussionAsync(title: $"Deep delete {levels}");
        var id = Id(discussion);
        var chain = await SeedAlternatingChainAsync(Guid.Parse(id), Guid.Parse(ThesisId(discussion)), levels);

        // Cut the chain in the middle, at a supporting statement: everything from there down goes.
        var cut = levels / 2;
        Assert.Equal(StatementKind.SupportingStatement, chain[cut - 1].Kind);
        using var response = await api.DeleteStatementAsync(id, chain[cut - 1].Id.ToString());
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var read = await api.Http.GetAsync($"{DiscussionsPath}/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var after = await ReadJsonAsync(read);
        var statements = after.GetProperty("statements").EnumerateArray().ToList();
        Assert.Equal(cut, statements.Count);
        Assert.Equal(ThesisId(discussion), Id(statements[0]));
        for (var depth = 1; depth < cut; depth++)
        {
            Assert.Equal(chain[depth - 1].Id.ToString(), Id(statements[depth]));
            Assert.Equal(Id(statements[depth - 1]), statements[depth].GetProperty("parentId").GetString());
        }

        // The deepest remaining group is now empty, so it has no holds; every group above it holds through the chain.
        Assert.False(statements[^1].TryGetProperty("holds", out _));
        Assert.True(statements[1].GetProperty("holds").GetBoolean());
        Assert.Equal("open", after.GetProperty("status").GetString());
    }

    /// <summary>
    /// Seeds a chain of <paramref name="levels"/> statements below the thesis through the store (for speed):
    /// group, support, group, support, ... each the parent of the next.
    /// </summary>
    private async Task<List<Statement>> SeedAlternatingChainAsync(Guid discussionId, Guid thesisId, int levels)
    {
        var store = factory.Services.GetRequiredService<IDiscussionStore>();
        var chain = new List<Statement>(levels);
        var parentId = thesisId;
        for (var depth = 1; depth <= levels; depth++)
        {
            var statement = depth % 2 == 1
                ? new Statement(Guid.NewGuid(), discussionId, StatementKind.SupportGroup, parentId, null, SupportGroupType.AllRequired, DateTimeOffset.UtcNow) { Name = $"Group at depth {depth}" }
                : new Statement(Guid.NewGuid(), discussionId, StatementKind.SupportingStatement, parentId, $"Support at depth {depth}", null, DateTimeOffset.UtcNow);
            await store.AddStatementAsync(statement);
            chain.Add(statement);
            parentId = statement.Id;
        }

        return chain;
    }

    /// <summary>
    /// A nested discussion whose thesis is Tested: everything stands and holds, and the only counters are refuted.
    /// <code>
    /// thesis
    ///   topGroup                  allRequired
    ///     topSupport
    ///       subArgumentGroup      anySufficient (a group under a supporting statement)
    ///         subArgumentSupport
    ///     nestedGroup             anySufficient (a group under a group)
    ///       nestedSupport
    ///         nestedIllustration
    ///         nestedCounter       refuted
    ///           nestedRebuttal
    ///   illustration
    ///   counter                   refuted
    ///     rebuttal
    /// </code>
    /// </summary>
    private async Task<(string Id, Dictionary<string, string> Targets)> CreateNestedDiscussionAsync()
    {
        var discussion = await api.CreateDiscussionAsync(title: "Nested delete", topicTag: "api.nested-delete");
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

    /// <summary>The raw <c>GET</c> body, so "nothing changed" compares the whole discussion.</summary>
    private async Task<string> RawDiscussionAsync(string id)
    {
        using var response = await api.Http.GetAsync($"{DiscussionsPath}/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static bool Holds(JsonElement discussion, string groupId) =>
        Statement(discussion, groupId).GetProperty("holds").GetBoolean();
}
