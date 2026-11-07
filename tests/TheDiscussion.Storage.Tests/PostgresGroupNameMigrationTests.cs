using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TheDiscussion.Core.Discussions;
using TheDiscussion.Storage.Postgres;

namespace TheDiscussion.Storage.Tests;

/// <summary>
/// nested-named-groups AC-3 (UC-3 main flow c; Cluster C "Pre-migration data" and "Migrated null-name groups"): data
/// stored before groups had names survives the <c>NamedSupportGroups</c> migration unchanged, and every existing group
/// keeps its group type and gets a <c>null</c> name. Needs PostgreSQL (see <see cref="PostgresDatabaseFixture"/>); runs
/// on its own database with no migrations applied up front.
/// </summary>
public sealed class PostgresGroupNameMigrationTests(UnmigratedPostgresDatabaseFixture database)
    : IClassFixture<UnmigratedPostgresDatabaseFixture>
{
    internal const string MultipleTopicTags = "20261104043745_MultipleTopicTags";
    internal const string NamedSupportGroups = "20261107184914_NamedSupportGroups";

    /// <summary>Every column that existed before the migration, one JSON object per row, so rows compare in full.</summary>
    private const string StatementRows =
        "SELECT row_to_json(s)::text AS \"Value\" FROM (SELECT id, discussion_id, parent_id, kind, text, group_type, created_at, edited_at, sequence FROM statements) s";

    private const string DiscussionRows =
        "SELECT row_to_json(d)::text AS \"Value\" FROM (SELECT id, title, created_at, sequence FROM discussions) d";

    private const string TopicTagRows =
        "SELECT row_to_json(t)::text AS \"Value\" FROM (SELECT discussion_id, tag, position FROM discussion_topic_tags) t";

    [SkippableFact]
    public async Task Existing_groups_keep_their_type_and_get_a_null_name_and_every_other_row_is_unchanged()
    {
        Skip.If(database.SkipReason is not null, database.SkipReason);
        await using var context = await database.ContextFactory.CreateDbContextAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MultipleTopicTags);
        Assert.Equal(MultipleTopicTags, (await context.Database.GetAppliedMigrationsAsync()).Last());

        var data = await InsertPreChangeDataAsync(context);
        var statementsBefore = await RowsAsync(context, StatementRows);
        var discussionsBefore = await RowsAsync(context, DiscussionRows);
        var tagsBefore = await RowsAsync(context, TopicTagRows);
        Assert.Equal(17, statementsBefore.Count);
        Assert.Equal(2, discussionsBefore.Count);
        Assert.Equal(4, tagsBefore.Count);

        await migrator.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(NamedSupportGroups, (await context.Database.GetAppliedMigrationsAsync()).Last());

        // Same counts, and every row and column that existed before is unchanged.
        Assert.Equal(statementsBefore, await RowsAsync(context, StatementRows));
        Assert.Equal(discussionsBefore, await RowsAsync(context, DiscussionRows));
        Assert.Equal(tagsBefore, await RowsAsync(context, TopicTagRows));
        Assert.Equal(0, await ScalarAsync(context, "SELECT count(*)::int AS \"Value\" FROM statements WHERE name IS NOT NULL"));

        // Read through the store: each group keeps its type and has no name; other kinds have no name either.
        var store = new PostgresDiscussionStore(database.ContextFactory);
        var stored = await store.GetDiscussionAsync(data.DiscussionId);
        Assert.NotNull(stored);
        Assert.Equal(["work.remote", "management", "productivity"], stored.TopicTags);
        Assert.Equal(data.StatementIds, stored.Statements.Select(s => s.Id));
        Assert.All(stored.Statements, s => Assert.Null(s.Name));
        foreach (var (groupId, groupType) in data.GroupTypes)
        {
            var group = stored.Statements.Single(s => s.Id == groupId);
            Assert.Equal(StatementKind.SupportGroup, group.Kind);
            Assert.Equal(groupType, group.GroupType);
        }

        Assert.Equal(data.EditedAt, stored.Statements.Single(s => s.Id == data.EditedSupportId).EditedAt);
        Assert.Equal(["single"], (await store.GetDiscussionAsync(data.OtherDiscussionId))!.TopicTags);

        // A migrated, unnamed group can be named (UC-3 A3) without touching its type or any other statement.
        var unnamed = data.GroupTypes[0].Id;
        var editedAt = DateTimeOffset.UtcNow;
        editedAt = editedAt.AddTicks(-(editedAt.UtcTicks % 10));
        await store.UpdateSupportGroupAsync(data.DiscussionId, unnamed, "Named after the migration", null, editedAt);
        var named = await store.GetDiscussionAsync(data.DiscussionId);
        Assert.Equal(
            stored.Statements.Select(s => s.Id == unnamed ? s with { Name = "Named after the migration", EditedAt = editedAt } : s),
            named!.Statements);
    }

    /// <summary>
    /// The schema before this change, holding a group of each type, a group with no supports, a nested group, a
    /// sub-argument group, supporting statements, counters, rebuttals, an illustration, a statement with <c>edited_at</c>
    /// set, and a discussion with several topic tags (plus a second discussion with one tag).
    /// </summary>
    private static async Task<PreChangeData> InsertPreChangeDataAsync(DiscussionDbContext context)
    {
        var discussionId = Guid.NewGuid();
        var otherDiscussionId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);
        var editedAt = new DateTimeOffset(2026, 11, 3, 10, 15, 0, TimeSpan.Zero);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO discussions (id, title, created_at) VALUES ({discussionId}, 'Stored before groups had names', {createdAt})");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO discussions (id, title, created_at) VALUES ({otherDiscussionId}, 'Another discussion', {createdAt})");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO discussion_topic_tags (discussion_id, tag, position) VALUES ({discussionId}, 'work.remote', 0), ({discussionId}, 'management', 1), ({discussionId}, 'productivity', 2), ({otherDiscussionId}, 'single', 0)");

        var ids = new List<Guid>();
        async Task<Guid> InsertAsync(Guid discussion, Guid? parentId, string kind, string? text, string? groupType, DateTimeOffset? edited = null)
        {
            var id = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO statements (id, discussion_id, parent_id, kind, text, group_type, created_at, edited_at) VALUES ({id}, {discussion}, {parentId}, {kind}, {text}, {groupType}, {createdAt}, {edited})");
            if (discussion == discussionId)
            {
                ids.Add(id);
            }

            return id;
        }

        var thesis = await InsertAsync(discussionId, null, "Thesis", "Remote work boosts productivity", null);
        var allRequired = await InsertAsync(discussionId, thesis, "SupportGroup", null, "AllRequired");
        var editedSupport = await InsertAsync(discussionId, allRequired, "SupportingStatement", "Fewer interruptions", null, editedAt);
        var counter = await InsertAsync(discussionId, editedSupport, "CounterArgument", "Home has interruptions too", null);
        await InsertAsync(discussionId, counter, "Rebuttal", "A door closes", null);
        var anySufficient = await InsertAsync(discussionId, thesis, "SupportGroup", null, "AnySufficient");
        await InsertAsync(discussionId, anySufficient, "SupportingStatement", "No commute", null);
        var nested = await InsertAsync(discussionId, allRequired, "SupportGroup", null, "AnySufficient");
        await InsertAsync(discussionId, nested, "SupportingStatement", "Focus time", null);
        var subArgument = await InsertAsync(discussionId, editedSupport, "SupportGroup", null, "AllRequired");
        await InsertAsync(discussionId, subArgument, "SupportingStatement", "Measured in a study", null);
        var empty = await InsertAsync(discussionId, thesis, "SupportGroup", null, "AllRequired");
        await InsertAsync(discussionId, thesis, "Illustration", "A team that went remote", null);
        var thesisCounter = await InsertAsync(discussionId, thesis, "CounterArgument", "Collaboration suffers", null);
        await InsertAsync(discussionId, thesisCounter, "Rebuttal", "Tools help", null);
        await InsertAsync(discussionId, thesis, "CounterArgument", "Loneliness", null);
        await InsertAsync(otherDiscussionId, null, "Thesis", "Another thesis", null);

        return new PreChangeData(
            discussionId,
            otherDiscussionId,
            ids,
            [
                (allRequired, SupportGroupType.AllRequired),
                (anySufficient, SupportGroupType.AnySufficient),
                (nested, SupportGroupType.AnySufficient),
                (subArgument, SupportGroupType.AllRequired),
                (empty, SupportGroupType.AllRequired),
            ],
            editedSupport,
            editedAt);
    }

    /// <summary>The rows of a query returning one text column named <c>Value</c>, sorted so they compare in full.</summary>
    internal static async Task<List<string>> RowsAsync(DiscussionDbContext context, string sql)
    {
        var rows = await context.Database.SqlQueryRaw<string>(sql).ToListAsync();
        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    internal static async Task<int> ScalarAsync(DiscussionDbContext context, string sql) =>
        (await context.Database.SqlQueryRaw<int>(sql).ToListAsync()).Single();

    private sealed record PreChangeData(
        Guid DiscussionId,
        Guid OtherDiscussionId,
        IReadOnlyList<Guid> StatementIds,
        IReadOnlyList<(Guid Id, SupportGroupType GroupType)> GroupTypes,
        Guid EditedSupportId,
        DateTimeOffset EditedAt);
}

