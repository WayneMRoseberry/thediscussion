using TheDiscussion.Core.Status;
using TheDiscussion.Core.Storage;
using TheDiscussion.Core.Topics;

namespace TheDiscussion.Core.Discussions;

/// <summary>Input for a new discussion. Field names in validation errors: <c>title</c>, <c>topicTags</c>, <c>thesis</c>.</summary>
public sealed record NewDiscussion(string? Title, IReadOnlyList<string?>? TopicTags, string? Thesis);

/// <summary>
/// Input for a new statement. Field names in validation errors: <c>kind</c>, <c>parentId</c>, <c>text</c>, <c>groupType</c>,
/// <c>name</c>. A <c>null</c> field means the client did not supply it. Support groups take a name and a group type and no
/// text; every other kind takes text and neither a name nor a group type.
/// </summary>
/// <remarks>
/// A <c>…ReadError</c> says the client sent the field but the caller could not read it (for example an unknown kind
/// name); the value is then <c>null</c> and the message is reported for that field in place of the usual checks.
/// </remarks>
public sealed record NewStatement(StatementKind? Kind, Guid? ParentId, string? Text, SupportGroupType? GroupType, string? Name = null)
{
    public string? KindReadError { get; init; }

    public string? ParentIdReadError { get; init; }

    public string? GroupTypeReadError { get; init; }
}

/// <summary>
/// Changes to one statement (the statement <c>PATCH</c>). Each <c>…Sent</c> flag says whether the client sent the field
/// at all, so an absent field can be told apart from one sent as <c>null</c>. <see cref="GroupType"/> is <c>null</c> when
/// it was sent as <c>null</c> or as a value that is not a group type. Field names in validation errors: <c>kind</c>,
/// <c>parentId</c>, <c>text</c>, <c>name</c>, <c>groupType</c>, <c>body</c>.
/// </summary>
/// <remarks>
/// A <c>…ReadError</c> says the field was sent but the caller could not read it (for example a number where a string
/// belongs). The field counts as sent with a <c>null</c> value, and the message is reported when that field's check is
/// reached, in the order <see cref="DiscussionService.EditStatementAsync"/> describes.
/// </remarks>
public sealed record StatementChanges(
    bool KindSent,
    bool ParentIdSent,
    bool TextSent,
    string? Text,
    bool NameSent,
    string? Name,
    bool GroupTypeSent,
    SupportGroupType? GroupType)
{
    public static StatementChanges None { get; } = new(false, false, false, null, false, null, false, null);

    public string? TextReadError { get; init; }

    public string? NameReadError { get; init; }

    public string? GroupTypeReadError { get; init; }

    public StatementChanges WithKind() => this with { KindSent = true };

    public StatementChanges WithParentId() => this with { ParentIdSent = true };

    public StatementChanges WithText(string? text) => this with { TextSent = true, Text = text, TextReadError = null };

    public StatementChanges WithUnreadableText(string error) => this with { TextSent = true, Text = null, TextReadError = error };

    public StatementChanges WithName(string? name) => this with { NameSent = true, Name = name, NameReadError = null };

    public StatementChanges WithUnreadableName(string error) => this with { NameSent = true, Name = null, NameReadError = error };

    public StatementChanges WithGroupType(SupportGroupType? groupType) =>
        this with { GroupTypeSent = true, GroupType = groupType, GroupTypeReadError = null };

    public StatementChanges WithUnreadableGroupType(string error) =>
        this with { GroupTypeSent = true, GroupType = null, GroupTypeReadError = error };
}

/// <summary>
/// Changes to a discussion's details. Only the fields marked as sent are changed; a sent field is validated as on create.
/// Field names in validation errors: <c>title</c>, <c>topicTags</c>, <c>body</c>. A <c>…ReadError</c> says the field was
/// sent but could not be read; it is reported for that field in place of the usual checks.
/// </summary>
public sealed record DiscussionChanges(bool TitleSent, string? Title, bool TopicTagsSent, IReadOnlyList<string?>? TopicTags)
{
    public static DiscussionChanges None { get; } = new(false, null, false, null);

    public string? TitleReadError { get; init; }

    public string? TopicTagsReadError { get; init; }

    public DiscussionChanges WithTitle(string? title) => this with { TitleSent = true, Title = title, TitleReadError = null };

