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

    /// <summary>
    /// The discussions with at least one tag equal to <paramref name="tag"/> or starting with <paramref name="tag"/>
    /// followed by a dot (a sub-tag), each listed once, most recently created first. Only whole segments match:
    /// <c>wor</c> does not match <c>work</c>. The caller passes a well-formed tag.
    /// </summary>
    Task<IReadOnlyList<DiscussionSummary>> ListDiscussionsUnderTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>The discussion with all its statements in creation order, or <c>null</c> when it does not exist.</summary>
    Task<Discussion?> GetDiscussionAsync(Guid discussionId, CancellationToken cancellationToken = default);

    /// <summary>Adds a statement to an existing discussion.</summary>
    /// <exception cref="DiscussionNotFoundException">The statement's discussion does not exist.</exception>
    /// <exception cref="ParentStatementNotFoundException">The parent is not a statement of that discussion.</exception>
    Task AddStatementAsync(Statement statement, CancellationToken cancellationToken = default);

    /// <summary>Replaces a discussion's title and topic tags (last write wins). <paramref name="topicTags"/> are 1 to 5 distinct tags, in order.</summary>
    /// <returns><c>false</c> when the discussion does not exist; nothing is changed then.</returns>
    Task<bool> UpdateDiscussionAsync(Guid discussionId, string title, IReadOnlyList<string> topicTags, CancellationToken cancellationToken = default);

    /// <summary>Removes a discussion and all its statements, atomically.</summary>
    /// <returns><c>false</c> when the discussion does not exist.</returns>
    Task<bool> DeleteDiscussionAsync(Guid discussionId, CancellationToken cancellationToken = default);

    /// <summary>Replaces a statement's text and records when it was edited (last write wins). Kind and parent never change.</summary>
    /// <exception cref="DiscussionNotFoundException">The discussion does not exist.</exception>
    /// <exception cref="StatementNotFoundException">The statement is not part of that discussion.</exception>
    Task UpdateStatementTextAsync(Guid discussionId, Guid statementId, string text, DateTimeOffset editedAt, CancellationToken cancellationToken = default);

    /// <summary>Removes a statement and every statement beneath it, at any depth, in one atomic operation.</summary>
    /// <exception cref="DiscussionNotFoundException">The discussion does not exist.</exception>
    /// <exception cref="StatementNotFoundException">The statement is not part of that discussion.</exception>
    /// <exception cref="InvalidOperationException">The statement is the thesis (delete the discussion instead).</exception>
    Task DeleteStatementAsync(Guid discussionId, Guid statementId, CancellationToken cancellationToken = default);

    /// <summary>Whether the storage tier can currently be reached.</summary>
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);
}
