using System.Text.Json.Serialization;
using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Status;

namespace TheDiscussion.Api.Discussions;

/// <summary><c>POST /api/v1/discussions</c> body. <c>topicTags</c> holds 1 to 5 distinct tags, for example <c>["work.remote", "management"]</c>.</summary>
public sealed record CreateDiscussionRequest(string? Title, IReadOnlyList<string?>? TopicTags, string? Thesis);

/// <summary>
/// <c>POST /api/v1/discussions/{id}/statements</c> body. <c>kind</c> is one of <c>supportGroup</c>,
/// <c>supportingStatement</c>, <c>illustration</c>, <c>counterArgument</c>, <c>rebuttal</c>.
/// Support groups take <c>groupType</c> (<c>allRequired</c> or <c>anySufficient</c>) and no text; every other kind takes text.
/// </summary>
public sealed record AddStatementRequest(string? Kind, string? ParentId, string? Text, string? GroupType);

// PATCH bodies are read field by field (DiscussionEndpoints), so a field that is absent can be told apart from one sent as null:
// PATCH /api/v1/discussions/{id}                         { title?, topicTags? }  only the fields sent change
// PATCH /api/v1/discussions/{id}/statements/{statementId} { text }                kind and parent never change

/// <summary>An entry of <c>GET /api/v1/discussions</c>.</summary>
public sealed record DiscussionSummaryResponse(Guid Id, string Title, IReadOnlyList<string> TopicTags, DateTimeOffset CreatedAt)
{
    public static DiscussionSummaryResponse From(DiscussionSummary summary) =>
        new(summary.Id, summary.Title, summary.TopicTags, summary.CreatedAt);
}

/// <summary>
/// <c>GET /api/v1/discussions/{id}</c>: the full argument tree as a flat list in creation order (each statement
/// names its <c>parentId</c>), with status computed on this read.
/// </summary>
public sealed record DiscussionResponse(
    Guid Id,
    string Title,
    IReadOnlyList<string> TopicTags,
    DateTimeOffset CreatedAt,
    ThesisStatus Status,
    Guid ThesisId,
    IReadOnlyList<StatementResponse> Statements)
{
    public static DiscussionResponse From(DiscussionWithStatus value)
    {
        var (discussion, status) = (value.Discussion, value.Status);
        return new DiscussionResponse(
            discussion.Id,
            discussion.Title,
            discussion.TopicTags,
            discussion.CreatedAt,
            status.Thesis,
            discussion.Thesis.Id,
            discussion.Statements.Select(statement => StatementResponse.From(statement, status)).ToList());
    }
}

/// <summary>
/// One statement. <c>stands</c> is present on supporting statements, <c>refuted</c> on counter arguments and
/// rebuttals, <c>holds</c> on non-empty support groups, <c>groupType</c> on support groups, <c>text</c> on every
/// kind except support groups. <c>editedAt</c> is always present: <c>null</c> until the text is first edited.
/// </summary>
public sealed record StatementResponse(
    Guid Id,
    StatementKind Kind,
    Guid? ParentId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SupportGroupType? GroupType,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Stands,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Refuted,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Holds)
{
    public static StatementResponse From(Statement statement, ArgumentStatus status) => new(
        statement.Id,
        statement.Kind,
        statement.ParentId,
        statement.Text,
        statement.GroupType,
        statement.CreatedAt,
        statement.EditedAt,
        status.Stands.TryGetValue(statement.Id, out var stands) ? stands : null,
        status.Refuted.TryGetValue(statement.Id, out var refuted) ? refuted : null,
        status.Holds.TryGetValue(statement.Id, out var holds) ? holds : null);
}
