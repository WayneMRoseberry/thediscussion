using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Storage;

namespace TheDiscussion.Storage.Tests;

/// <summary>
/// The storage contract every <see cref="IDiscussionStore"/> provider must satisfy (AC-2).
/// Each provider gets a concrete subclass; the tests themselves live here so all providers run the same suite.
/// Tests only rely on data they create (random ids), so they can share one database.
/// </summary>
public abstract class DiscussionStoreContract
{
    /// <summary>Returns the store under test, or skips the test (with a reason) when the provider is unavailable.</summary>
    protected abstract IDiscussionStore CreateStore();

    [SkippableFact]
    public async Task Created_discussion_is_read_back_with_its_thesis()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Remote work boosts productivity", "work.remote");

        await store.CreateDiscussionAsync(discussion, thesis);
        var stored = await store.GetDiscussionAsync(discussion.Id);

        Assert.NotNull(stored);
        Assert.Equal(discussion, stored.ToSummary());
        var only = Assert.Single(stored.Statements);
        Assert.Equal(thesis, only);
        Assert.Equal(thesis, stored.Thesis);
    }

    [SkippableFact]
    public async Task Several_topic_tags_are_stored_and_read_back_in_their_order()
    {
        var store = CreateStore();
        string[] tags = ["work.remote", "management", "a.b.c.d.e"];
        var (discussion, thesis) = NewDiscussion("Several tags", tags);

        await store.CreateDiscussionAsync(discussion, thesis);
        var stored = await store.GetDiscussionAsync(discussion.Id);
        var listed = (await store.ListDiscussionsAsync()).Single(d => d.Id == discussion.Id);

        Assert.Equal(tags, stored!.TopicTags);
        Assert.Equal(tags, listed.TopicTags);
    }

    [SkippableFact]
    public async Task Updating_tags_can_reorder_shrink_and_grow_the_list()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Retag", "contract.tags.a", "contract.tags.b");
        await store.CreateDiscussionAsync(discussion, thesis);

        await store.UpdateDiscussionAsync(discussion.Id, "Retag", ["contract.tags.b", "contract.tags.a"]);
        Assert.Equal(["contract.tags.b", "contract.tags.a"], (await store.GetDiscussionAsync(discussion.Id))!.TopicTags);

        await store.UpdateDiscussionAsync(discussion.Id, "Retag", ["contract.tags.c"]);
        Assert.Equal(["contract.tags.c"], (await store.GetDiscussionAsync(discussion.Id))!.TopicTags);

        string[] five = ["contract.tags.c", "v", "w", "x", "y"];
        await store.UpdateDiscussionAsync(discussion.Id, "Retag", five);
        Assert.Equal(five, (await store.GetDiscussionAsync(discussion.Id))!.TopicTags);
    }

    [SkippableFact]
    public async Task Listing_includes_created_discussions_most_recent_first()
    {
        var store = CreateStore();
        var (first, firstThesis) = NewDiscussion("First", "contract.listing");
        var (second, secondThesis) = NewDiscussion("Second", "contract.listing");

        await store.CreateDiscussionAsync(first, firstThesis);
        await store.CreateDiscussionAsync(second, secondThesis);
        var listed = await store.ListDiscussionsAsync();

        var ids = listed.Select(summary => summary.Id).ToList();
        Assert.Contains(first.Id, ids);
        Assert.Contains(second.Id, ids);
        Assert.True(ids.IndexOf(second.Id) < ids.IndexOf(first.Id), "The most recently created discussion is listed first.");
        Assert.Equal(second, listed.Single(summary => summary.Id == second.Id));
    }

    [SkippableFact]
    public async Task Every_statement_kind_is_stored_and_the_full_tree_is_read_back_in_creation_order()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Every kind", "contract.tree");
        await store.CreateDiscussionAsync(discussion, thesis);

        var allRequired = Group(discussion.Id, thesis.Id, SupportGroupType.AllRequired);
        var anySufficient = Group(discussion.Id, thesis.Id, SupportGroupType.AnySufficient);
        var support = Child(discussion.Id, StatementKind.SupportingStatement, allRequired.Id, "Fewer interruptions");
        var otherSupport = Child(discussion.Id, StatementKind.SupportingStatement, anySufficient.Id, "Studies show +13% output");
        var thesisIllustration = Child(discussion.Id, StatementKind.Illustration, thesis.Id, "e.g. my team shipped early");
        var supportIllustration = Child(discussion.Id, StatementKind.Illustration, support.Id, "No more desk drive-bys");
        var thesisCounter = Child(discussion.Id, StatementKind.CounterArgument, thesis.Id, "Collaboration suffers");
        var supportCounter = Child(discussion.Id, StatementKind.CounterArgument, support.Id, "Slack pings replace them");
        var rebuttal = Child(discussion.Id, StatementKind.Rebuttal, thesisCounter.Id, "Async docs offset this");
        var rebuttalOfRebuttal = Child(discussion.Id, StatementKind.Rebuttal, rebuttal.Id, "Docs go stale");
        var added = new[]
        {
            allRequired, anySufficient, support, otherSupport, thesisIllustration, supportIllustration,
            thesisCounter, supportCounter, rebuttal, rebuttalOfRebuttal,
        };

        foreach (var statement in added)
        {
            await store.AddStatementAsync(statement);
        }

        var stored = await store.GetDiscussionAsync(discussion.Id);

        Assert.NotNull(stored);
        Assert.Equal([thesis, .. added], stored.Statements);
        Assert.Equal(thesis.Id, stored.Statements.Single(s => s.Id == allRequired.Id).ParentId);
        Assert.Equal(SupportGroupType.AnySufficient, stored.Statements.Single(s => s.Id == anySufficient.Id).GroupType);
        Assert.Null(stored.Statements.Single(s => s.Id == anySufficient.Id).Text);
        Assert.Equal(rebuttal.Id, stored.Statements.Single(s => s.Id == rebuttalOfRebuttal.Id).ParentId);
    }

    [SkippableFact]
    public async Task Text_at_the_maximum_length_round_trips_unchanged()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion(new string('T', 200), "contract.limits.max-length");
        await store.CreateDiscussionAsync(discussion, thesis);
        var group = Group(discussion.Id, thesis.Id, SupportGroupType.AllRequired);
        var longText = string.Concat(Enumerable.Repeat("é—ü 🙂 ", 5000))[..5000];
        var support = Child(discussion.Id, StatementKind.SupportingStatement, group.Id, longText);

        await store.AddStatementAsync(group);
        await store.AddStatementAsync(support);
        var stored = await store.GetDiscussionAsync(discussion.Id);

        Assert.NotNull(stored);
        Assert.Equal(discussion.Title, stored.Title);
        Assert.Equal(longText, stored.Statements.Single(s => s.Id == support.Id).Text);
    }

    [SkippableFact]
    public async Task Statements_belong_only_to_their_own_discussion()
    {
        var store = CreateStore();
        var (one, oneThesis) = NewDiscussion("One", "contract.isolation");
        var (two, twoThesis) = NewDiscussion("Two", "contract.isolation");
        await store.CreateDiscussionAsync(one, oneThesis);
        await store.CreateDiscussionAsync(two, twoThesis);

        await store.AddStatementAsync(Child(one.Id, StatementKind.CounterArgument, oneThesis.Id, "Only in one"));
        var storedTwo = await store.GetDiscussionAsync(two.Id);

        Assert.NotNull(storedTwo);
        Assert.Equal([twoThesis], storedTwo.Statements);
    }

    [SkippableFact]
    public async Task Unknown_discussion_reads_as_null()
    {
        var store = CreateStore();

        var stored = await store.GetDiscussionAsync(Guid.NewGuid());

        Assert.Null(stored);
    }

    [SkippableFact]
    public async Task Adding_to_an_unknown_discussion_throws_DiscussionNotFound()
    {
        var store = CreateStore();
        var missingDiscussion = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<DiscussionNotFoundException>(() =>
            store.AddStatementAsync(Child(missingDiscussion, StatementKind.CounterArgument, Guid.NewGuid(), "Nobody hears this")));

        Assert.Equal(missingDiscussion, error.DiscussionId);
    }

    [SkippableFact]
    public async Task Adding_under_an_unknown_parent_throws_ParentStatementNotFound_and_stores_nothing()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Unknown parent", "contract.not-found");
        await store.CreateDiscussionAsync(discussion, thesis);
        var missingParent = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<ParentStatementNotFoundException>(() =>
            store.AddStatementAsync(Child(discussion.Id, StatementKind.Rebuttal, missingParent, "Orphan")));

        Assert.Equal(missingParent, error.ParentId);
        var stored = await store.GetDiscussionAsync(discussion.Id);
        Assert.Equal([thesis], stored!.Statements);
    }

    [SkippableFact]
    public async Task Adding_under_a_parent_from_another_discussion_throws_ParentStatementNotFound()
    {
        var store = CreateStore();
        var (one, oneThesis) = NewDiscussion("One", "contract.not-found");
        var (two, twoThesis) = NewDiscussion("Two", "contract.not-found");
        await store.CreateDiscussionAsync(one, oneThesis);
        await store.CreateDiscussionAsync(two, twoThesis);

        await Assert.ThrowsAsync<ParentStatementNotFoundException>(() =>
            store.AddStatementAsync(Child(two.Id, StatementKind.CounterArgument, oneThesis.Id, "Wrong discussion")));

        var storedTwo = await store.GetDiscussionAsync(two.Id);
        Assert.Equal([twoThesis], storedTwo!.Statements);
    }

    // ---- Tag filtering (discussion-crud-and-tags AC-5) ----

    [SkippableFact]
    public async Task Listing_under_a_tag_returns_the_tag_and_its_sub_tags_once_each_most_recent_first()
    {
        var store = CreateStore();
        var root = UniqueTagRoot();
        var (exact, exactThesis) = NewDiscussion("Exact", root);
        var (child, childThesis) = NewDiscussion("Child", $"{root}.remote");
        var (grandchild, grandchildThesis) = NewDiscussion("Grandchild", "other", $"{root}.remote.tools");
        var (notASegment, notASegmentThesis) = NewDiscussion("Not a segment", $"{root}s", $"{root}s.remote");
        var (twoMatches, twoMatchesThesis) = NewDiscussion("Two matches", $"{root}.remote", $"{root}.hours");
        var (containsOnly, containsOnlyThesis) = NewDiscussion("Contains only", $"other.{root}");
        foreach (var (discussion, thesis) in new[]
        {
            (exact, exactThesis), (child, childThesis), (grandchild, grandchildThesis),
            (notASegment, notASegmentThesis), (twoMatches, twoMatchesThesis), (containsOnly, containsOnlyThesis),
        })
        {
            await store.CreateDiscussionAsync(discussion, thesis);
        }

        Assert.Equal([twoMatches, grandchild, child, exact], await store.ListDiscussionsUnderTagAsync(root));
        Assert.Equal([twoMatches, grandchild, child], await store.ListDiscussionsUnderTagAsync($"{root}.remote"));
        Assert.Equal([grandchild], await store.ListDiscussionsUnderTagAsync($"{root}.remote.tools"));
        Assert.Equal([twoMatches], await store.ListDiscussionsUnderTagAsync($"{root}.hours"));
        Assert.Equal([notASegment], await store.ListDiscussionsUnderTagAsync($"{root}s"));
        Assert.Empty(await store.ListDiscussionsUnderTagAsync(root[..^1]));
        Assert.Empty(await store.ListDiscussionsUnderTagAsync($"{root}.remo"));
        Assert.Empty(await store.ListDiscussionsUnderTagAsync($"{root}.unused"));
    }

    [SkippableFact]
    public async Task Tag_listing_follows_tag_changes_and_deletes()
    {
        var store = CreateStore();
        var root = UniqueTagRoot();
        var (moved, movedThesis) = NewDiscussion("Moved", $"{root}.before");
        var (deleted, deletedThesis) = NewDiscussion("Deleted", $"{root}.before");
        await store.CreateDiscussionAsync(moved, movedThesis);
        await store.CreateDiscussionAsync(deleted, deletedThesis);

        await store.UpdateDiscussionAsync(moved.Id, moved.Title, [$"{root}.after"]);
        await store.DeleteDiscussionAsync(deleted.Id);

        Assert.Empty(await store.ListDiscussionsUnderTagAsync($"{root}.before"));
        Assert.Equal([moved.Id], (await store.ListDiscussionsUnderTagAsync(root)).Select(d => d.Id));
        Assert.Equal([$"{root}.after"], (await store.ListDiscussionsUnderTagAsync($"{root}.after")).Single().TopicTags);
    }

    // ---- Update and delete (discussion-crud-and-tags AC-1 to AC-3) ----

    [SkippableFact]
    public async Task Updating_a_discussion_replaces_its_title_and_tag_and_keeps_its_statements()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Before", "contract.update");
        await store.CreateDiscussionAsync(discussion, thesis);
        var counter = Child(discussion.Id, StatementKind.CounterArgument, thesis.Id, "Counter");
        await store.AddStatementAsync(counter);

        var updated = await store.UpdateDiscussionAsync(discussion.Id, "After", ["contract.update.after", "contract.other"]);
        var stored = await store.GetDiscussionAsync(discussion.Id);

        Assert.True(updated);
        Assert.Equal(discussion with { Title = "After", TopicTags = ["contract.update.after", "contract.other"] }, stored!.ToSummary());
        Assert.Equal([thesis, counter], stored.Statements);
        Assert.Equal("After", (await store.ListDiscussionsAsync()).Single(d => d.Id == discussion.Id).Title);
    }

    [SkippableFact]
    public async Task Updating_an_unknown_discussion_returns_false()
    {
        var store = CreateStore();

        Assert.False(await store.UpdateDiscussionAsync(Guid.NewGuid(), "Title", ["contract.update"]));
    }

    [SkippableFact]
    public async Task Deleting_a_discussion_removes_it_and_all_its_statements_and_nothing_else()
    {
        var store = CreateStore();
        var (doomed, doomedThesis) = NewDiscussion("Doomed", "contract.delete");
        var (kept, keptThesis) = NewDiscussion("Kept", "contract.delete");
        await store.CreateDiscussionAsync(doomed, doomedThesis);
        await store.CreateDiscussionAsync(kept, keptThesis);
        var group = Group(doomed.Id, doomedThesis.Id, SupportGroupType.AllRequired);
        var support = Child(doomed.Id, StatementKind.SupportingStatement, group.Id, "Support");
        var counter = Child(doomed.Id, StatementKind.CounterArgument, support.Id, "Counter");
        var rebuttal = Child(doomed.Id, StatementKind.Rebuttal, counter.Id, "Rebuttal");
        foreach (var statement in new[] { group, support, counter, rebuttal })
        {
            await store.AddStatementAsync(statement);
        }

        var deleted = await store.DeleteDiscussionAsync(doomed.Id);

        Assert.True(deleted);
        Assert.Null(await store.GetDiscussionAsync(doomed.Id));
        var ids = (await store.ListDiscussionsAsync()).Select(d => d.Id).ToList();
        Assert.DoesNotContain(doomed.Id, ids);
        Assert.Contains(kept.Id, ids);
        Assert.Equal([keptThesis], (await store.GetDiscussionAsync(kept.Id))!.Statements);
        await Assert.ThrowsAsync<DiscussionNotFoundException>(() =>
            store.AddStatementAsync(Child(doomed.Id, StatementKind.Rebuttal, rebuttal.Id, "Too late")));
    }

    [SkippableFact]
    public async Task Deleting_an_unknown_discussion_returns_false()
    {
        var store = CreateStore();

        Assert.False(await store.DeleteDiscussionAsync(Guid.NewGuid()));
    }

    [SkippableFact]
    public async Task Editing_a_statement_replaces_its_text_and_sets_edited_at_without_moving_it()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Edit", "contract.edit");
        await store.CreateDiscussionAsync(discussion, thesis);
        var counter = Child(discussion.Id, StatementKind.CounterArgument, thesis.Id, "Original counter");
        var rebuttal = Child(discussion.Id, StatementKind.Rebuttal, counter.Id, "Rebuttal");
        await store.AddStatementAsync(counter);
        await store.AddStatementAsync(rebuttal);
        var editedAt = Now();

        await store.UpdateStatementTextAsync(discussion.Id, counter.Id, "Edited counter", editedAt);
        await store.UpdateStatementTextAsync(discussion.Id, thesis.Id, "Edited thesis", editedAt);
        var stored = await store.GetDiscussionAsync(discussion.Id);

        Assert.Equal(
            [thesis with { Text = "Edited thesis", EditedAt = editedAt }, counter with { Text = "Edited counter", EditedAt = editedAt }, rebuttal],
            stored!.Statements);
        Assert.Null(stored.Statements.Single(s => s.Id == rebuttal.Id).EditedAt);
    }

    [SkippableFact]
    public async Task Editing_an_unknown_statement_or_discussion_throws_not_found()
    {
        var store = CreateStore();
        var (one, oneThesis) = NewDiscussion("One", "contract.edit");
        var (two, twoThesis) = NewDiscussion("Two", "contract.edit");
        await store.CreateDiscussionAsync(one, oneThesis);
        await store.CreateDiscussionAsync(two, twoThesis);

        await Assert.ThrowsAsync<DiscussionNotFoundException>(() =>
            store.UpdateStatementTextAsync(Guid.NewGuid(), oneThesis.Id, "Text", Now()));
        await Assert.ThrowsAsync<StatementNotFoundException>(() =>
            store.UpdateStatementTextAsync(one.Id, Guid.NewGuid(), "Text", Now()));
        await Assert.ThrowsAsync<StatementNotFoundException>(() =>
            store.UpdateStatementTextAsync(two.Id, oneThesis.Id, "Text", Now()));
        Assert.Equal([oneThesis], (await store.GetDiscussionAsync(one.Id))!.Statements);
    }

    [SkippableFact]
    public async Task Deleting_a_statement_removes_its_whole_subtree_and_leaves_the_rest_of_the_tree_intact()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Subtree", "contract.delete-subtree");
        await store.CreateDiscussionAsync(discussion, thesis);
        var group = Group(discussion.Id, thesis.Id, SupportGroupType.AllRequired);
        var support = Child(discussion.Id, StatementKind.SupportingStatement, group.Id, "Doomed support");
        var otherSupport = Child(discussion.Id, StatementKind.SupportingStatement, group.Id, "Kept support");
        var illustration = Child(discussion.Id, StatementKind.Illustration, support.Id, "Doomed illustration");
        var counter = Child(discussion.Id, StatementKind.CounterArgument, support.Id, "Doomed counter");
        var rebuttal = Child(discussion.Id, StatementKind.Rebuttal, counter.Id, "Doomed rebuttal");
        var deeper = Child(discussion.Id, StatementKind.Rebuttal, rebuttal.Id, "Doomed deeper rebuttal");
        var otherCounter = Child(discussion.Id, StatementKind.CounterArgument, otherSupport.Id, "Kept counter");
        var thesisCounter = Child(discussion.Id, StatementKind.CounterArgument, thesis.Id, "Kept thesis counter");
        var added = new[] { group, support, otherSupport, illustration, counter, rebuttal, deeper, otherCounter, thesisCounter };
        foreach (var statement in added)
        {
            await store.AddStatementAsync(statement);
        }

        await store.DeleteStatementAsync(discussion.Id, support.Id);
        var stored = await store.GetDiscussionAsync(discussion.Id);

        Assert.Equal([thesis, group, otherSupport, otherCounter, thesisCounter], stored!.Statements);

        // The rest of the tree keeps working: new statements attach to survivors, never to deleted statements.
        var newRebuttal = Child(discussion.Id, StatementKind.Rebuttal, otherCounter.Id, "New rebuttal");
        await store.AddStatementAsync(newRebuttal);
        await Assert.ThrowsAsync<ParentStatementNotFoundException>(() =>
            store.AddStatementAsync(Child(discussion.Id, StatementKind.Rebuttal, rebuttal.Id, "Under a deleted rebuttal")));
        Assert.Equal([thesis, group, otherSupport, otherCounter, thesisCounter, newRebuttal], (await store.GetDiscussionAsync(discussion.Id))!.Statements);
    }

    [SkippableFact]
    public async Task Deleting_a_leaf_statement_removes_only_that_statement()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Leaf", "contract.delete-subtree");
        await store.CreateDiscussionAsync(discussion, thesis);
        var counter = Child(discussion.Id, StatementKind.CounterArgument, thesis.Id, "Counter");
        var rebuttal = Child(discussion.Id, StatementKind.Rebuttal, counter.Id, "Rebuttal");
        await store.AddStatementAsync(counter);
        await store.AddStatementAsync(rebuttal);

        await store.DeleteStatementAsync(discussion.Id, rebuttal.Id);

        Assert.Equal([thesis, counter], (await store.GetDiscussionAsync(discussion.Id))!.Statements);
    }

    [SkippableFact]
    public async Task Deleting_the_thesis_is_refused_and_changes_nothing()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Thesis stays", "contract.delete-subtree");
        await store.CreateDiscussionAsync(discussion, thesis);
        var counter = Child(discussion.Id, StatementKind.CounterArgument, thesis.Id, "Counter");
        await store.AddStatementAsync(counter);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteStatementAsync(discussion.Id, thesis.Id));

        Assert.Equal([thesis, counter], (await store.GetDiscussionAsync(discussion.Id))!.Statements);
    }

    [SkippableFact]
    public async Task Deleting_an_unknown_statement_or_discussion_throws_not_found()
    {
        var store = CreateStore();
        var (one, oneThesis) = NewDiscussion("One", "contract.delete-subtree");
        var (two, twoThesis) = NewDiscussion("Two", "contract.delete-subtree");
        await store.CreateDiscussionAsync(one, oneThesis);
        await store.CreateDiscussionAsync(two, twoThesis);
        var counter = Child(one.Id, StatementKind.CounterArgument, oneThesis.Id, "Counter");
        await store.AddStatementAsync(counter);

        await Assert.ThrowsAsync<DiscussionNotFoundException>(() => store.DeleteStatementAsync(Guid.NewGuid(), counter.Id));
        await Assert.ThrowsAsync<StatementNotFoundException>(() => store.DeleteStatementAsync(one.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<StatementNotFoundException>(() => store.DeleteStatementAsync(two.Id, counter.Id));

        Assert.Equal([oneThesis, counter], (await store.GetDiscussionAsync(one.Id))!.Statements);
    }

    /// <summary>A top-level tag no other test uses, so tag queries only see this test's discussions.</summary>
    protected static string UniqueTagRoot() => $"t{Guid.NewGuid():N}";

    /// <summary>Current UTC time at microsecond precision (the precision PostgreSQL stores).</summary>
    protected static DateTimeOffset Now()
    {
        var ticks = DateTimeOffset.UtcNow.UtcTicks;
        return new DateTimeOffset(ticks - (ticks % 10), TimeSpan.Zero);
    }

    protected static (DiscussionSummary Discussion, Statement Thesis) NewDiscussion(string title, params string[] topicTags)
    {
        var id = Guid.NewGuid();
        var createdAt = Now();
        return (
            new DiscussionSummary(id, title, [.. topicTags], createdAt),
            new Statement(Guid.NewGuid(), id, StatementKind.Thesis, null, title + " (thesis)", null, createdAt));
    }

    protected static Statement Group(Guid discussionId, Guid thesisId, SupportGroupType type) =>
        new(Guid.NewGuid(), discussionId, StatementKind.SupportGroup, thesisId, null, type, Now());

    protected static Statement Child(Guid discussionId, StatementKind kind, Guid parentId, string text) =>
        new(Guid.NewGuid(), discussionId, kind, parentId, text, null, Now());
}
