namespace TheDiscussion.Core.Discussions;

/// <summary>Summary of a discussion, without its statements.</summary>
public sealed record DiscussionSummary(Guid Id, string Title, string TopicTag, DateTimeOffset CreatedAt);

/// <summary>
/// A discussion with its full argument tree. <see cref="Statements"/> is a flat list in creation order;
/// the tree is given by each statement's <see cref="Statement.ParentId"/>. Exactly one statement is the thesis.
/// </summary>
public sealed record Discussion(
    Guid Id,
    string Title,
    string TopicTag,
    DateTimeOffset CreatedAt,
    IReadOnlyList<Statement> Statements)
{
    public Statement Thesis => Statements.Single(statement => statement.Kind == StatementKind.Thesis);

    public DiscussionSummary ToSummary() => new(Id, Title, TopicTag, CreatedAt);
}
