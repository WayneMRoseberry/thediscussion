using Microsoft.EntityFrameworkCore;
using Npgsql;
using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Storage;

namespace TheDiscussion.Storage.Postgres;

/// <summary>PostgreSQL implementation of <see cref="IDiscussionStore"/>. Uses a short-lived context per operation.</summary>
public sealed class PostgresDiscussionStore(IDbContextFactory<DiscussionDbContext> contextFactory) : IDiscussionStore
{
    public string ProviderName => "Postgres";

    public async Task CreateDiscussionAsync(DiscussionSummary discussion, Statement thesis, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discussion);
        ArgumentNullException.ThrowIfNull(thesis);
        if (thesis.Kind != StatementKind.Thesis || thesis.ParentId is not null || thesis.DiscussionId != discussion.Id)
        {
            throw new ArgumentException("The thesis must be a parentless Thesis statement of the new discussion.", nameof(thesis));
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Discussions.Add(new DiscussionEntity
        {
            Id = discussion.Id,
            Title = discussion.Title,
            CreatedAt = discussion.CreatedAt,
            TopicTags = DiscussionTopicTagEntity.From(discussion.Id, discussion.TopicTags),
            Statements = [StatementEntity.From(thesis)],
        });

        // A single SaveChanges call runs in one transaction: the discussion, its tags and its thesis are stored together.
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DiscussionSummary>> ListDiscussionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await SummariesAsync(context.Discussions, cancellationToken);
    }

