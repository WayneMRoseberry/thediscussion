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
            TopicTag = discussion.TopicTag,
            CreatedAt = discussion.CreatedAt,
            Statements = [StatementEntity.From(thesis)],
        });

        // A single SaveChanges call runs in one transaction: the discussion and its thesis are stored together.
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DiscussionSummary>> ListDiscussionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Discussions
            .AsNoTracking()
            .OrderByDescending(d => d.Sequence)
            .Select(d => new DiscussionSummary(d.Id, d.Title, d.TopicTag, d.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<Discussion?> GetDiscussionAsync(Guid discussionId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var discussion = await context.Discussions
            .AsNoTracking()
            .Where(d => d.Id == discussionId)
            .Select(d => new { d.Id, d.Title, d.TopicTag, d.CreatedAt })
            .SingleOrDefaultAsync(cancellationToken);
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
            discussion.TopicTag,
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

    public async Task<bool> UpdateDiscussionAsync(Guid discussionId, string title, string topicTag, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var updated = await context.Discussions
            .Where(d => d.Id == discussionId)
            .ExecuteUpdateAsync(
                set => set.SetProperty(d => d.Title, title).SetProperty(d => d.TopicTag, topicTag),
                cancellationToken);
        return updated > 0;
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

        // One DELETE of the target; the self-referencing foreign key cascades to the whole subtree within the same
        // statement, so the delete is atomic. The kind filter keeps the thesis safe even if the row changed meanwhile.
        var deleted = kind is null
            ? 0
            : await context.Statements
                .Where(s => s.Id == statementId && s.DiscussionId == discussionId && s.Kind != StatementKind.Thesis)
                .ExecuteDeleteAsync(cancellationToken);
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
