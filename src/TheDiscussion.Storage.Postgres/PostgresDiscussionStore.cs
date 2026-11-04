using Microsoft.EntityFrameworkCore;
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

        // Statements are never deleted in this version, so checking before inserting cannot race with a delete.
        // The foreign keys remain the backstop for integrity.
        if (!await context.Discussions.AnyAsync(d => d.Id == statement.DiscussionId, cancellationToken))
        {
            throw new DiscussionNotFoundException(statement.DiscussionId);
        }

        if (!await context.Statements.AnyAsync(s => s.Id == parentId && s.DiscussionId == statement.DiscussionId, cancellationToken))
        {
            throw new ParentStatementNotFoundException(statement.DiscussionId, parentId);
        }

        context.Statements.Add(StatementEntity.From(statement));
        await context.SaveChangesAsync(cancellationToken);
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
}
