using TheDiscussion.Core.Status;
using TheDiscussion.Core.Storage;

namespace TheDiscussion.Core.Discussions;

/// <summary>Input for a new discussion. Field names in validation errors: <c>title</c>, <c>topicTag</c>, <c>thesis</c>.</summary>
public sealed record NewDiscussion(string? Title, string? TopicTag, string? Thesis);

/// <summary>
/// Input for a new statement. Field names in validation errors: <c>kind</c>, <c>parentId</c>, <c>text</c>, <c>groupType</c>.
/// A <c>null</c> field means the client did not supply it.
/// </summary>
public sealed record NewStatement(StatementKind? Kind, Guid? ParentId, string? Text, SupportGroupType? GroupType);

/// <summary>A discussion together with its freshly computed status.</summary>
public sealed record DiscussionWithStatus(Discussion Discussion, ArgumentStatus Status);

/// <summary>
/// Application service for discussions: validates input, assigns ids and timestamps, and computes status on
/// every read (status is never stored). Storage-technology independent: it only uses <see cref="IDiscussionStore"/>.
/// </summary>
public sealed class DiscussionService(IDiscussionStore store, TimeProvider clock)
{
    /// <exception cref="DiscussionValidationException">The input is invalid; nothing is stored.</exception>
    public async Task<DiscussionWithStatus> CreateDiscussionAsync(NewDiscussion input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var errors = new ValidationErrors();
        var title = errors.RequireText("title", input.Title, DiscussionRules.TitleMaxLength, "Title");
        var thesisText = errors.RequireText("thesis", input.Thesis, DiscussionRules.StatementTextMaxLength, "Thesis");
        var topicTag = input.TopicTag?.Trim();
        if (string.IsNullOrEmpty(topicTag))
        {
            errors.Add("topicTag", "Topic tag is required.");
        }
        else if (!DiscussionRules.IsValidTopicTag(topicTag))
        {
            errors.Add(
                "topicTag",
                $"Topic tag must be 1 to {DiscussionRules.TopicTagMaxSegments} dot-separated segments of lowercase letters, digits or hyphens, for example 'work.remote'.");
        }

        ThrowIfInvalid(errors);

        var now = Now();
        var discussion = new DiscussionSummary(Guid.NewGuid(), title!, topicTag!, now);
        var thesis = new Statement(Guid.NewGuid(), discussion.Id, StatementKind.Thesis, null, thesisText, null, now);
        await store.CreateDiscussionAsync(discussion, thesis, cancellationToken);

        return WithStatus(new Discussion(discussion.Id, discussion.Title, discussion.TopicTag, discussion.CreatedAt, [thesis]));
    }

    public Task<IReadOnlyList<DiscussionSummary>> ListDiscussionsAsync(CancellationToken cancellationToken = default) =>
        store.ListDiscussionsAsync(cancellationToken);

    /// <returns>The discussion with its status, or <c>null</c> when it does not exist.</returns>
    public async Task<DiscussionWithStatus?> GetDiscussionAsync(Guid discussionId, CancellationToken cancellationToken = default)
    {
        var discussion = await store.GetDiscussionAsync(discussionId, cancellationToken);
        return discussion is null ? null : WithStatus(discussion);
    }

    /// <summary>Adds a statement to a discussion.</summary>
    /// <param name="discussionId">The discussion to add to.</param>
    /// <param name="input">The statement.</param>
    /// <param name="formatErrors">Errors the caller already found while reading the request (for example an unknown kind name).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The stored statement and the discussion's updated status.</returns>
    /// <exception cref="DiscussionNotFoundException">The discussion does not exist.</exception>
    /// <exception cref="DiscussionValidationException">The input is invalid; nothing is stored.</exception>
    public async Task<(Statement Statement, DiscussionWithStatus Discussion)> AddStatementAsync(
        Guid discussionId,
        NewStatement input,
        ValidationErrors? formatErrors = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var discussion = await store.GetDiscussionAsync(discussionId, cancellationToken)
            ?? throw new DiscussionNotFoundException(discussionId);

        var errors = formatErrors ?? new ValidationErrors();
        var statement = Validate(discussion, input, errors);
        ThrowIfInvalid(errors);

        try
        {
            await store.AddStatementAsync(statement!, cancellationToken);
        }
        catch (ParentStatementNotFoundException)
        {
            errors.Add("parentId", $"No statement with id '{input.ParentId}' exists in this discussion.");
            ThrowIfInvalid(errors);
            throw;
        }

        var updated = discussion with { Statements = [.. discussion.Statements, statement!] };
        return (statement!, WithStatus(updated));
    }

    private Statement? Validate(Discussion discussion, NewStatement input, ValidationErrors errors)
    {
        if (input.Kind is null && !errors.Has("kind"))
        {
            errors.Add("kind", "Kind is required.");
        }
        else if (input.Kind == StatementKind.Thesis)
        {
            errors.Add("kind", "A discussion has exactly one thesis, created with the discussion.");
        }

        Statement? parent = null;
        if (input.ParentId is not { } parentId)
        {
            if (!errors.Has("parentId"))
            {
                errors.Add("parentId", "Parent id is required.");
            }
        }
        else
        {
            parent = discussion.Statements.FirstOrDefault(statement => statement.Id == parentId);
            if (parent is null)
            {
                errors.Add("parentId", $"No statement with id '{parentId}' exists in this discussion.");
            }
            else if (input.Kind is { } kind && kind != StatementKind.Thesis && !DiscussionRules.CanAttach(kind, parent.Kind))
            {
                var allowed = string.Join(" or ", DiscussionRules.AllowedParentKinds(kind).Select(DiscussionRules.Describe));
                errors.Add(
                    "parentId",
                    $"{Capitalize(DiscussionRules.Describe(kind))} must target {allowed}; '{parentId}' is {DiscussionRules.Describe(parent.Kind)}.");
            }
        }

        string? text = null;
        if (input.Kind == StatementKind.SupportGroup)
        {
            if (!string.IsNullOrWhiteSpace(input.Text))
            {
                errors.Add("text", "Support groups have no text.");
            }

            if (input.GroupType is null && !errors.Has("groupType"))
            {
                errors.Add("groupType", "Group type is required for a support group: 'allRequired' or 'anySufficient'.");
            }
        }
        else
        {
            text = errors.RequireText("text", input.Text, DiscussionRules.StatementTextMaxLength, "Text");
            if (input.GroupType is not null && input.Kind is not null)
            {
                errors.Add("groupType", "Only support groups have a group type.");
            }
        }

        if (!errors.IsEmpty || input.Kind is not { } validKind || parent is null)
        {
            return null;
        }

        return new Statement(
            Guid.NewGuid(),
            discussion.Id,
            validKind,
            parent.Id,
            text,
            validKind == StatementKind.SupportGroup ? input.GroupType : null,
            Now());
    }

    private static DiscussionWithStatus WithStatus(Discussion discussion) =>
        new(discussion, ArgumentStatusEngine.Compute(discussion.Statements));

    private static void ThrowIfInvalid(ValidationErrors errors)
    {
        if (!errors.IsEmpty)
        {
            throw new DiscussionValidationException(errors.ToDictionary());
        }
    }

    /// <summary>Current UTC time at microsecond precision, so every storage provider returns the same value.</summary>
    private DateTimeOffset Now()
    {
        var ticks = clock.GetUtcNow().UtcTicks;
        return new DateTimeOffset(ticks - (ticks % 10), TimeSpan.Zero);
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
