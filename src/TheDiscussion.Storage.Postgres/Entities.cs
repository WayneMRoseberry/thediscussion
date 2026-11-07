using TheDiscussion.Core.Discussions;

namespace TheDiscussion.Storage.Postgres;

internal sealed class DiscussionEntity
{
    public Guid Id { get; set; }

    public required string Title { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Database-generated insertion order, used for a stable listing order.</summary>
    public long Sequence { get; set; }

    public List<DiscussionTopicTagEntity> TopicTags { get; set; } = [];

    public List<StatementEntity> Statements { get; set; } = [];
}

/// <summary>One of a discussion's 1 to 5 distinct topic tags. <see cref="Position"/> keeps the order they were given in.</summary>
internal sealed class DiscussionTopicTagEntity
{
    public Guid DiscussionId { get; set; }

    public required string Tag { get; set; }

    public int Position { get; set; }

    public static List<DiscussionTopicTagEntity> From(Guid discussionId, IEnumerable<string> tags) =>
        tags.Select((tag, position) => new DiscussionTopicTagEntity { DiscussionId = discussionId, Tag = tag, Position = position }).ToList();
}

internal sealed class StatementEntity
{
    public Guid Id { get; set; }

    public Guid DiscussionId { get; set; }

    public Guid? ParentId { get; set; }

    public StatementKind Kind { get; set; }

    public string? Text { get; set; }

    public SupportGroupType? GroupType { get; set; }

    /// <summary>A support group's name; <c>null</c> for other kinds and for groups stored before groups had names.</summary>
    public string? Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? EditedAt { get; set; }

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
        Name = statement.Name,
        CreatedAt = statement.CreatedAt,
        EditedAt = statement.EditedAt,
    };

    public Statement ToStatement() => new(Id, DiscussionId, Kind, ParentId, Text, GroupType, CreatedAt, EditedAt, Name);
}
