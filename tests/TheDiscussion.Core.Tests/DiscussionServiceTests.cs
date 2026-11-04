using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Status;
using TheDiscussion.Core.Storage;
using TheDiscussion.Storage.InMemory;

namespace TheDiscussion.Core.Tests;

/// <summary>Validation boundaries (D-9) and behavior of the application service.</summary>
public class DiscussionServiceTests
{
    private readonly InMemoryDiscussionStore store = new();
    private readonly DiscussionService service;

    public DiscussionServiceTests()
    {
        service = new DiscussionService(store, TimeProvider.System);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public async Task Title_length_within_limits_is_accepted(int length)
    {
        var created = await service.CreateDiscussionAsync(new NewDiscussion(new string('t', length), "a.b", "Thesis"));

        Assert.Equal(length, created.Discussion.Title.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_title_is_rejected(string? title)
    {
        var error = await Assert.ThrowsAsync<DiscussionValidationException>(() =>
            service.CreateDiscussionAsync(new NewDiscussion(title, "a.b", "Thesis")));

        Assert.Equal(["title"], error.Errors.Keys);
        Assert.Empty(await store.ListDiscussionsAsync());
    }

    [Fact]
    public async Task Title_over_200_characters_is_rejected()
    {
        var error = await Assert.ThrowsAsync<DiscussionValidationException>(() =>
            service.CreateDiscussionAsync(new NewDiscussion(new string('t', 201), "a.b", "Thesis")));

        Assert.Contains("title", error.Errors.Keys);
    }

    [Fact]
    public async Task Thesis_of_5000_characters_is_accepted_and_5001_rejected()
    {
        await service.CreateDiscussionAsync(new NewDiscussion("Title", "a.b", new string('x', 5000)));

        var error = await Assert.ThrowsAsync<DiscussionValidationException>(() =>
            service.CreateDiscussionAsync(new NewDiscussion("Title", "a.b", new string('x', 5001))));

        Assert.Equal(["thesis"], error.Errors.Keys);
    }

    [Fact]
    public async Task Every_invalid_field_is_reported_at_once()
    {
        var error = await Assert.ThrowsAsync<DiscussionValidationException>(() =>
            service.CreateDiscussionAsync(new NewDiscussion(null, "Not A Tag", null)));

        Assert.Equal(new[] { "thesis", "title", "topicTag" }, error.Errors.Keys.Order());
    }

    [Fact]
    public async Task Text_is_trimmed_before_it_is_stored()
    {
        var created = await service.CreateDiscussionAsync(new NewDiscussion("  Title  ", " work.remote ", "  Thesis  "));

        Assert.Equal("Title", created.Discussion.Title);
        Assert.Equal("work.remote", created.Discussion.TopicTag);
        Assert.Equal("Thesis", created.Discussion.Thesis.Text);
    }

    [Fact]
    public async Task Status_is_recomputed_from_the_stored_tree_on_every_read()
    {
        var created = await service.CreateDiscussionAsync(new NewDiscussion("Title", "a.b", "Thesis"));
        var id = created.Discussion.Id;
        Assert.Equal(ThesisStatus.Unsupported, created.Status.Thesis);

        var (group, _) = await service.AddStatementAsync(id, new NewStatement(StatementKind.SupportGroup, created.Discussion.Thesis.Id, null, SupportGroupType.AllRequired));
        var (support, afterSupport) = await service.AddStatementAsync(id, new NewStatement(StatementKind.SupportingStatement, group.Id, "Support", null));
        Assert.Equal(ThesisStatus.Open, afterSupport.Status.Thesis);

        await service.AddStatementAsync(id, new NewStatement(StatementKind.CounterArgument, support.Id, "Counter", null));
        var read = await service.GetDiscussionAsync(id);

        Assert.Equal(ThesisStatus.Countered, read!.Status.Thesis);
    }

    [Fact]
    public async Task Statement_text_of_5001_characters_is_rejected_and_nothing_is_stored()
    {
        var created = await service.CreateDiscussionAsync(new NewDiscussion("Title", "a.b", "Thesis"));

        var error = await Assert.ThrowsAsync<DiscussionValidationException>(() => service.AddStatementAsync(
            created.Discussion.Id,
            new NewStatement(StatementKind.CounterArgument, created.Discussion.Thesis.Id, new string('x', 5001), null)));

        Assert.Equal(["text"], error.Errors.Keys);
        Assert.Single((await store.GetDiscussionAsync(created.Discussion.Id))!.Statements);
    }

    [Fact]
    public async Task Adding_to_an_unknown_discussion_throws_not_found()
    {
        await Assert.ThrowsAsync<DiscussionNotFoundException>(() => service.AddStatementAsync(
            Guid.NewGuid(),
            new NewStatement(StatementKind.CounterArgument, Guid.NewGuid(), "Counter", null)));
    }
}