    public DiscussionChanges WithUnreadableTitle(string error) => this with { TitleSent = true, Title = null, TitleReadError = error };

    public DiscussionChanges WithTopicTags(IReadOnlyList<string?>? topicTags) =>
        this with { TopicTagsSent = true, TopicTags = topicTags, TopicTagsReadError = null };

    public DiscussionChanges WithUnreadableTopicTags(string error) =>
        this with { TopicTagsSent = true, TopicTags = null, TopicTagsReadError = error };
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
        var title = RequireTitle(input.Title, errors);
        var thesisText = RequireThesis(input.Thesis, errors);
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

    /// <summary>Discussions tagged with <paramref name="tag"/> or one of its sub-tags, each once, most recent first.</summary>
    /// <exception cref="DiscussionValidationException">The tag is malformed (field <c>tag</c>).</exception>
    public Task<IReadOnlyList<DiscussionSummary>> ListDiscussionsUnderTagAsync(string? tag, CancellationToken cancellationToken = default)
    {
        var trimmed = tag?.Trim();
        if (!DiscussionRules.IsValidTopicTag(trimmed))
        {
            var errors = new ValidationErrors();
            errors.Add(
                RequestFields.Tag,
                $"Tag must be 1 to {DiscussionRules.TopicTagMaxSegments} dot-separated segments of lowercase letters, digits or hyphens, for example 'work.remote'.");
            ThrowIfInvalid(errors);
        }

        return store.ListDiscussionsUnderTagAsync(trimmed!, cancellationToken);
    }

    /// <summary>The topic hierarchy built from every discussion's tags, with distinct-discussion counts.</summary>
    public async Task<IReadOnlyList<TopicNode>> GetTopicsAsync(CancellationToken cancellationToken = default) =>
        TopicTree.Build(await store.ListDiscussionsAsync(cancellationToken));

    /// <returns>The discussion with its status, or <c>null</c> when it does not exist.</returns>
    public async Task<DiscussionWithStatus?> GetDiscussionAsync(Guid discussionId, CancellationToken cancellationToken = default)
    {
        var discussion = await store.GetDiscussionAsync(discussionId, cancellationToken);
        return discussion is null ? null : WithStatus(discussion);
    }

    /// <summary>Adds a statement to a discussion.</summary>
    /// <param name="discussionId">The discussion to add to.</param>
    /// <param name="input">The statement, including any fields the caller could not read.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The stored statement and the discussion's updated status.</returns>
    /// <exception cref="DiscussionNotFoundException">The discussion does not exist.</exception>
    /// <exception cref="DiscussionValidationException">The input is invalid; nothing is stored.</exception>
    public async Task<(Statement Statement, DiscussionWithStatus Discussion)> AddStatementAsync(
        Guid discussionId,
        NewStatement input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var discussion = await store.GetDiscussionAsync(discussionId, cancellationToken)
            ?? throw new DiscussionNotFoundException(discussionId);

        var errors = new ValidationErrors();
        var statement = Validate(discussion, input, errors);
        ThrowIfInvalid(errors);

        try
        {
            await store.AddStatementAsync(statement!, cancellationToken);
        }
        catch (ParentStatementNotFoundException)
        {
            errors.Add(RequestFields.ParentId, $"No statement with id '{input.ParentId}' exists in this discussion.");
            ThrowIfInvalid(errors);
            throw;
        }

        var updated = discussion with { Statements = [.. discussion.Statements, statement!] };
        return (statement!, WithStatus(updated));
    }

