using Microsoft.EntityFrameworkCore;
using TheDiscussion.Core.Storage;
using TheDiscussion.Storage.Postgres;

namespace TheDiscussion.Storage.Tests;

/// <summary>
/// Runs the shared storage contract against PostgreSQL. Runs in CI against a postgres service container;
/// skipped locally (with a visible reason) unless THEDISCUSSION_TEST_POSTGRES is set.
/// </summary>
public sealed class PostgresDiscussionStoreContractTests(PostgresDatabaseFixture database)
    : DiscussionStoreContract, IClassFixture<PostgresDatabaseFixture>
{
    protected override IDiscussionStore CreateStore()
    {
        Skip.If(database.SkipReason is not null, database.SkipReason);
        return new PostgresDiscussionStore(database.ContextFactory);
    }

    [SkippableFact]
    public async Task Migrations_are_all_applied_and_none_are_pending()
    {
        Skip.If(database.SkipReason is not null, database.SkipReason);
        await using var context = await database.ContextFactory.CreateDbContextAsync();

        var applied = await context.Database.GetAppliedMigrationsAsync();
        var pending = await context.Database.GetPendingMigrationsAsync();

        Assert.Equal(context.Database.GetMigrations(), applied);
        Assert.Empty(pending);
    }

    [SkippableFact]
    public async Task Store_reports_the_database_as_reachable()
    {
        var store = CreateStore();

        Assert.True(await store.CanConnectAsync());
    }

    [SkippableFact]
    public async Task Database_rejects_a_second_thesis_in_one_discussion()
    {
        Skip.If(database.SkipReason is not null, database.SkipReason);
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("One thesis only", "contract.integrity");
        await store.CreateDiscussionAsync(discussion, thesis);

        await using var context = await database.ContextFactory.CreateDbContextAsync();
        context.Add(new StatementEntity
        {
            Id = Guid.NewGuid(),
            DiscussionId = discussion.Id,
            Kind = Core.Discussions.StatementKind.Thesis,
            Text = "A second thesis",
            CreatedAt = Now(),
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
