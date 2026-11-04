namespace TheDiscussion.Core.Discussions;

/// <summary>
/// One node of a discussion's argument tree. Every statement except the thesis has a parent.
/// Support groups have a <see cref="GroupType"/> and no text; every other kind has text.
/// </summary>
public sealed record Statement(
    Guid Id,
    Guid DiscussionId,
    StatementKind Kind,
    Guid? ParentId,
    string? Text,
    SupportGroupType? GroupType,
    DateTimeOffset CreatedAt);
