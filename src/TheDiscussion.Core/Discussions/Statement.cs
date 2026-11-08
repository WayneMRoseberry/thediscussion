namespace TheDiscussion.Core.Discussions;

/// <summary>
/// One node of a discussion's argument tree. Every statement except the thesis has a parent.
/// Support groups have a <see cref="GroupType"/>, a <see cref="Name"/> and no text; every other kind has text and no name.
/// New groups always get a name (1 to <see cref="DiscussionRules.GroupNameMaxLength"/> characters, trimmed); groups stored
/// before names existed have a <c>null</c> name until one is set.
/// <see cref="EditedAt"/> is <c>null</c> until the statement is first edited; editing never changes the kind or parent.
/// </summary>
public sealed record Statement(
    Guid Id,
    Guid DiscussionId,
    StatementKind Kind,
    Guid? ParentId,
    string? Text,
    SupportGroupType? GroupType,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt = null,
    string? Name = null);
