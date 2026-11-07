using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Storage;
using static TheDiscussion.Api.Tests.DiscussionApiClient;

namespace TheDiscussion.Api.Tests;

/// <summary>
/// nested-named-groups AC-3 (UC-3, Cluster B): every new support group needs a name of 1 to 100 characters after
/// trimming, and <c>PATCH .../statements/{statementId}</c> on a group changes its <c>name</c> and/or <c>groupType</c>
/// (in-memory provider). Every rejected request also leaves the next <c>GET</c> byte-for-byte identical, which covers
/// <c>editedAt</c> keeping its earlier value.
/// </summary>
public class GroupNameEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>"😀" is one emoji made of a surrogate pair: 2 UTF-16 code units (Q-4).</summary>
    private const string Emoji = "\U0001F600";

    private readonly DiscussionApiClient api = new(factory.CreateClient());

    // ---- Create: Cluster B "name length after trimming" (UC-3 E1) ----

    [Theory]
    [InlineData("name missing", "omit")]
    [InlineData("name null", null)]
    [InlineData("name empty", "")]
    [InlineData("name one space", " ")]
    [InlineData("name whitespace only", " \t\n ")]
    [InlineData("name of 101 characters", "LONG")]
    [InlineData("name of 101 characters after trimming", "LONG_PADDED")]
    public async Task A_group_without_a_valid_name_is_rejected_with_400_naming_name_and_nothing_is_stored(string scenario, string? name)
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var before = await RawDiscussionAsync(id);
        var body = new Dictionary<string, object?> { ["kind"] = "supportGroup", ["parentId"] = thesis, ["groupType"] = "allRequired" };
        if (name != "omit")
        {
            body["name"] = name switch
            {
                "LONG" => new string('n', 101),
                "LONG_PADDED" => $"  {new string('n', 101)}  ",
                _ => name,
            };
        }

        using var response = await api.PostStatementAsync(id, body);

        await AssertValidationProblemAsync(response, "name", scenario);
        Assert.Equal(before, await RawDiscussionAsync(id));
    }

    // ---- Create: accepted names (UC-3 main flow a, A1) ----

    [Theory]
    [InlineData("1 character", "a", "a")]
    [InlineData("1 character with surrounding spaces (A1)", "  a  ", "a")]
    [InlineData("2 characters", "ab", "ab")]
    [InlineData("99 characters", "N99", "N99")]
    [InlineData("100 characters", "N100", "N100")]
    [InlineData("100 characters with surrounding spaces, stored trimmed", "  N100  ", "N100")]
    [InlineData("inner spaces kept", "  Productivity  mechanisms ", "Productivity  mechanisms")]
    public async Task A_group_with_a_valid_name_is_created_with_the_trimmed_name(string scenario, string name, string expected)
    {
        var (id, thesis) = await CreateDiscussionAsync();
        name = name.Replace("N100", new string('n', 100)).Replace("N99", new string('n', 99));
        expected = expected.Replace("N100", new string('n', 100)).Replace("N99", new string('n', 99));

        var created = await api.AddStatementAsync(id, new { kind = "supportGroup", parentId = thesis, name, groupType = "anySufficient" });

        Assert.True(created.GetProperty("name").GetString() == expected, $"{scenario}: {created}");
        Assert.Equal("anySufficient", created.GetProperty("groupType").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("editedAt").ValueKind);
        Assert.False(created.TryGetProperty("text", out _));
        Assert.Equal(expected, Statement(await api.GetDiscussionAsync(id), Id(created)).GetProperty("name").GetString());
    }

    // ---- Create: a name is required on every allowed parent (combines with AC-1) ----

    [Theory]
    [InlineData("under the thesis", "thesis")]
    [InlineData("under a group", "group")]
    [InlineData("under a supporting statement (sub-argument)", "support")]
    public async Task A_name_is_required_under_every_allowed_parent(string scenario, string parent)
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired");
        var support = await api.AddAsync(id, "supportingStatement", group, "Support");
        var parentId = parent switch { "group" => group, "support" => support, _ => thesis };
        var before = await RawDiscussionAsync(id);

        using var unnamed = await api.PostStatementAsync(id, new { kind = "supportGroup", parentId, groupType = "allRequired" });
        await AssertValidationProblemAsync(unnamed, "name", scenario);
        Assert.Equal(before, await RawDiscussionAsync(id));

        var named = await api.AddStatementAsync(id, new { kind = "supportGroup", parentId, name = $"Group {parent}", groupType = "allRequired" });
        Assert.Equal(parentId, named.GetProperty("parentId").GetString());
        Assert.Equal($"Group {parent}", Statement(await api.GetDiscussionAsync(id), Id(named)).GetProperty("name").GetString());
    }

    // ---- Create: Cluster B "name content" ----

    [Theory]
    [InlineData("combining marks", "Café résumé")]
    [InlineData("letters from several scripts", "Ölçüm 証拠 доказательство دليل")]
    [InlineData("emoji", "Evidence \U0001F600\U0001F44D")]
    public async Task Unicode_names_round_trip_unchanged(string scenario, string name)
    {
        var (id, thesis) = await CreateDiscussionAsync();

        var created = await api.AddStatementAsync(id, new { kind = "supportGroup", parentId = thesis, name, groupType = "allRequired" });

        Assert.True(created.GetProperty("name").GetString() == name, scenario);
        Assert.Equal(name, Statement(await api.GetDiscussionAsync(id), Id(created)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Emoji_at_the_boundary_count_as_2_so_100_code_units_are_accepted_and_101_rejected()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var hundredCodeUnits = string.Concat(Enumerable.Repeat(Emoji, 50));
        Assert.Equal(100, hundredCodeUnits.Length);

        var created = await api.AddStatementAsync(id, new { kind = "supportGroup", parentId = thesis, name = hundredCodeUnits, groupType = "allRequired" });
        Assert.Equal(hundredCodeUnits, created.GetProperty("name").GetString());

        var before = await RawDiscussionAsync(id);
        foreach (var tooLong in new[] { hundredCodeUnits + "a", "a" + hundredCodeUnits, string.Concat(Enumerable.Repeat(Emoji, 51)) })
        {
            using var response = await api.PostStatementAsync(id, new { kind = "supportGroup", parentId = thesis, name = tooLong, groupType = "allRequired" });
            await AssertValidationProblemAsync(response, "name", $"{tooLong.Length} code units");
        }

        Assert.Equal(before, await RawDiscussionAsync(id));
    }

    // ---- Create: Cluster B "groupType value" (UC-3 E6) ----

    [Theory]
    [InlineData("groupType unknown", "someRequired")]
    [InlineData("groupType null", null)]
    [InlineData("groupType missing", "omit")]
    [InlineData("groupType empty", "")]
    public async Task A_named_group_with_an_invalid_group_type_is_rejected_with_400_naming_groupType(string scenario, string? groupType)
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var before = await RawDiscussionAsync(id);
        var body = new Dictionary<string, object?> { ["kind"] = "supportGroup", ["parentId"] = thesis, ["name"] = "Named" };
        if (groupType != "omit")
        {
            body["groupType"] = groupType;
        }

        using var response = await api.PostStatementAsync(id, body);

        var problem = await AssertValidationProblemAsync(response, "groupType", scenario);
        Assert.False(problem.GetProperty("errors").TryGetProperty("name", out _), $"{scenario}: the name is valid");
        Assert.Equal(before, await RawDiscussionAsync(id));
    }

    [Fact]
    public async Task Group_type_names_stay_case_insensitive_on_create()
    {
        var (id, thesis) = await CreateDiscussionAsync();

        var created = await api.AddStatementAsync(id, new { kind = "supportGroup", parentId = thesis, name = "Named", groupType = "AllRequired" });

        Assert.Equal("allRequired", created.GetProperty("groupType").GetString());
    }

    // ---- Create: name on other kinds (this task's choice: rejected like the PATCH rule) and the response shape ----

    [Theory]
    [InlineData("supportingStatement", "group")]
    [InlineData("counterArgument", "thesis")]
    [InlineData("rebuttal", "counter")]
    [InlineData("illustration", "thesis")]
    public async Task A_name_on_a_new_statement_of_another_kind_is_rejected_with_400_naming_name(string kind, string parent)
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired");
        var counter = await api.AddAsync(id, "counterArgument", thesis, "Counter");
        var parentId = parent switch { "group" => group, "counter" => counter, _ => thesis };
        var before = await RawDiscussionAsync(id);

        using var response = await api.PostStatementAsync(id, new { kind, parentId, text = "Text", name = "Not for this kind" });

        await AssertValidationProblemAsync(response, "name", kind);
        Assert.Equal(before, await RawDiscussionAsync(id));
    }

    [Fact]
    public async Task Only_groups_carry_a_name_in_responses_and_an_unnamed_group_has_name_null()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var named = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired", name: "Named");
        await api.AddAsync(id, "supportingStatement", named, "Support");
        await api.AddAsync(id, "counterArgument", thesis, "Counter");
        await api.AddAsync(id, "illustration", thesis, "Illustration");
        var unnamed = await SeedUnnamedGroupAsync(id, thesis, SupportGroupType.AnySufficient);

        var read = await api.GetDiscussionAsync(id);

        foreach (var statement in read.GetProperty("statements").EnumerateArray())
        {
            var isGroup = statement.GetProperty("kind").GetString() == "supportGroup";
            Assert.Equal(isGroup, statement.TryGetProperty("name", out _));
        }

        Assert.Equal("Named", Statement(read, named).GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, Statement(read, unnamed).GetProperty("name").ValueKind);
        Assert.Equal("anySufficient", Statement(read, unnamed).GetProperty("groupType").GetString());
    }

    // ---- PATCH a group: UC-3 main flow (b), A2, A3, postconditions ----

    [Fact]
    public async Task Patching_a_groups_name_sets_the_trimmed_name_and_editedAt_and_changes_nothing_else()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired", name: "Old name");
        var support = await api.AddAsync(id, "supportingStatement", group, "Support");
        var subGroup = await api.AddAsync(id, "supportGroup", group, groupType: "anySufficient", name: "Sub-group");
        var before = await api.GetDiscussionAsync(id);
        Assert.Equal(JsonValueKind.Null, Statement(before, group).GetProperty("editedAt").ValueKind);

        using var response = await api.PatchStatementAsync(id, group, new { name = "  New name  " });

        var edited = await ReadOkAsync(response);
        Assert.Equal("New name", edited.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.String, edited.GetProperty("editedAt").ValueKind);
        var after = await api.GetDiscussionAsync(id);
        var stored = Statement(after, group);
        var original = Statement(before, group);
        Assert.Equal("New name", stored.GetProperty("name").GetString());
        Assert.Equal(edited.GetProperty("editedAt").GetString(), stored.GetProperty("editedAt").GetString());
        foreach (var unchanged in new[] { "kind", "parentId", "createdAt", "groupType" })
        {
            Assert.Equal(original.GetProperty(unchanged).GetString(), stored.GetProperty(unchanged).GetString());
        }

        // The statement list (and so every child of the group) is unchanged apart from the edited group.
        Assert.Equal(
            before.GetProperty("statements").EnumerateArray().Select(s => Id(s) == group ? null : s.GetRawText()),
            after.GetProperty("statements").EnumerateArray().Select(s => Id(s) == group ? null : s.GetRawText()));
        Assert.Equal(group, Statement(after, support).GetProperty("parentId").GetString());
        Assert.Equal(group, Statement(after, subGroup).GetProperty("parentId").GetString());
    }

    [Fact]
    public async Task Patching_a_named_groups_type_sets_editedAt()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired", name: "Named");

        using var response = await api.PatchStatementAsync(id, group, new { groupType = "anySufficient" });

        var edited = await ReadOkAsync(response);
        Assert.Equal("anySufficient", edited.GetProperty("groupType").GetString());
        Assert.Equal("Named", edited.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.String, edited.GetProperty("editedAt").ValueKind);
        Assert.Equal("anySufficient", Statement(await api.GetDiscussionAsync(id), group).GetProperty("groupType").GetString());
    }

    [Fact]
    public async Task Patching_name_and_type_together_changes_both_in_one_edit()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "anySufficient", name: "Before");

        using var response = await api.PatchStatementAsync(id, group, new { name = "After", groupType = "allRequired" });

        var edited = await ReadOkAsync(response);
        var stored = Statement(await api.GetDiscussionAsync(id), group);
        Assert.Equal("After", stored.GetProperty("name").GetString());
        Assert.Equal("allRequired", stored.GetProperty("groupType").GetString());
        Assert.Equal(edited.GetProperty("editedAt").GetString(), stored.GetProperty("editedAt").GetString());
    }

    [Fact]
    public async Task Patching_a_name_onto_a_migrated_unnamed_group_names_it()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var unnamed = await SeedUnnamedGroupAsync(id, thesis, SupportGroupType.AllRequired);

        using var response = await api.PatchStatementAsync(id, unnamed, new { name = "Named at last" });

        var edited = await ReadOkAsync(response);
        Assert.Equal("Named at last", edited.GetProperty("name").GetString());
        var stored = Statement(await api.GetDiscussionAsync(id), unnamed);
        Assert.Equal("Named at last", stored.GetProperty("name").GetString());
        Assert.Equal("allRequired", stored.GetProperty("groupType").GetString());
        Assert.Equal(JsonValueKind.String, stored.GetProperty("editedAt").ValueKind);
    }

    [Fact]
    public async Task Patching_only_the_type_of_an_unnamed_group_is_rejected_with_400_naming_name()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var unnamed = await SeedUnnamedGroupAsync(id, thesis, SupportGroupType.AllRequired);
        var before = await RawDiscussionAsync(id);

        using var response = await api.PatchStatementAsync(id, unnamed, new { groupType = "anySufficient" });

        var problem = await AssertValidationProblemAsync(response, "name", "groupType only on an unnamed group (E3, Q-1)");
        Assert.Equal(["name"], ErrorFields(problem));
        Assert.Equal(before, await RawDiscussionAsync(id));

        // Sending a name along with the type is accepted.
        using var withName = await api.PatchStatementAsync(id, unnamed, new { name = "Named", groupType = "anySufficient" });
        Assert.Equal("anySufficient", (await ReadOkAsync(withName)).GetProperty("groupType").GetString());
    }

    // ---- PATCH a group: Cluster B "name length after trimming" on edit (UC-3 E2) ----

    [Theory]
    [InlineData("name null", null)]
    [InlineData("name empty", "")]
    [InlineData("name whitespace only", "   ")]
    [InlineData("name of 101 characters after trimming", "LONG")]
    public async Task Patching_an_invalid_name_is_rejected_with_400_naming_name_and_nothing_changes(string scenario, string? name)
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired", name: "Named");
        var before = await RawDiscussionAsync(id);

        using var response = await api.PatchStatementAsync(id, group, new { name = name == "LONG" ? $" {new string('n', 101)} " : name });

        var problem = await AssertValidationProblemAsync(response, "name", scenario);
        Assert.Equal(["name"], ErrorFields(problem));
        Assert.Equal(before, await RawDiscussionAsync(id));
    }

    [Theory]
    [InlineData("1 character", "a", "a")]
    [InlineData("100 characters with surrounding spaces", "  N100  ", "N100")]
    public async Task Patching_a_name_at_the_length_limits_is_accepted(string scenario, string name, string expected)
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired", name: "Named");
        name = name.Replace("N100", new string('n', 100));
        expected = expected.Replace("N100", new string('n', 100));

        using var response = await api.PatchStatementAsync(id, group, new { name });

        Assert.True((await ReadOkAsync(response)).GetProperty("name").GetString() == expected, scenario);
        Assert.Equal(expected, Statement(await api.GetDiscussionAsync(id), group).GetProperty("name").GetString());
    }

    // ---- PATCH a group: Cluster B "PATCH field combination" errors (UC-3 E4, E6, E7, E8) ----

    public static TheoryData<string, string, string> RejectedGroupPatches => new()
    {
        { "text (E4)", """{ "text": "Groups have no text" }""", "text" },
        { "name and text (E4)", """{ "name": "Fine", "text": "Groups have no text" }""", "text" },
        { "groupType unknown (E6)", """{ "groupType": "bogus" }""", "groupType" },
        { "groupType null (E6)", """{ "groupType": null }""", "groupType" },
        { "groupType empty (E6)", """{ "groupType": "" }""", "groupType" },
        { "valid name with an unknown groupType (E6)", """{ "name": "Fine", "groupType": "bogus" }""", "groupType" },
        { "groupType of the wrong JSON type (E6)", """{ "groupType": 1 }""", "groupType" },
        { "name of the wrong JSON type (E2)", """{ "name": 42 }""", "name" },
        { "kind (E7)", """{ "kind": "supportingStatement" }""", "kind" },
        { "kind with a valid name (E7)", """{ "kind": "supportGroup", "name": "Fine" }""", "kind" },
        { "parentId (E7)", """{ "parentId": "00000000-0000-0000-0000-000000000000" }""", "parentId" },
        { "parentId with a valid groupType (E7)", """{ "parentId": null, "groupType": "anySufficient" }""", "parentId" },
        { "empty body (E8, Q-2)", "{ }", "body" },
        { "only unknown fields (E8, Q-2)", """{ "title": "Not a statement field" }""", "body" },
    };

    [Theory]
    [MemberData(nameof(RejectedGroupPatches))]
    public async Task Invalid_group_patches_are_rejected_with_400_naming_the_field_and_nothing_changes(string scenario, string json, string field)
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired", name: "Named");
        var before = await RawDiscussionAsync(id);

        using var response = await PatchRawAsync(id, group, json);

        var problem = await AssertValidationProblemAsync(response, field, scenario);
        Assert.True(ErrorFields(problem).SequenceEqual([field]), $"{scenario}: {problem}");
        Assert.Equal(before, await RawDiscussionAsync(id));
    }

    [Fact]
    public async Task Group_type_names_stay_case_insensitive_on_patch()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired", name: "Named");

        using var response = await api.PatchStatementAsync(id, group, new { groupType = "AnySufficient" });

        Assert.Equal("anySufficient", (await ReadOkAsync(response)).GetProperty("groupType").GetString());
    }

    // ---- PATCH a non-group: Cluster B "PATCH target kind" (UC-3 E5) ----

    [Theory]
    [InlineData("thesis", "name")]
    [InlineData("thesis", "groupType")]
    [InlineData("support", "name")]
    [InlineData("support", "groupType")]
    [InlineData("counter", "name")]
    [InlineData("counter", "groupType")]
    [InlineData("rebuttal", "name")]
    [InlineData("rebuttal", "groupType")]
    [InlineData("illustration", "name")]
    [InlineData("illustration", "groupType")]
    public async Task Name_or_groupType_sent_to_a_statement_that_is_not_a_group_is_rejected_and_text_edits_still_work(string target, string field)
    {
        var (id, targets) = await CreateDiscussionWithEveryKindAsync();
        var before = await RawDiscussionAsync(id);
        object body = field == "name" ? new { name = "A name" } : new { groupType = "allRequired" };

        using var rejected = await api.PatchStatementAsync(id, targets[target], body);
        var problem = await AssertValidationProblemAsync(rejected, field, $"{field} on {target}");
        Assert.Equal([field], ErrorFields(problem));

        using var withText = await api.PatchStatementAsync(
            id, targets[target], field == "name" ? new { text = "Text", name = "A name" } : new { text = "Text", groupType = "allRequired" });
        await AssertValidationProblemAsync(withText, field, $"{field} with text on {target}");
        Assert.Equal(before, await RawDiscussionAsync(id));

        using var textEdit = await api.PatchStatementAsync(id, targets[target], new { text = $"  Edited {target}  " });
        var edited = await ReadOkAsync(textEdit);
        Assert.Equal($"Edited {target}", edited.GetProperty("text").GetString());
        Assert.False(edited.TryGetProperty("name", out _));
        Assert.False(edited.TryGetProperty("groupType", out _));
    }

    // ---- UC-3 E9 ----

    [Fact]
    public async Task Patching_a_group_of_an_unknown_discussion_or_an_unknown_statement_returns_404()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired", name: "Named");
        var (otherId, _) = await CreateDiscussionAsync();

        using var unknownDiscussion = await api.PatchStatementAsync(Guid.NewGuid().ToString(), group, new { name = "New" });
        await AssertNotFoundAsync(unknownDiscussion, "unknown discussion");

        using var unknownStatement = await api.PatchStatementAsync(id, Guid.NewGuid().ToString(), new { name = "New" });
        await AssertNotFoundAsync(unknownStatement, "unknown statement");

        using var otherDiscussion = await api.PatchStatementAsync(otherId, group, new { groupType = "anySufficient" });
        await AssertNotFoundAsync(otherDiscussion, "group of another discussion");

        // Not found is reported before any field error.
        using var invalidAndUnknown = await api.PatchStatementAsync(id, Guid.NewGuid().ToString(), new { kind = "rebuttal", name = "" });
        await AssertNotFoundAsync(invalidAndUnknown, "unknown statement with invalid fields");
    }

    // ---- Cluster B "editedAt before the edit" and UC-3 A4 (Q-3) ----

    [Fact]
    public async Task EditedAt_is_null_until_the_first_edit_and_a_second_edit_sets_it_again()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired", name: "First");
        Assert.Equal(JsonValueKind.Null, Statement(await api.GetDiscussionAsync(id), group).GetProperty("editedAt").ValueKind);

        using var first = await api.PatchStatementAsync(id, group, new { name = "Second" });
        var firstEditedAt = (await ReadOkAsync(first)).GetProperty("editedAt").GetDateTimeOffset();

        using var second = await api.PatchStatementAsync(id, group, new { groupType = "anySufficient" });
        var secondEditedAt = (await ReadOkAsync(second)).GetProperty("editedAt").GetDateTimeOffset();

        Assert.True(secondEditedAt >= firstEditedAt, $"{secondEditedAt:O} should not be before {firstEditedAt:O}");
        Assert.Equal(secondEditedAt, Statement(await api.GetDiscussionAsync(id), group).GetProperty("editedAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("the same name", """{ "name": "Same" }""")]
    [InlineData("the same name with surrounding spaces", """{ "name": "  Same  " }""")]
    [InlineData("the same type", """{ "groupType": "allRequired" }""")]
    [InlineData("the same name and type", """{ "name": "Same", "groupType": "allRequired" }""")]
    public async Task Setting_the_value_a_group_already_has_still_counts_as_an_edit(string scenario, string json)
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired", name: "Same");

        using var response = await PatchRawAsync(id, group, json);

        var edited = await ReadOkAsync(response);
        Assert.True(edited.GetProperty("editedAt").ValueKind == JsonValueKind.String, $"{scenario}: {edited}");
        Assert.Equal("Same", edited.GetProperty("name").GetString());
        Assert.Equal("allRequired", edited.GetProperty("groupType").GetString());
    }

    // ---- Cluster B "Effect of a type change on status" (AC-2 rules, recomputed on the next read) ----

    [Fact]
    public async Task Switching_a_nested_groups_type_flips_its_holds_its_ancestors_and_the_thesis_status_and_back()
    {
        // thesis > top (allRequired) > nested (allRequired) > [standing support, countered support]
        var (id, thesis) = await CreateDiscussionAsync();
        var top = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired", name: "Top");
        var nested = await api.AddAsync(id, "supportGroup", top, groupType: "allRequired", name: "Nested");
        var standing = await api.AddAsync(id, "supportingStatement", nested, "Stands");
        var countered = await api.AddAsync(id, "supportingStatement", nested, "Countered");
        await api.AddAsync(id, "counterArgument", countered, "Unrefuted counter");

        var before = await api.GetDiscussionAsync(id);
        Assert.True(Statement(before, standing).GetProperty("stands").GetBoolean());
        Assert.False(Statement(before, countered).GetProperty("stands").GetBoolean());
        Assert.False(Statement(before, nested).GetProperty("holds").GetBoolean());
        Assert.False(Statement(before, top).GetProperty("holds").GetBoolean());
        Assert.Equal("countered", before.GetProperty("status").GetString());

        using var toAny = await api.PatchStatementAsync(id, nested, new { groupType = "anySufficient" });
        await ReadOkAsync(toAny);
        var afterAny = await api.GetDiscussionAsync(id);
        Assert.True(Statement(afterAny, nested).GetProperty("holds").GetBoolean());
        Assert.True(Statement(afterAny, top).GetProperty("holds").GetBoolean());
        Assert.Equal("tested", afterAny.GetProperty("status").GetString());

        using var backToAll = await api.PatchStatementAsync(id, nested, new { groupType = "allRequired" });
        await ReadOkAsync(backToAll);
        var afterAll = await api.GetDiscussionAsync(id);
        Assert.False(Statement(afterAll, nested).GetProperty("holds").GetBoolean());
        Assert.False(Statement(afterAll, top).GetProperty("holds").GetBoolean());
        Assert.Equal("countered", afterAll.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Switching_a_top_level_groups_type_flips_its_holds_and_the_thesis_status()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired", name: "Group");
        await api.AddAsync(id, "supportingStatement", group, "Stands");
        var countered = await api.AddAsync(id, "supportingStatement", group, "Countered");
        await api.AddAsync(id, "counterArgument", countered, "Unrefuted counter");
        Assert.Equal("countered", await api.StatusAsync(id));

        using var toAny = await api.PatchStatementAsync(id, group, new { groupType = "anySufficient" });
        await ReadOkAsync(toAny);
        var read = await api.GetDiscussionAsync(id);
        Assert.True(Statement(read, group).GetProperty("holds").GetBoolean());
        Assert.Equal("tested", read.GetProperty("status").GetString());

        using var backToAll = await api.PatchStatementAsync(id, group, new { groupType = "allRequired" });
        await ReadOkAsync(backToAll);
        Assert.Equal("countered", await api.StatusAsync(id));
    }

    [Fact]
    public async Task Switching_an_empty_groups_type_leaves_it_without_holds()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var empty = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired", name: "Empty");
        Assert.False(Statement(await api.GetDiscussionAsync(id), empty).TryGetProperty("holds", out _));
        Assert.Equal("unsupported", await api.StatusAsync(id));

        using var response = await api.PatchStatementAsync(id, empty, new { groupType = "anySufficient" });

        Assert.False((await ReadOkAsync(response)).TryGetProperty("holds", out _));
        var read = await api.GetDiscussionAsync(id);
        Assert.False(Statement(read, empty).TryGetProperty("holds", out _));
        Assert.Equal("anySufficient", Statement(read, empty).GetProperty("groupType").GetString());
        Assert.Equal("unsupported", read.GetProperty("status").GetString());
    }

    private async Task<(string Id, string ThesisId)> CreateDiscussionAsync()
    {
        var discussion = await api.CreateDiscussionAsync();
        return (Id(discussion), ThesisId(discussion));
    }

    /// <summary>A discussion with one statement of every kind that is not a group, keyed by role.</summary>
    private async Task<(string Id, Dictionary<string, string> Targets)> CreateDiscussionWithEveryKindAsync()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await api.AddAsync(id, "supportGroup", thesis, groupType: "allRequired");
        var support = await api.AddAsync(id, "supportingStatement", group, "Support");
        var counter = await api.AddAsync(id, "counterArgument", thesis, "Counter");
        var rebuttal = await api.AddAsync(id, "rebuttal", counter, "Rebuttal");
        var illustration = await api.AddAsync(id, "illustration", thesis, "Illustration");
        return (id, new Dictionary<string, string>
        {
            ["thesis"] = thesis,
            ["support"] = support,
            ["counter"] = counter,
            ["rebuttal"] = rebuttal,
            ["illustration"] = illustration,
        });
    }

    /// <summary>
    /// Stores a group with no name straight through the store, as a group migrated from before names existed would
    /// be (the API cannot create one).
    /// </summary>
    private async Task<string> SeedUnnamedGroupAsync(string discussionId, string parentId, SupportGroupType type)
    {
        var store = factory.Services.GetRequiredService<IDiscussionStore>();
        var createdAt = DateTimeOffset.UtcNow;
        var group = new Statement(
            Guid.NewGuid(),
            Guid.Parse(discussionId),
            StatementKind.SupportGroup,
            Guid.Parse(parentId),
            null,
            type,
            createdAt.AddTicks(-(createdAt.UtcTicks % 10)));
        await store.AddStatementAsync(group);
        return group.Id.ToString();
    }

    private Task<HttpResponseMessage> PatchRawAsync(string discussionId, string statementId, string json) =>
        api.Http.PatchAsync(
            $"{DiscussionsPath}/{discussionId}/statements/{statementId}",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    /// <summary>The raw <c>GET</c> body, so "nothing changed" compares the whole discussion, including every <c>editedAt</c>.</summary>
    private async Task<string> RawDiscussionAsync(string id)
    {
        using var response = await api.Http.GetAsync($"{DiscussionsPath}/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<JsonElement> ReadOkAsync(HttpResponseMessage response)
    {
        var json = await ReadJsonAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200 but got {(int)response.StatusCode}: {json}");
        return json;
    }

    private static string[] ErrorFields(JsonElement problem) =>
        problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).ToArray();
}
