using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Storage;
using TheDiscussion.Storage.InMemory;

namespace TheDiscussion.Core.Tests;

/// <summary>
/// nested-named-groups AC-3 (UC-3, Cluster B): the group name rule in <see cref="DiscussionRules"/>, and how
/// <see cref="DiscussionService"/> validates a new group and a statement edit, with errors keyed by field.
/// </summary>
public class GroupNameTests
{
    /// <summary>"😀" is one emoji made of a surrogate pair: 2 UTF-16 code units (Q-4).</summary>
    private const string Emoji = "\U0001F600";

    private readonly InMemoryDiscussionStore store = new();
    private readonly DiscussionService service;

    public GroupNameTests()
    {
        service = new DiscussionService(store, TimeProvider.System);
    }

    // ---- DiscussionRules.NormalizeGroupName: Cluster B "name length after trimming" and "name content" ----

    [Theory]
    [InlineData("1 character", "a", "a")]
    [InlineData("surrounding spaces are trimmed (A1)", "  a  ", "a")]
    [InlineData("tabs and newlines are trimmed like text", "\t Evidence \n", "Evidence")]
    [InlineData("inner spaces are kept", " Productivity  mechanisms ", "Productivity  mechanisms")]
    [InlineData("combining marks round-trip", "Café résumé", "Café résumé")]
    [InlineData("Unicode letters round-trip", "Ölçüm 証拠 доказательство", "Ölçüm 証拠 доказательство")]
    public void Valid_names_are_returned_trimmed(string scenario, string name, string expected)
    {
        Assert.True(DiscussionRules.NormalizeGroupName(name) == expected, scenario);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(99)]
    [InlineData(100)]
    public void Names_of_2_99_and_100_characters_are_valid(int length)
    {
        var name = new string('n', length);

        Assert.Equal(name, DiscussionRules.NormalizeGroupName(name));
        Assert.Equal(name, DiscussionRules.NormalizeGroupName($"  {name}  "));
    }

    [Theory]
    [InlineData("missing (null)", null)]
    [InlineData("empty", "")]
    [InlineData("one space", " ")]
    [InlineData("whitespace only", " \t\r\n ")]
    public void Missing_empty_and_whitespace_only_names_are_invalid(string scenario, string? name)
    {
        Assert.True(DiscussionRules.NormalizeGroupName(name) is null, scenario);
    }

    [Fact]
    public void A_name_of_101_characters_after_trimming_is_invalid()
    {
        Assert.Null(DiscussionRules.NormalizeGroupName(new string('n', 101)));
        Assert.Null(DiscussionRules.NormalizeGroupName($"  {new string('n', 101)}  "));
    }

    [Fact]
    public void Name_length_counts_UTF16_code_units_so_an_emoji_counts_as_2()
    {
        var hundredCodeUnits = string.Concat(Enumerable.Repeat(Emoji, 50));
        Assert.Equal(100, hundredCodeUnits.Length);

        Assert.Equal(hundredCodeUnits, DiscussionRules.NormalizeGroupName(hundredCodeUnits));
        Assert.Null(DiscussionRules.NormalizeGroupName(hundredCodeUnits + "a"));
        Assert.Null(DiscussionRules.NormalizeGroupName(string.Concat(Enumerable.Repeat(Emoji, 51))));
        Assert.Equal(100, DiscussionRules.GroupNameMaxLength);
    }

    // ---- DiscussionService.AddStatementAsync: UC-3 main flow (a), E1, E6 ----

    [Fact]
    public async Task A_new_group_is_stored_with_its_trimmed_name()
    {
        var (id, thesis) = await CreateDiscussionAsync();

        var (group, _) = await service.AddStatementAsync(
            id, new NewStatement(StatementKind.SupportGroup, thesis, null, SupportGroupType.AllRequired, "  Productivity mechanisms "));

        Assert.Equal("Productivity mechanisms", group.Name);
        Assert.Equal("Productivity mechanisms", (await store.GetDiscussionAsync(id))!.Statements.Single(s => s.Id == group.Id).Name);
    }

    [Theory]
    [InlineData("missing", null)]
    [InlineData("empty", "")]
    [InlineData("whitespace only", "   ")]
    [InlineData("101 characters", "LONG")]
    public async Task A_new_group_without_a_valid_name_is_rejected_naming_name(string scenario, string? name)
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var before = await store.GetDiscussionAsync(id);

        var error = await Assert.ThrowsAsync<DiscussionValidationException>(() => service.AddStatementAsync(
            id,
            new NewStatement(StatementKind.SupportGroup, thesis, null, SupportGroupType.AnySufficient, name == "LONG" ? new string('n', 101) : name)));

