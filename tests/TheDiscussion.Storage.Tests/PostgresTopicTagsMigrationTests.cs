using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TheDiscussion.Storage.Postgres;

namespace TheDiscussion.Storage.Tests;

/// <summary>
/// discussion-crud-and-tags AC-4: a discussion stored with the single <c>topic_tag</c> column of the initial schema
/// reads back after the migrations with a one-item <c>TopicTags</c> holding its old tag. Needs PostgreSQL (see
/// <see cref="PostgresDatabaseFixture"/>); runs on its own database with no migrations applied up front.
/// </summary>
public sealed class PostgresTopicTagsMigrationTests(UnmigratedPostgresDatabaseFixture database)
    : IClassFixture<UnmigratedPostgresDatabaseFixture>
{
    private const string InitialSchema = "20261103235923_InitialSchema";

    [SkippableFact]
    public async Task A_discussion_stored_with_one_topic_tag_reads_back_with_that_tag_as_its_only_tag()
    {
        Skip.If(database.SkipReason is not null, database.SkipReason);
        await using var context = await database.ContextFactory.CreateDbContextAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(InitialSchema);
        Assert.Equal([InitialSchema], await context.Database.GetAppliedMigrationsAsync());

        var discussionId = Guid.NewGuid();
        var thesisId = Guid.NewGuid();
        var counterId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO discussions (id, title, topic_tag, created_at) VALUES ({discussionId}, 'Stored before tags were lists', 'work.remote', {createdAt})");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO statements (id, discussion_id, parent_id, kind, text, created_at) VALUES ({thesisId}, {discussionId}, NULL, 'Thesis', 'Old thesis', {createdAt})");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO statements (id, discussion_id, parent_id, kind, text, created_at) VALUES ({counterId}, {discussionId}, {thesisId}, 'CounterArgument', 'Old counter', {createdAt})");

        await migrator.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        var store = new PostgresDiscussionStore(database.ContextFactory);
        var stored = await store.GetDiscussionAsync(discussionId);
        Assert.NotNull(stored);
        Assert.Equal(["work.remote"], stored.TopicTags);
        Assert.Equal("Stored before tags were lists", stored.Title);
        Assert.Equal(createdAt, stored.CreatedAt);
        Assert.Equal([thesisId, counterId], stored.Statements.Select(s => s.Id));
        Assert.All(stored.Statements, s => Assert.Null(s.EditedAt));
        Assert.Equal(["work.remote"], (await store.ListDiscussionsAsync()).Single(d => d.Id == discussionId).TopicTags);

        // The migrated discussion behaves like any other: its tags can change and its subtree can be deleted.
        Assert.True(await store.UpdateDiscussionAsync(discussionId, stored.Title, ["work.remote", "management"]));
        await store.DeleteStatementAsync(discussionId, counterId);
        var updated = await store.GetDiscussionAsync(discussionId);
        Assert.Equal(["work.remote", "management"], updated!.TopicTags);
        Assert.Equal([thesisId], updated.Statements.Select(s => s.Id));
    }
}
