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

    /// <summary>Current UTC time at microsecond precision (the precision PostgreSQL stores).</summary>
    protected static DateTimeOffset Now()
    {
        var ticks = DateTimeOffset.UtcNow.UtcTicks;
        return new DateTimeOffset(ticks - (ticks % 10), TimeSpan.Zero);
    }

    protected static (DiscussionSummary Discussion, Statement Thesis) NewDiscussion(string title, string topicTag)
    {
        var id = Guid.NewGuid();
        var createdAt = Now();
        return (
            new DiscussionSummary(id, title, topicTag, createdAt),
            new Statement(Guid.NewGuid(), id, StatementKind.Thesis, null, title + " (thesis)", null, createdAt));
    }

    protected static Statement Group(Guid discussionId, Guid thesisId, SupportGroupType type) =>
        new(Guid.NewGuid(), discussionId, StatementKind.SupportGroup, thesisId, null, type, Now());

    protected static Statement Child(Guid discussionId, StatementKind kind, Guid parentId, string text) =>
        new(Guid.NewGuid(), discussionId, kind, parentId, text, null, Now());
}
