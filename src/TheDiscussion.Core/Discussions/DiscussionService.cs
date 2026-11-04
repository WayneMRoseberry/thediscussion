using TheDiscussion.Core.Status;
using TheDiscussion.Core.Storage;

namespace TheDiscussion.Core.Discussions;

/// <summary>Input for a new discussion. Field names in validation errors: <c>title</c>, <c>topicTags</c>, <c>thesis</c>.</summary>
public sealed record NewDiscussion(string? Title, IReadOnlyList<string?>? TopicTags, string? Thesis);

/// <summary>
/// Input for a new statement. Field names in validation errors: <c>kind</c>, <c>parentId</c>, <c>text</c>, <c>groupType</c>.
/// A <c>null</c> field means the client did not supply it.
/// </summary>
public sealed record NewStatement(StatementKind? Kind, Guid? ParentId, string? Text, SupportGroupType? GroupType);

/// <summary>
/// Changes to a discussion's details. Only the fields marked as sent are changed; a sent field is validated as on create.
/// Field names in validation errors: <c>title</c>, <c>topicTags</c>.
/// </summary>
public sealed record DiscussionChanges(bool TitleSent, string? Title, bool TopicTagsSent, IReadOnlyList<string?>? TopicTags)
{
    public static DiscussionChanges None { get; } = new(false, null, false, null);

    public DiscussionChanges WithTitle(string? title) => this with { TitleSent = true, Title = title };

