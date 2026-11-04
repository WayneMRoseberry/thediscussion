namespace TheDiscussion.Core.Storage;

/// <summary>The referenced discussion does not exist in storage.</summary>
public sealed class DiscussionNotFoundException(Guid discussionId)
    : Exception($"Discussion '{discussionId}' was not found.")
{
    public Guid DiscussionId { get; } = discussionId;
}

/// <summary>The referenced parent statement does not exist in the discussion.</summary>
public sealed class ParentStatementNotFoundException(Guid discussionId, Guid parentId)
    : Exception($"Statement '{parentId}' was not found in discussion '{discussionId}'.")
{
    public Guid DiscussionId { get; } = discussionId;

    public Guid ParentId { get; } = parentId;
}