/// <summary>
/// nested-named-groups AC-3 (UC-3 "Database has existing groups?" no): the <c>NamedSupportGroups</c> migration applies
/// cleanly to a database with no data and adds a nullable <c>name</c> column of at most 100 characters.
/// </summary>
public sealed class PostgresGroupNameMigrationOnEmptyDatabaseTests(UnmigratedPostgresDatabaseFixture database)
    : IClassFixture<UnmigratedPostgresDatabaseFixture>
{
    [SkippableFact]
    public async Task The_migration_applies_cleanly_to_an_empty_database()
    {
        Skip.If(database.SkipReason is not null, database.SkipReason);
        await using var context = await database.ContextFactory.CreateDbContextAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PostgresGroupNameMigrationTests.MultipleTopicTags);

        await migrator.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(PostgresGroupNameMigrationTests.NamedSupportGroups, (await context.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(0, await PostgresGroupNameMigrationTests.ScalarAsync(context, "SELECT count(*)::int AS \"Value\" FROM statements"));
        Assert.Equal(0, await PostgresGroupNameMigrationTests.ScalarAsync(context, "SELECT count(*)::int AS \"Value\" FROM discussions"));
        Assert.Equal(
            ["{\"is_nullable\":\"YES\",\"character_maximum_length\":100}"],
            await PostgresGroupNameMigrationTests.RowsAsync(
                context,
                "SELECT row_to_json(c)::text AS \"Value\" FROM (SELECT is_nullable, character_maximum_length FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'statements' AND column_name = 'name') c"));
    }
}
