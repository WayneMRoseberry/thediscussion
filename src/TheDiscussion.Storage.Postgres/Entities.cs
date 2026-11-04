using TheDiscussion.Core.Discussions;

namespace TheDiscussion.Storage.Postgres;

internal sealed class DiscussionEntity
{
    public Guid Id { get; set; }

    public required string Title { get; set; }

    public required string TopicTag { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Database-generated insertion order, used for a stable listing order.</summary>
    public long Sequence { get; set; }

    public List<StatementEntity> Statements { get; set; } = [];
}

internal sealed class StatementEntity
{
    public Guid Id { get; set; }

    public Guid DiscussionId { get; set; }

    public Guid? ParentId { get; set; }

    public StatementKind Kind { get; set; }

    public string? Text { get; set; }

    public SupportGroupType? GroupType { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Database-generated insertion order, used to return statements in creation order.</summary>
    public long Sequence { get; set; }

    public static StatementEntity From(Statement statement) => new()
    {
        Id = statement.Id,
        DiscussionId = statement.DiscussionId,
        ParentId = statement.ParentId,
        Kind = statement.Kind,
        Text = statement.Text,
        GroupType = statement.GroupType,
        CreatedAt = statement.CreatedAt,
    };

    public Statement ToStatement() => new(Id, DiscussionId, Kind, ParentId, Text, GroupType, CreatedAt);
}
