namespace TheDiscussion.Core.Discussions;

/// <summary>
/// The request field names that validation errors are keyed by. They are the API's camelCase JSON names, so the API reads
/// request fields by these names and every <see cref="ValidationErrors"/> key comes from here.
/// </summary>
public static class RequestFields
{
    /// <summary>The whole request body, for errors that belong to no single field.</summary>
    public const string Body = "body";

    public const string Title = "title";

    public const string TopicTags = "topicTags";

    public const string Thesis = "thesis";

    public const string Tag = "tag";

    public const string Kind = "kind";

    public const string ParentId = "parentId";

    public const string Text = "text";

    public const string Name = "name";

    public const string GroupType = "groupType";

    public const string StatementId = "statementId";
}
