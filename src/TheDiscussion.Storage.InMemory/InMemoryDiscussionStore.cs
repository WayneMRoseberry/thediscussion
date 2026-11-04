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

            var stored = new StoredDiscussion(discussion with { TopicTags = [.. discussion.TopicTags] }, nextSequence++);
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

    public Task<IReadOnlyList<DiscussionSummary>> ListDiscussionsUnderTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);
        lock (gate)
        {
            IReadOnlyList<DiscussionSummary> result = discussions.Values
                .Where(stored => stored.Summary.TopicTags.Any(topicTag => DiscussionRules.IsAtOrUnder(topicTag, tag)))
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
                new Discussion(summary.Id, summary.Title, summary.TopicTags, summary.CreatedAt, stored.Statements.ToList()));
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

    public Task<bool> UpdateDiscussionAsync(Guid discussionId, string title, IReadOnlyList<string> topicTags, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!discussions.TryGetValue(discussionId, out var stored))
            {
                return Task.FromResult(false);
            }

            stored.Summary = stored.Summary with { Title = title, TopicTags = [.. topicTags] };
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeleteDiscussionAsync(Guid discussionId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(discussions.Remove(discussionId));
        }
    }

    public Task UpdateStatementTextAsync(
        Guid discussionId,
        Guid statementId,
        string text,
        DateTimeOffset editedAt,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var stored = Find(discussionId);
            var index = stored.Statements.FindIndex(statement => statement.Id == statementId);
            if (index < 0)
            {
                throw new StatementNotFoundException(discussionId, statementId);
            }

            stored.Statements[index] = stored.Statements[index] with { Text = text, EditedAt = editedAt };
        }

        return Task.CompletedTask;
    }

    public Task DeleteStatementAsync(Guid discussionId, Guid statementId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var stored = Find(discussionId);
            var target = stored.Statements.Find(statement => statement.Id == statementId)
                ?? throw new StatementNotFoundException(discussionId, statementId);
            if (target.Kind == StatementKind.Thesis)
            {
                throw new InvalidOperationException("The thesis cannot be deleted on its own; delete the discussion instead.");
            }

            // Collect the subtree first, then remove it in one step under the lock, so readers never see part of it.
            var doomed = new HashSet<Guid> { statementId };
            bool grew;
            do
            {
                grew = false;
                foreach (var statement in stored.Statements)
                {
                    if (statement.ParentId is { } parentId && doomed.Contains(parentId) && doomed.Add(statement.Id))
                    {
                        grew = true;
                    }
                }
            }
            while (grew);

            stored.Statements.RemoveAll(statement => doomed.Contains(statement.Id));
            stored.StatementIds.ExceptWith(doomed);
        }

        return Task.CompletedTask;
    }

    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <summary>The stored discussion; call with <see cref="gate"/> held.</summary>
    private StoredDiscussion Find(Guid discussionId) =>
        discussions.TryGetValue(discussionId, out var stored) ? stored : throw new DiscussionNotFoundException(discussionId);

    private sealed class StoredDiscussion(DiscussionSummary summary, long sequence)
    {
        public DiscussionSummary Summary { get; set; } = summary;

        public long Sequence { get; } = sequence;

        public List<Statement> Statements { get; } = [];

        public HashSet<Guid> StatementIds { get; } = [];
    }
}
