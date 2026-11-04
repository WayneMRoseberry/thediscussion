using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Storage;

namespace TheDiscussion.Storage.InMemory;

/// <summary>
/// Thread-safe, process-local storage. Used by tests and as the local default. Data is lost on restart.
/// </summary>
public sealed class InMemoryDiscussionStore : IDiscussionStore
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, StoredDiscussion> discussions = [];
    private long nextSequence;

    public string ProviderName => "InMemory";

    public Task CreateDiscussionAsync(DiscussionSummary discussion, Statement thesis, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discussion);
        ArgumentNullException.ThrowIfNull(thesis);
        if (thesis.Kind != StatementKind.Thesis || thesis.ParentId is not null || thesis.DiscussionId != discussion.Id)
        {
            throw new ArgumentException("The thesis must be a parentless Thesis statement of the new discussion.", nameof(thesis));
        }

        lock (gate)
        {
            if (discussions.ContainsKey(discussion.Id))
            {
                throw new InvalidOperationException($"Discussion '{discussion.Id}' already exists.");
            }

            var stored = new StoredDiscussion(discussion, nextSequence++);
            stored.Statements.Add(thesis);
            stored.StatementIds.Add(thesis.Id);
            discussions.Add(discussion.Id, stored);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DiscussionSummary>> ListDiscussionsAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<DiscussionSummary> result = discussions.Values
                .OrderByDescending(stored => stored.Sequence)
                .Select(stored => stored.Summary)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<Discussion?> GetDiscussionAsync(Guid discussionId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!discussions.TryGetValue(discussionId, out var stored))
            {
                return Task.FromResult<Discussion?>(null);
            }

            var summary = stored.Summary;
            return Task.FromResult<Discussion?>(
                new Discussion(summary.Id, summary.Title, summary.TopicTag, summary.CreatedAt, stored.Statements.ToList()));
        }
    }

    public Task AddStatementAsync(Statement statement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statement);
        if (statement.ParentId is not { } parentId || statement.Kind == StatementKind.Thesis)
        {
            throw new ArgumentException("Only non-thesis statements with a parent can be added.", nameof(statement));
        }

        lock (gate)
        {
            if (!discussions.TryGetValue(statement.DiscussionId, out var stored))
            {
                throw new DiscussionNotFoundException(statement.DiscussionId);
            }

            if (!stored.StatementIds.Contains(parentId))
            {
                throw new ParentStatementNotFoundException(statement.DiscussionId, parentId);
            }

            if (!stored.StatementIds.Add(statement.Id))
            {
                throw new InvalidOperationException($"Statement '{statement.Id}' already exists.");
            }

            stored.Statements.Add(statement);
        }

        return Task.CompletedTask;
    }

    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    private sealed class StoredDiscussion(DiscussionSummary summary, long sequence)
    {
        public DiscussionSummary Summary { get; } = summary;

        public long Sequence { get; } = sequence;

        public List<Statement> Statements { get; } = [];

        public HashSet<Guid> StatementIds { get; } = [];
    }
}