    public async Task<IReadOnlyList<DiscussionSummary>> ListDiscussionsUnderTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);
        var subTagPrefix = tag + ".";
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // EXISTS over the discussion's tags, so a discussion with several matching tags is listed once. StartsWith
        // becomes an escaped LIKE 'tag.%', which the text_pattern_ops index on discussion_topic_tags.tag serves.
        return await SummariesAsync(
            context.Discussions.Where(d => d.TopicTags.Any(t => t.Tag == tag || t.Tag.StartsWith(subTagPrefix))),
            cancellationToken);
    }

    public async Task<Discussion?> GetDiscussionAsync(Guid discussionId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var discussion = (await SummariesAsync(context.Discussions.Where(d => d.Id == discussionId), cancellationToken)).SingleOrDefault();
        if (discussion is null)
        {
            return null;
        }

        var statements = await context.Statements
            .AsNoTracking()
            .Where(s => s.DiscussionId == discussionId)
            .OrderBy(s => s.Sequence)
            .ToListAsync(cancellationToken);

        return new Discussion(
            discussion.Id,
            discussion.Title,
            discussion.TopicTags,
            discussion.CreatedAt,
            statements.Select(s => s.ToStatement()).ToList());
    }

    public async Task AddStatementAsync(Statement statement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statement);
        if (statement.ParentId is not { } parentId || statement.Kind == StatementKind.Thesis)
        {
            throw new ArgumentException("Only non-thesis statements with a parent can be added.", nameof(statement));
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (!await context.Discussions.AnyAsync(d => d.Id == statement.DiscussionId, cancellationToken))
        {
            throw new DiscussionNotFoundException(statement.DiscussionId);
        }

        if (!await context.Statements.AnyAsync(s => s.Id == parentId && s.DiscussionId == statement.DiscussionId, cancellationToken))
        {
            throw new ParentStatementNotFoundException(statement.DiscussionId, parentId);
        }

        context.Statements.Add(StatementEntity.From(statement));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException failed) when (failed.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } violation)
        {
            // The discussion or the parent was deleted between the checks above and the insert; the foreign keys caught it.
            if (violation.ConstraintName == DiscussionForeignKey)
            {
                throw new DiscussionNotFoundException(statement.DiscussionId);
            }

            throw new ParentStatementNotFoundException(statement.DiscussionId, parentId);
        }
    }

    public async Task<bool> UpdateDiscussionAsync(
        Guid discussionId,
        string title,
        IReadOnlyList<string> topicTags,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topicTags);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Updating the discussion row first locks it, so concurrent updates of one discussion run one after the other
        // (last write wins) and never interleave their tag replacements.
        var updated = await context.Discussions
            .Where(d => d.Id == discussionId)
            .ExecuteUpdateAsync(set => set.SetProperty(d => d.Title, title), cancellationToken);
        if (updated == 0)
        {
            return false;
        }

        await context.TopicTags.Where(t => t.DiscussionId == discussionId).ExecuteDeleteAsync(cancellationToken);
        context.TopicTags.AddRange(DiscussionTopicTagEntity.From(discussionId, topicTags));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteDiscussionAsync(Guid discussionId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // One DELETE; the foreign keys cascade to every statement of the discussion within the same statement.
        var deleted = await context.Discussions.Where(d => d.Id == discussionId).ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    public async Task UpdateStatementTextAsync(
        Guid discussionId,
        Guid statementId,
        string text,
        DateTimeOffset editedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var updated = await context.Statements
            .Where(s => s.Id == statementId && s.DiscussionId == discussionId)
            .ExecuteUpdateAsync(
                set => set.SetProperty(s => s.Text, text).SetProperty(s => s.EditedAt, editedAt),
                cancellationToken);
        if (updated == 0)
        {
            throw await NotFoundAsync(context, discussionId, statementId, cancellationToken);
        }
    }

    public async Task UpdateSupportGroupAsync(
        Guid discussionId,
        Guid statementId,
        string? name,
        SupportGroupType? groupType,
        DateTimeOffset editedAt,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var group = await context.Statements
            .SingleOrDefaultAsync(s => s.Id == statementId && s.DiscussionId == discussionId, cancellationToken)
            ?? throw await NotFoundAsync(context, discussionId, statementId, cancellationToken);
        if (group.Kind != StatementKind.SupportGroup)
        {
            throw new InvalidOperationException($"Statement '{statementId}' is not a support group.");
        }

        if (name is not null)
        {
            group.Name = name;
        }

        if (groupType is not null)
        {
            group.GroupType = groupType;
        }

        group.EditedAt = editedAt;
        try
        {
            // One UPDATE of only the changed columns, so the name, the type and the edit time change together.
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The group (or its discussion) was deleted between the read above and the update.
            throw await NotFoundAsync(context, discussionId, statementId, cancellationToken);
        }
    }

    public async Task DeleteStatementAsync(Guid discussionId, Guid statementId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var kind = await context.Statements
            .Where(s => s.Id == statementId && s.DiscussionId == discussionId)
            .Select(s => (StatementKind?)s.Kind)
            .SingleOrDefaultAsync(cancellationToken);
        if (kind == StatementKind.Thesis)
        {
            throw new InvalidOperationException("The thesis cannot be deleted on its own; delete the discussion instead.");
        }

        // One SQL statement, so the delete is atomic: a recursive CTE collects the target and its whole subtree, at any
        // depth, and a single DELETE removes all of it. Deleting only the target and leaving the rest to the
        // self-referencing ON DELETE CASCADE would fire one nested cascade per level, which PostgreSQL runs recursively
        // on its own stack, so a deep enough tree could fail with "stack depth limit exceeded". Here the cascade still
        // fires but finds nothing left beneath any row. The kind filter keeps the thesis safe even if the row changed
        // meanwhile; UNION (not UNION ALL) would stop even on a malformed parent cycle.
        var thesisKind = nameof(StatementKind.Thesis);
        var deleted = kind is null
            ? 0
            : await context.Database.ExecuteSqlAsync(
                $"""
                WITH RECURSIVE subtree (id) AS (
                    SELECT id FROM statements
                    WHERE id = {statementId} AND discussion_id = {discussionId} AND kind <> {thesisKind}
                    UNION
                    SELECT child.id FROM statements AS child JOIN subtree ON child.parent_id = subtree.id
                )
                DELETE FROM statements WHERE id IN (SELECT id FROM subtree)
                """,
                cancellationToken);
        if (deleted == 0)
        {
            throw await NotFoundAsync(context, discussionId, statementId, cancellationToken);
        }
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Summaries of the given discussions, most recently created first, each with its tags in their stored order.</summary>
    private static async Task<IReadOnlyList<DiscussionSummary>> SummariesAsync(
        IQueryable<DiscussionEntity> discussions,
        CancellationToken cancellationToken)
    {
        var rows = await discussions
            .AsNoTracking()
            .OrderByDescending(d => d.Sequence)
            .Select(d => new
            {
                d.Id,
                d.Title,
                d.CreatedAt,
                Tags = d.TopicTags.OrderBy(t => t.Position).Select(t => t.Tag).ToList(),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new DiscussionSummary(row.Id, row.Title, row.Tags, row.CreatedAt)).ToList();
    }

    /// <summary>Name of the statements-to-discussions foreign key (see the migrations).</summary>
    private const string DiscussionForeignKey = "FK_statements_discussions_discussion_id";

    private static async Task<Exception> NotFoundAsync(
        DiscussionDbContext context,
        Guid discussionId,
        Guid statementId,
        CancellationToken cancellationToken) =>
        await context.Discussions.AnyAsync(d => d.Id == discussionId, cancellationToken)
            ? new StatementNotFoundException(discussionId, statementId)
            : new DiscussionNotFoundException(discussionId);
}
