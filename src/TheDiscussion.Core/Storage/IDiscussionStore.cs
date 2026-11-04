using TheDiscussion.Core.Discussions;

namespace TheDiscussion.Core.Storage;

/// <summary>
/// Storage tier contract. Implementations live in their own provider projects and are chosen by the
/// API's composition root. Implementations persist exactly what they are given: ids, timestamps and
/// validation of statement kinds are the application tier's job. Every implementation must pass the
/// shared contract test suite (tests/TheDiscussion.Storage.Tests).
/// </summary>
public interface IDiscussionStore
{
    /// <summary>A short name for the provider, reported by the health endpoint.</summary>
    string ProviderName { get; }

    /// <summary>Stores a new discussion together with its thesis, atomically.</summary>
    Task CreateDiscussionAsync(DiscussionSummary discussion, Statement thesis, CancellationToken cancellationToken = default);

    /// <summary>All discussions, most recently created first.</summary>
    Task<IReadOnlyList<DiscussionSummary>> ListDiscussionsAsync(CancellationToken cancellationToken = default);

    /// <summary>The discussion with all its statements in creation order, or <c>null</c> when it does not exist.</summary>
    Task<Discussion?> GetDiscussionAsync(Guid discussionId, CancellationToken cancellationToken = default);

    /// <summary>Adds a statement to an existing discussion.</summary>
    /// <exception cref="DiscussionNotFoundException">The statement's discussion does not exist.</exception>
    /// <exception cref="ParentStatementNotFoundException">The parent is not a statement of that discussion.</exception>
    Task AddStatementAsync(Statement statement, CancellationToken cancellationToken = default);

    /// <summary>Whether the storage tier can currently be reached.</summary>
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);
}