        Assert.True(error.Errors.Keys.SequenceEqual(["name"]), $"{scenario}: {string.Join(", ", error.Errors.Keys)}");
        Assert.Equal(before!.Statements, (await store.GetDiscussionAsync(id))!.Statements);
    }

    [Fact]
    public async Task A_new_group_without_a_group_type_is_rejected_naming_groupType()
    {
        var (id, thesis) = await CreateDiscussionAsync();

        var error = await Assert.ThrowsAsync<DiscussionValidationException>(() =>
            service.AddStatementAsync(id, new NewStatement(StatementKind.SupportGroup, thesis, null, null, "Named")));

        Assert.Equal(["groupType"], error.Errors.Keys);
    }

    [Fact]
    public async Task A_name_on_a_new_statement_of_another_kind_is_rejected_naming_name()
    {
        var (id, thesis) = await CreateDiscussionAsync();

        var error = await Assert.ThrowsAsync<DiscussionValidationException>(() =>
            service.AddStatementAsync(id, new NewStatement(StatementKind.CounterArgument, thesis, "Counter", null, "A name")));

        Assert.Equal(["name"], error.Errors.Keys);
    }

    // ---- DiscussionService.EditStatementAsync on a group: UC-3 main flow (b), A2–A4, E2–E8 in the flowchart's order ----

    [Fact]
    public async Task Editing_a_groups_name_stores_it_trimmed_and_sets_editedAt()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await AddGroupAsync(id, thesis, "Old name", SupportGroupType.AllRequired);

        var (edited, _) = await service.EditStatementAsync(id, group.Id, StatementChanges.None.WithName("  New name  "));

        Assert.Equal("New name", edited.Name);
        Assert.Equal(SupportGroupType.AllRequired, edited.GroupType);
        Assert.NotNull(edited.EditedAt);
        Assert.Equal(edited, (await store.GetDiscussionAsync(id))!.Statements.Single(s => s.Id == group.Id));
    }

    [Fact]
    public async Task Editing_both_name_and_type_changes_both_in_one_edit()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await AddGroupAsync(id, thesis, "Old name", SupportGroupType.AllRequired);

        var (edited, _) = await service.EditStatementAsync(
            id, group.Id, StatementChanges.None.WithName("Both").WithGroupType(SupportGroupType.AnySufficient));

        Assert.Equal(group with { Name = "Both", GroupType = SupportGroupType.AnySufficient, EditedAt = edited.EditedAt }, edited);
        Assert.NotNull(edited.EditedAt);
    }

    [Fact]
    public async Task Setting_the_same_values_still_counts_as_an_edit()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await AddGroupAsync(id, thesis, "Same", SupportGroupType.AllRequired);

        var (edited, _) = await service.EditStatementAsync(
            id, group.Id, StatementChanges.None.WithName("Same").WithGroupType(SupportGroupType.AllRequired));

        Assert.Equal("Same", edited.Name);
        Assert.NotNull(edited.EditedAt);
    }

    [Fact]
    public async Task An_unnamed_group_can_be_named_but_not_retyped_without_a_name()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var unnamed = new Statement(Guid.NewGuid(), id, StatementKind.SupportGroup, thesis, null, SupportGroupType.AllRequired, DateTimeOffset.UtcNow);
        await store.AddStatementAsync(unnamed);

        var error = await Assert.ThrowsAsync<DiscussionValidationException>(() =>
            service.EditStatementAsync(id, unnamed.Id, StatementChanges.None.WithGroupType(SupportGroupType.AnySufficient)));
        Assert.Equal(["name"], error.Errors.Keys);
        Assert.Equal(unnamed, (await store.GetDiscussionAsync(id))!.Statements.Single(s => s.Id == unnamed.Id));

        var (named, _) = await service.EditStatementAsync(id, unnamed.Id, StatementChanges.None.WithName("Named at last"));
        Assert.Equal("Named at last", named.Name);
        Assert.Equal(SupportGroupType.AllRequired, named.GroupType);
    }

    public static TheoryData<string, StatementChanges, string> RejectedGroupEdits => new()
    {
        { "kind sent (E7)", StatementChanges.None.WithKind().WithName("Fine"), "kind" },
        { "parentId sent (E7)", StatementChanges.None.WithParentId().WithName("Fine"), "parentId" },
        { "kind is checked before text", StatementChanges.None.WithKind().WithText("Text"), "kind" },
        { "text sent (E4)", StatementChanges.None.WithText("Text"), "text" },
        { "name and text sent (E4)", StatementChanges.None.WithName("Fine").WithText("Text"), "text" },
        { "text is checked before an invalid name", StatementChanges.None.WithName("").WithText("Text"), "text" },
        { "nothing to change (E8, Q-2)", StatementChanges.None, "body" },
        { "name null (E2)", StatementChanges.None.WithName(null), "name" },
        { "name empty (E2)", StatementChanges.None.WithName(""), "name" },
        { "name whitespace only (E2)", StatementChanges.None.WithName("   "), "name" },
        { "name of 101 characters (E2)", StatementChanges.None.WithName(new string('n', 101)), "name" },
        { "name is checked before groupType", StatementChanges.None.WithName(" ").WithGroupType(null), "name" },
        { "groupType null or unknown (E6)", StatementChanges.None.WithGroupType(null), "groupType" },
        { "valid name with an invalid groupType (E6)", StatementChanges.None.WithName("Fine").WithGroupType(null), "groupType" },
    };

    [Theory]
    [MemberData(nameof(RejectedGroupEdits))]
    public async Task Invalid_group_edits_are_rejected_naming_only_the_first_failing_field_and_change_nothing(
        string scenario, StatementChanges changes, string field)
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await AddGroupAsync(id, thesis, "Named", SupportGroupType.AllRequired);
        var before = await store.GetDiscussionAsync(id);

        var error = await Assert.ThrowsAsync<DiscussionValidationException>(() => service.EditStatementAsync(id, group.Id, changes));

        Assert.True(error.Errors.Keys.SequenceEqual([field]), $"{scenario}: {string.Join(", ", error.Errors.Keys)}");
        Assert.Equal(before!.Statements, (await store.GetDiscussionAsync(id))!.Statements);
    }

    [Fact]
    public async Task A_format_error_found_by_the_caller_is_reported_when_its_field_is_checked()
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var group = await AddGroupAsync(id, thesis, "Named", SupportGroupType.AllRequired);
        var format = new ValidationErrors();
        format.Add("name", "'name' has the wrong JSON type; send a string.");

        var error = await Assert.ThrowsAsync<DiscussionValidationException>(() =>
            service.EditStatementAsync(id, group.Id, StatementChanges.None.WithName(null), format));

        Assert.Equal(["'name' has the wrong JSON type; send a string."], error.Errors["name"]);
    }

    // ---- DiscussionService.EditStatementAsync on other kinds: UC-3 E5 ----

    public static TheoryData<string, StatementChanges, string> RejectedTextEdits => new()
    {
        { "name sent (E5)", StatementChanges.None.WithName("A name"), "name" },
        { "name with text sent (E5)", StatementChanges.None.WithName("A name").WithText("Text"), "name" },
        { "groupType sent (E5)", StatementChanges.None.WithGroupType(SupportGroupType.AllRequired), "groupType" },
        { "groupType with text sent (E5)", StatementChanges.None.WithGroupType(SupportGroupType.AnySufficient).WithText("Text"), "groupType" },
        { "kind sent (E7)", StatementChanges.None.WithKind().WithText("Text"), "kind" },
        { "parentId sent (E7)", StatementChanges.None.WithParentId().WithText("Text"), "parentId" },
        { "no text, as today", StatementChanges.None, "text" },
    };

    [Theory]
    [MemberData(nameof(RejectedTextEdits))]
    public async Task Invalid_edits_of_a_statement_with_text_are_rejected_naming_the_field(string scenario, StatementChanges changes, string field)
    {
        var (id, thesis) = await CreateDiscussionAsync();
        var (counter, _) = await service.AddStatementAsync(id, new NewStatement(StatementKind.CounterArgument, thesis, "Counter", null));
        var before = await store.GetDiscussionAsync(id);

        var error = await Assert.ThrowsAsync<DiscussionValidationException>(() => service.EditStatementAsync(id, counter.Id, changes));

        Assert.True(error.Errors.Keys.SequenceEqual([field]), $"{scenario}: {string.Join(", ", error.Errors.Keys)}");
        Assert.Equal(before!.Statements, (await store.GetDiscussionAsync(id))!.Statements);
    }

    [Fact]
    public async Task Editing_an_unknown_statement_or_discussion_is_not_found()
    {
        var (id, _) = await CreateDiscussionAsync();

        await Assert.ThrowsAsync<StatementNotFoundException>(() =>
            service.EditStatementAsync(id, Guid.NewGuid(), StatementChanges.None.WithKind()));
        await Assert.ThrowsAsync<DiscussionNotFoundException>(() =>
            service.EditStatementAsync(Guid.NewGuid(), Guid.NewGuid(), StatementChanges.None.WithName("Name")));
    }

    private async Task<(Guid DiscussionId, Guid ThesisId)> CreateDiscussionAsync()
    {
        var created = await service.CreateDiscussionAsync(new NewDiscussion("Title", ["a.b"], "Thesis"));
        return (created.Discussion.Id, created.Discussion.Thesis.Id);
    }

    private async Task<Statement> AddGroupAsync(Guid id, Guid parentId, string name, SupportGroupType type) =>
        (await service.AddStatementAsync(id, new NewStatement(StatementKind.SupportGroup, parentId, null, type, name))).Statement;
}
