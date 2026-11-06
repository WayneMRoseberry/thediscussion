namespace TheDiscussion.Core.Discussions;

/// <summary>
/// Summary of a discussion, without its statements. <see cref="TopicTags"/> holds 1 to 5 distinct tags in the order
/// they were given. Equality compares the tags by value.
/// </summary>
public sealed record DiscussionSummary(Guid Id, string Title, IReadOnlyList<string> TopicTags, DateTimeOffset CreatedAt)
{
    public bool Equals(DiscussionSummary? other) =>
        other is not null
        && Id == other.Id
        && Title == other.Title
        && CreatedAt == other.CreatedAt
        && TopicTags.SequenceEqual(other.TopicTags, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Id, Title, CreatedAt);
}

/// <summary>
/// A discussion with its full argument tree. <see cref="Statements"/> is a flat list in creation order;
/// the tree is given by each statement's <see cref="Statement.ParentId"/>. Exactly one statement is the thesis.
/// </summary>
public sealed record Discussion(
    Guid Id,
    string Title,
    IReadOnlyList<string> TopicTags,
    DateTimeOffset CreatedAt,
    IReadOnlyList<Statement> Statements)
{
    public Statement Thesis => Statements.Single(statement => statement.Kind == StatementKind.Thesis);

    public DiscussionSummary ToSummary() => new(Id, Title, TopicTags, CreatedAt);
}