    /// <summary>Changes a discussion's title and/or topic tags (last write wins).</summary>
    /// <param name="discussionId">The discussion to change.</param>
    /// <param name="changes">The fields to change, including any the caller could not read.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The updated discussion with its status.</returns>
    /// <exception cref="DiscussionNotFoundException">The discussion does not exist.</exception>
    /// <exception cref="DiscussionValidationException">The input is invalid; nothing is changed.</exception>
    public async Task<DiscussionWithStatus> UpdateDiscussionAsync(
        Guid discussionId,
        DiscussionChanges changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var discussion = await store.GetDiscussionAsync(discussionId, cancellationToken)
            ?? throw new DiscussionNotFoundException(discussionId);

        var errors = new ValidationErrors();
        var title = discussion.Title;
        var topicTags = discussion.TopicTags;
        if (changes.TitleReadError is { } titleReadError)
        {
            errors.Add(RequestFields.Title, titleReadError);
        }
        else if (changes.TitleSent)
        {
            title = RequireTitle(changes.Title, errors) ?? title;
        }

        if (changes.TopicTagsReadError is { } topicTagsReadError)
        {
            errors.Add(RequestFields.TopicTags, topicTagsReadError);
        }
        else if (changes.TopicTagsSent)
        {
            topicTags = ValidateTopicTags(changes.TopicTags, errors) ?? topicTags;
        }

        if (!changes.TitleSent && !changes.TopicTagsSent)
        {
            errors.Add(RequestFields.Body, "Send at least one field to change: title or topicTags.");
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
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The edited statement and the discussion's status.</returns>
    /// <exception cref="DiscussionNotFoundException">The discussion does not exist.</exception>
    /// <exception cref="StatementNotFoundException">The statement is not part of the discussion.</exception>
    /// <exception cref="DiscussionValidationException">The input is invalid; nothing is changed.</exception>
    public Task<(Statement Statement, DiscussionWithStatus Discussion)> EditStatementTextAsync(
        Guid discussionId,
        Guid statementId,
        string? text,
        CancellationToken cancellationToken = default) =>
        EditStatementAsync(discussionId, statementId, StatementChanges.None.WithText(text), cancellationToken);

    /// <summary>
    /// Edits a statement and marks it as edited (last write wins). A support group's <c>name</c> and/or <c>groupType</c>
    /// can change (D-5); every other kind, including the thesis, can change its <c>text</c>. Kind and parent never change.
    /// The checks run in this order (UC-3), and the first that fails rejects the request without changing anything:
    /// <list type="number">
    /// <item>unknown discussion or statement (not found);</item>
    /// <item><c>kind</c> or <c>parentId</c> sent;</item>
    /// <item>not a group: <c>name</c> or <c>groupType</c> sent, then the text checks;</item>
    /// <item>a group: <c>text</c> sent;</item>
    /// <item>a group: neither <c>name</c> nor <c>groupType</c> sent (field <c>body</c>);</item>
    /// <item><c>name</c> sent but not 1 to <see cref="DiscussionRules.GroupNameMaxLength"/> characters after trimming;</item>
    /// <item><c>name</c> not sent to a group that has no name, which would stay unnamed;</item>
    /// <item><c>groupType</c> sent but not a group type (including <c>null</c>).</item>
    /// </list>
    /// A field the caller could not read (see the <c>…ReadError</c> properties of <see cref="StatementChanges"/>) fails its
    /// own check with the caller's message. A valid edit always saves and sets <see cref="Statement.EditedAt"/> once, even
    /// when a value is unchanged.
    /// </summary>
    /// <param name="discussionId">The discussion.</param>
    /// <param name="statementId">The statement to edit.</param>
    /// <param name="changes">The fields the client sent, including any the caller could not read.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The edited statement and the discussion's status.</returns>
    /// <exception cref="DiscussionNotFoundException">The discussion does not exist.</exception>
    /// <exception cref="StatementNotFoundException">The statement is not part of the discussion.</exception>
    /// <exception cref="DiscussionValidationException">The input is invalid; nothing is changed.</exception>
    public async Task<(Statement Statement, DiscussionWithStatus Discussion)> EditStatementAsync(
        Guid discussionId,
        Guid statementId,
        StatementChanges changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var discussion = await store.GetDiscussionAsync(discussionId, cancellationToken)
            ?? throw new DiscussionNotFoundException(discussionId);
        var statement = discussion.Statements.FirstOrDefault(s => s.Id == statementId)
            ?? throw new StatementNotFoundException(discussionId, statementId);

        var errors = new ValidationErrors();
        if (changes.KindSent)
        {
            errors.Add(RequestFields.Kind, "A statement's kind never changes.");
        }

        if (changes.ParentIdSent)
        {
            errors.Add(RequestFields.ParentId, "A statement's parent never changes.");
        }

        ThrowIfInvalid(errors);

        Statement edited;
        if (statement.Kind == StatementKind.SupportGroup)
        {
            var (name, groupType) = ValidateGroupChanges(statement, changes);
            edited = statement with { Name = name ?? statement.Name, GroupType = groupType ?? statement.GroupType, EditedAt = Now() };
            await store.UpdateSupportGroupAsync(discussionId, statementId, name, groupType, edited.EditedAt!.Value, cancellationToken);
        }
        else
        {
            var text = ValidateTextChanges(statement, changes);
            edited = statement with { Text = text, EditedAt = Now() };
            await store.UpdateStatementTextAsync(discussionId, statementId, text, edited.EditedAt!.Value, cancellationToken);
        }

        var updated = discussion with
        {
            Statements = discussion.Statements.Select(s => s.Id == statementId ? edited : s).ToList(),
        };
        return (edited, WithStatus(updated));
    }

    /// <summary>The checks for a statement with text (UC-3 "Target is a group?" no). Returns the trimmed text.</summary>
    private static string ValidateTextChanges(Statement statement, StatementChanges changes)
    {
        var errors = new ValidationErrors();
        if (changes.NameSent)
        {
            errors.Add(RequestFields.Name, $"Only support groups have a name; {DiscussionRules.Describe(statement.Kind)} has text instead.");
        }

        if (changes.GroupTypeSent)
        {
            errors.Add(RequestFields.GroupType, $"Only support groups have a group type; {DiscussionRules.Describe(statement.Kind)} has none.");
        }

        ThrowIfInvalid(errors);

        string? text = null;
        if (changes.TextReadError is { } textReadError)
        {
            errors.Add(RequestFields.Text, textReadError);
        }
        else
        {
            text = RequireStatementText(changes.Text, errors);
        }

        ThrowIfInvalid(errors);
        return text!;
    }

    /// <summary>
    /// The checks for a support group, in UC-3 order. Returns the trimmed new name and the new group type; each is
    /// <c>null</c> when it stays as it is.
    /// </summary>
    private static (string? Name, SupportGroupType? GroupType) ValidateGroupChanges(Statement group, StatementChanges changes)
    {
        var errors = new ValidationErrors();
        if (changes.TextSent)
        {
            errors.Add(RequestFields.Text, "Support groups have no text to edit; change their name or groupType instead.");
            ThrowIfInvalid(errors);
        }

        if (!changes.NameSent && !changes.GroupTypeSent)
        {
            errors.Add(RequestFields.Body, "Send at least one field to change on a support group: name or groupType.");
            ThrowIfInvalid(errors);
        }

        string? name = null;
        if (changes.NameReadError is { } nameReadError)
        {
            errors.Add(RequestFields.Name, nameReadError);
            ThrowIfInvalid(errors);
        }
        else if (changes.NameSent)
        {
            name = DiscussionRules.NormalizeGroupName(changes.Name);
            if (name is null)
            {
                AddGroupNameError(changes.Name, errors);
                ThrowIfInvalid(errors);
            }
        }
        else if (group.Name is null)
        {
            errors.Add(
                RequestFields.Name,
                $"This support group has no name yet; send a name of 1 to {DiscussionRules.GroupNameMaxLength} characters with the change.");
            ThrowIfInvalid(errors);
        }

        if (changes.GroupTypeSent && changes.GroupType is null)
        {
            errors.Add(RequestFields.GroupType, changes.GroupTypeReadError ?? "Group type must be 'allRequired' or 'anySufficient'.");
            ThrowIfInvalid(errors);
        }

        return (name, changes.GroupType);
    }

    /// <summary>Records why <paramref name="value"/> is not a valid group name (see <see cref="DiscussionRules.NormalizeGroupName"/>).</summary>
    private static void AddGroupNameError(string? value, ValidationErrors errors)
    {
        var trimmed = value?.Trim();
        errors.Add(
            RequestFields.Name,
            string.IsNullOrEmpty(trimmed)
                ? $"A support group needs a name of 1 to {DiscussionRules.GroupNameMaxLength} characters."
                : $"Name must be at most {DiscussionRules.GroupNameMaxLength} characters (it has {trimmed.Length}).");
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
            errors.Add(RequestFields.StatementId, "The thesis cannot be deleted on its own. Delete the discussion instead.");
            ThrowIfInvalid(errors);
        }

        await store.DeleteStatementAsync(discussionId, statementId, cancellationToken);
    }

    private Statement? Validate(Discussion discussion, NewStatement input, ValidationErrors errors)
    {
        if (input.KindReadError is { } kindReadError)
        {
            errors.Add(RequestFields.Kind, kindReadError);
        }
        else if (input.Kind is null)
        {
            errors.Add(RequestFields.Kind, "Kind is required.");
        }
        else if (input.Kind == StatementKind.Thesis)
        {
            errors.Add(RequestFields.Kind, "A discussion has exactly one thesis, created with the discussion.");
        }

        Statement? parent = null;
        if (input.ParentIdReadError is { } parentIdReadError)
        {
            errors.Add(RequestFields.ParentId, parentIdReadError);
        }
        else if (input.ParentId is not { } parentId)
        {
            errors.Add(RequestFields.ParentId, "Parent id is required.");
        }
        else
        {
            parent = discussion.Statements.FirstOrDefault(statement => statement.Id == parentId);
            if (parent is null)
            {
                errors.Add(RequestFields.ParentId, $"No statement with id '{parentId}' exists in this discussion.");
            }
            else if (input.Kind is { } kind && kind != StatementKind.Thesis && !DiscussionRules.CanAttach(kind, parent.Kind))
            {
                var allowed = string.Join(" or ", DiscussionRules.AllowedParentKinds(kind).Select(DiscussionRules.Describe));
                errors.Add(
                    RequestFields.ParentId,
                    $"{Capitalize(DiscussionRules.Describe(kind))} must target {allowed}; '{parentId}' is {DiscussionRules.Describe(parent.Kind)}.");
            }
        }

        string? text = null;
        string? name = null;
        if (input.Kind == StatementKind.SupportGroup)
        {
            if (!string.IsNullOrWhiteSpace(input.Text))
            {
                errors.Add(RequestFields.Text, "Support groups have no text.");
            }

            name = DiscussionRules.NormalizeGroupName(input.Name);
            if (name is null)
            {
                AddGroupNameError(input.Name, errors);
            }

            if (input.GroupTypeReadError is { } groupTypeReadError)
            {
                errors.Add(RequestFields.GroupType, groupTypeReadError);
            }
            else if (input.GroupType is null)
            {
                errors.Add(RequestFields.GroupType, "Group type is required for a support group: 'allRequired' or 'anySufficient'.");
            }
        }
        else
        {
            text = RequireStatementText(input.Text, errors);
            if (input.GroupTypeReadError is { } groupTypeReadError)
            {
                errors.Add(RequestFields.GroupType, groupTypeReadError);
            }
            else if (input.GroupType is not null && input.Kind is not null)
            {
                errors.Add(RequestFields.GroupType, "Only support groups have a group type.");
            }

            if (input.Name is not null && input.Kind is not null)
            {
                errors.Add(RequestFields.Name, "Only support groups have a name.");
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
            Now(),
            Name: name);
    }

    // Each text rule is defined once here (field, length limit and the label used in its messages), so every place that
    // checks the same field applies the same rule.

    /// <summary>A discussion title: required, at most <see cref="DiscussionRules.TitleMaxLength"/> characters after trimming.</summary>
    /// <returns>The trimmed title, or <c>null</c> after recording an error under <c>title</c>.</returns>
    private static string? RequireTitle(string? value, ValidationErrors errors) =>
        errors.RequireText(RequestFields.Title, value, DiscussionRules.TitleMaxLength, "Title");

    /// <summary>A thesis: required, at most <see cref="DiscussionRules.StatementTextMaxLength"/> characters after trimming.</summary>
    /// <returns>The trimmed thesis, or <c>null</c> after recording an error under <c>thesis</c>.</returns>
    private static string? RequireThesis(string? value, ValidationErrors errors) =>
        errors.RequireText(RequestFields.Thesis, value, DiscussionRules.StatementTextMaxLength, "Thesis");

    /// <summary>
    /// The text of a statement that has text: required, at most <see cref="DiscussionRules.StatementTextMaxLength"/>
    /// characters after trimming.
    /// </summary>
    /// <returns>The trimmed text, or <c>null</c> after recording an error under <c>text</c>.</returns>
    private static string? RequireStatementText(string? value, ValidationErrors errors) =>
        errors.RequireText(RequestFields.Text, value, DiscussionRules.StatementTextMaxLength, "Text");

    /// <summary>1 to 5 distinct, well-formed tags (each trimmed). Every problem is reported under <c>topicTags</c>.</summary>
    /// <returns>The trimmed tags in the order given, or <c>null</c> when they are invalid.</returns>
    private static IReadOnlyList<string>? ValidateTopicTags(IReadOnlyList<string?>? values, ValidationErrors errors)
    {
        const string Field = RequestFields.TopicTags;
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