    public DiscussionChanges WithTopicTags(IReadOnlyList<string?>? topicTags) => this with { TopicTagsSent = true, TopicTags = topicTags };
}

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
        var topicTags = ValidateTopicTags(input.TopicTags, errors);

        ThrowIfInvalid(errors);

        var now = Now();
        var discussion = new DiscussionSummary(Guid.NewGuid(), title!, topicTags!, now);
        var thesis = new Statement(Guid.NewGuid(), discussion.Id, StatementKind.Thesis, null, thesisText, null, now);
        await store.CreateDiscussionAsync(discussion, thesis, cancellationToken);

        return WithStatus(new Discussion(discussion.Id, discussion.Title, discussion.TopicTags, discussion.CreatedAt, [thesis]));
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

    /// <summary>Changes a discussion's title and/or topic tags (last write wins).</summary>
    /// <param name="discussionId">The discussion to change.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="formatErrors">Errors the caller already found while reading the request.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The updated discussion with its status.</returns>
    /// <exception cref="DiscussionNotFoundException">The discussion does not exist.</exception>
    /// <exception cref="DiscussionValidationException">The input is invalid; nothing is changed.</exception>
    public async Task<DiscussionWithStatus> UpdateDiscussionAsync(
        Guid discussionId,
        DiscussionChanges changes,
        ValidationErrors? formatErrors = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var discussion = await store.GetDiscussionAsync(discussionId, cancellationToken)
            ?? throw new DiscussionNotFoundException(discussionId);

        var errors = formatErrors ?? new ValidationErrors();
        var title = discussion.Title;
        var topicTags = discussion.TopicTags;
        if (changes.TitleSent)
        {
            title = errors.RequireText("title", changes.Title, DiscussionRules.TitleMaxLength, "Title") ?? title;
        }

        if (changes.TopicTagsSent)
        {
            topicTags = ValidateTopicTags(changes.TopicTags, errors) ?? topicTags;
        }

        if (!changes.TitleSent && !changes.TopicTagsSent && errors.IsEmpty)
        {
            errors.Add("body", "Send at least one field to change: title or topicTags.");
        }

        ThrowIfInvalid(errors);

        if (!await store.UpdateDiscussionAsync(discussionId, title, topicTags, cancellationToken))
        {
            throw new DiscussionNotFoundException(discussionId);
        }

        return await GetDiscussionAsync(discussionId, cancellationToken) ?? throw new DiscussionNotFoundException(discussionId);
    }

    /// <summary>Deletes a discussion and all its statements.</summary>
    /// <exception cref="DiscussionNotFoundException">The discussion does not exist.</exception>
    public async Task DeleteDiscussionAsync(Guid discussionId, CancellationToken cancellationToken = default)
    {
        if (!await store.DeleteDiscussionAsync(discussionId, cancellationToken))
        {
            throw new DiscussionNotFoundException(discussionId);
        }
    }

    /// <summary>
    /// Replaces the text of a statement of any kind that has text, including the thesis, and marks it as edited.
    /// Allowed whether or not anything is attached beneath it. Kind and parent never change.
    /// </summary>
    /// <param name="discussionId">The discussion.</param>
    /// <param name="statementId">The statement to edit.</param>
    /// <param name="text">The new text (field <c>text</c>).</param>
    /// <param name="formatErrors">Errors the caller already found while reading the request.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The edited statement and the discussion's status.</returns>
    /// <exception cref="DiscussionNotFoundException">The discussion does not exist.</exception>
    /// <exception cref="StatementNotFoundException">The statement is not part of the discussion.</exception>
    /// <exception cref="DiscussionValidationException">The input is invalid; nothing is changed.</exception>
    public async Task<(Statement Statement, DiscussionWithStatus Discussion)> EditStatementTextAsync(
        Guid discussionId,
        Guid statementId,
        string? text,
        ValidationErrors? formatErrors = null,
        CancellationToken cancellationToken = default)
    {
        var discussion = await store.GetDiscussionAsync(discussionId, cancellationToken)
            ?? throw new DiscussionNotFoundException(discussionId);
        var statement = discussion.Statements.FirstOrDefault(s => s.Id == statementId)
            ?? throw new StatementNotFoundException(discussionId, statementId);

        var errors = formatErrors ?? new ValidationErrors();
        string? trimmed = null;
        if (statement.Kind == StatementKind.SupportGroup)
        {
            errors.Add("text", "Support groups have no text to edit.");
        }
        else if (!errors.Has("text"))
        {
            trimmed = errors.RequireText("text", text, DiscussionRules.StatementTextMaxLength, "Text");
        }

        ThrowIfInvalid(errors);

        var edited = statement with { Text = trimmed, EditedAt = Now() };
        await store.UpdateStatementTextAsync(discussionId, statementId, trimmed!, edited.EditedAt!.Value, cancellationToken);

        var updated = discussion with
        {
            Statements = discussion.Statements.Select(s => s.Id == statementId ? edited : s).ToList(),
        };
        return (edited, WithStatus(updated));
    }

    /// <summary>Deletes a statement and every statement beneath it, at any depth. The thesis cannot be deleted on its own.</summary>
    /// <exception cref="DiscussionNotFoundException">The discussion does not exist.</exception>
    /// <exception cref="StatementNotFoundException">The statement is not part of the discussion.</exception>
    /// <exception cref="DiscussionValidationException">The statement is the thesis (field <c>statementId</c>); nothing is deleted.</exception>
    public async Task DeleteStatementAsync(Guid discussionId, Guid statementId, CancellationToken cancellationToken = default)
    {
        var discussion = await store.GetDiscussionAsync(discussionId, cancellationToken)
            ?? throw new DiscussionNotFoundException(discussionId);
        var statement = discussion.Statements.FirstOrDefault(s => s.Id == statementId)
            ?? throw new StatementNotFoundException(discussionId, statementId);

        if (statement.Kind == StatementKind.Thesis)
        {
            var errors = new ValidationErrors();
            errors.Add("statementId", "The thesis cannot be deleted on its own. Delete the discussion instead.");
            ThrowIfInvalid(errors);
        }

        await store.DeleteStatementAsync(discussionId, statementId, cancellationToken);
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

    /// <summary>1 to 5 distinct, well-formed tags (each trimmed). Every problem is reported under <c>topicTags</c>.</summary>
    /// <returns>The trimmed tags in the order given, or <c>null</c> when they are invalid.</returns>
    private static IReadOnlyList<string>? ValidateTopicTags(IReadOnlyList<string?>? values, ValidationErrors errors)
    {
        const string Field = "topicTags";
        if (values is null || values.Count < DiscussionRules.TopicTagsMin)
        {
            errors.Add(Field, "At least one topic tag is required.");
            return null;
        }

        var valid = true;
        if (values.Count > DiscussionRules.TopicTagsMax)
        {
            errors.Add(Field, $"A discussion has at most {DiscussionRules.TopicTagsMax} topic tags (it has {values.Count}).");
            valid = false;
        }

        var tags = new List<string>(values.Count);
        foreach (var value in values)
        {
            var tag = value?.Trim();
            if (string.IsNullOrEmpty(tag))
            {
                errors.Add(Field, "Topic tags cannot be empty.");
                valid = false;
            }
            else if (!DiscussionRules.IsValidTopicTag(tag))
            {
                errors.Add(
                    Field,
                    $"Topic tag '{tag}' must be 1 to {DiscussionRules.TopicTagMaxSegments} dot-separated segments of lowercase letters, digits or hyphens, for example 'work.remote'.");
                valid = false;
            }
            else if (tags.Contains(tag, StringComparer.Ordinal))
            {
                errors.Add(Field, $"Topic tag '{tag}' is listed more than once.");
                valid = false;
            }
            else
            {
                tags.Add(tag);
            }
        }

        return valid ? tags : null;
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
