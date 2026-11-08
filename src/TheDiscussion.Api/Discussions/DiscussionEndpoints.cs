using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Storage;

namespace TheDiscussion.Api.Discussions;

/// <summary>REST endpoints under <c>/api/v1/discussions</c>. Errors are RFC 7807 problem details.</summary>
internal static class DiscussionEndpoints
{
    public const string BasePath = "/api/v1/discussions";

    private static readonly IReadOnlyDictionary<string, StatementKind> KindNames =
        new Dictionary<string, StatementKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["supportGroup"] = StatementKind.SupportGroup,
            ["supportingStatement"] = StatementKind.SupportingStatement,
            ["illustration"] = StatementKind.Illustration,
            ["counterArgument"] = StatementKind.CounterArgument,
            ["rebuttal"] = StatementKind.Rebuttal,
        };

    private static readonly IReadOnlyDictionary<string, SupportGroupType> GroupTypeNames =
        new Dictionary<string, SupportGroupType>(StringComparer.OrdinalIgnoreCase)
        {
            ["allRequired"] = SupportGroupType.AllRequired,
            ["anySufficient"] = SupportGroupType.AnySufficient,
        };

    public static IEndpointRouteBuilder MapDiscussionEndpoints(this IEndpointRouteBuilder app)
    {
        var discussions = app.MapGroup(BasePath);
        discussions.MapGet(string.Empty, ListAsync);
        discussions.MapPost(string.Empty, CreateAsync);
        discussions.MapGet("/{id:guid}", GetAsync);
        discussions.MapPatch("/{id:guid}", UpdateAsync);
        discussions.MapDelete("/{id:guid}", DeleteAsync);
        discussions.MapPost("/{id:guid}/statements", AddStatementAsync);
        discussions.MapPatch("/{id:guid}/statements/{statementId:guid}", EditStatementAsync);
        discussions.MapDelete("/{id:guid}/statements/{statementId:guid}", DeleteStatementAsync);
        return app;
    }

    private static async Task<IResult> UpdateAsync(Guid id, HttpRequest request, DiscussionService service, CancellationToken cancellationToken)
    {
        var (body, error) = await ReadJsonObjectAsync(request, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var changes = DiscussionChanges.None;
        if (TryGetField(body, RequestFields.Title, out var title))
        {
            changes = TryReadString(title, out var titleText)
                ? changes.WithTitle(titleText)
                : changes.WithUnreadableTitle(WrongTypeMessage(RequestFields.Title));
        }

        if (TryGetField(body, RequestFields.TopicTags, out var topicTags))
        {
            changes = TryReadStringArray(topicTags, out var topicTagList)
                ? changes.WithTopicTags(topicTagList)
                : changes.WithUnreadableTopicTags(WrongTypeMessage(RequestFields.TopicTags));
        }

        try
        {
            var updated = await service.UpdateDiscussionAsync(id, changes, cancellationToken);
            return TypedResults.Ok(DiscussionResponse.From(updated));
        }
        catch (DiscussionNotFoundException)
        {
            return NotFound(id);
        }
        catch (DiscussionValidationException invalid)
        {
            return Invalid(invalid.Errors);
        }
    }

    private static async Task<IResult> DeleteAsync(Guid id, DiscussionService service, CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteDiscussionAsync(id, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (DiscussionNotFoundException)
        {
            return NotFound(id);
        }
    }

    private static async Task<IResult> EditStatementAsync(
        Guid id,
        Guid statementId,
        HttpRequest request,
        DiscussionService service,
        CancellationToken cancellationToken)
    {
        var (body, error) = await ReadJsonObjectAsync(request, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        // Only record what was sent (and what could not be read) here; the service decides, in the UC-3 order, which
        // field to report.
        var changes = StatementChanges.None;
        if (TryGetField(body, RequestFields.Kind, out _))
        {
            changes = changes.WithKind();
        }

        if (TryGetField(body, RequestFields.ParentId, out _))
        {
            changes = changes.WithParentId();
        }

        if (TryGetField(body, RequestFields.Text, out var textValue))
        {
            changes = TryReadString(textValue, out var text)
                ? changes.WithText(text)
                : changes.WithUnreadableText(WrongTypeMessage(RequestFields.Text));
        }

        if (TryGetField(body, RequestFields.Name, out var nameValue))
        {
            changes = TryReadString(nameValue, out var name)
                ? changes.WithName(name)
                : changes.WithUnreadableName(WrongTypeMessage(RequestFields.Name));
        }

        if (TryGetField(body, RequestFields.GroupType, out var groupTypeValue))
        {
            if (!TryReadString(groupTypeValue, out var groupTypeName))
            {
                changes = changes.WithUnreadableGroupType(WrongTypeMessage(RequestFields.GroupType));
            }
            else if (groupTypeName is null)
            {
                changes = changes.WithGroupType(null);
            }
            else
            {
                changes = TryParseGroupType(groupTypeName, out var groupType)
                    ? changes.WithGroupType(groupType)
                    : changes.WithUnreadableGroupType(UnknownGroupTypeMessage);
            }
        }

        try
        {
            var (statement, discussion) = await service.EditStatementAsync(id, statementId, changes, cancellationToken);
            return TypedResults.Ok(StatementResponse.From(statement, discussion.Status));
        }
        catch (DiscussionNotFoundException)
        {
            return NotFound(id);
        }
        catch (StatementNotFoundException)
        {
            return StatementNotFound(id, statementId);
        }
        catch (DiscussionValidationException invalid)
        {
            return Invalid(invalid.Errors);
        }
    }

    private static async Task<IResult> DeleteStatementAsync(Guid id, Guid statementId, DiscussionService service, CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteStatementAsync(id, statementId, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (DiscussionNotFoundException)
        {
            return NotFound(id);
        }
        catch (StatementNotFoundException)
        {
            return StatementNotFound(id, statementId);
        }
        catch (DiscussionValidationException invalid)
        {
            return Invalid(invalid.Errors);
        }
    }

    /// <summary><c>GET /api/v1/discussions[?tag=work]</c>: all discussions, or those under one tag (the tag or its sub-tags).</summary>
    private static async Task<IResult> ListAsync(HttpRequest request, DiscussionService service, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<DiscussionSummary> discussions;
            if (request.Query.TryGetValue("tag", out var tags))
            {
                if (tags.Count != 1)
                {
                    return Invalid(new Dictionary<string, string[]> { [RequestFields.Tag] = ["Send one tag."] });
                }

                discussions = await service.ListDiscussionsUnderTagAsync(tags[0], cancellationToken);
            }
            else
            {
                discussions = await service.ListDiscussionsAsync(cancellationToken);
            }

            return TypedResults.Ok(discussions.Select(DiscussionSummaryResponse.From).ToList());
        }
        catch (DiscussionValidationException invalid)
        {
            return Invalid(invalid.Errors);
        }
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request,
        DiscussionService service,
        IOptions<JsonOptions> json,
        CancellationToken cancellationToken)
    {
        var (body, error) = await ReadJsonAsync<CreateDiscussionRequest>(request, json.Value.SerializerOptions, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        try
        {
            var created = await service.CreateDiscussionAsync(new NewDiscussion(body!.Title, body.TopicTags, body.Thesis), cancellationToken);
            return TypedResults.Created($"{BasePath}/{created.Discussion.Id}", DiscussionResponse.From(created));
        }
        catch (DiscussionValidationException invalid)
        {
            return Invalid(invalid.Errors);
        }
    }

    private static async Task<IResult> GetAsync(Guid id, DiscussionService service, CancellationToken cancellationToken)
    {
        var discussion = await service.GetDiscussionAsync(id, cancellationToken);
        return discussion is null ? NotFound(id) : TypedResults.Ok(DiscussionResponse.From(discussion));
    }

    private static async Task<IResult> AddStatementAsync(
        Guid id,
        HttpRequest request,
        DiscussionService service,
        IOptions<JsonOptions> json,
        CancellationToken cancellationToken)
    {
        var (body, error) = await ReadJsonAsync<AddStatementRequest>(request, json.Value.SerializerOptions, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var input = Parse(body!);

        try
        {
            var (statement, discussion) = await service.AddStatementAsync(id, input, cancellationToken);
            return TypedResults.Created($"{BasePath}/{id}", StatementResponse.From(statement, discussion.Status));
        }
        catch (DiscussionNotFoundException)
        {
            return NotFound(id);
        }
        catch (DiscussionValidationException invalid)
        {
            return Invalid(invalid.Errors);
        }
    }

    private static readonly string UnknownKindMessage = $"Kind must be one of: {string.Join(", ", KindNames.Keys)}.";

    private static readonly string UnknownGroupTypeMessage = $"Group type must be one of: {string.Join(", ", GroupTypeNames.Keys)}.";

    /// <summary>Turns wire values into domain values, recording a read error for each value that cannot be read.</summary>
    private static NewStatement Parse(AddStatementRequest body)
    {
        StatementKind? kind = null;
        string? kindReadError = null;
        if (!string.IsNullOrWhiteSpace(body.Kind))
        {
            if (KindNames.TryGetValue(body.Kind.Trim(), out var parsedKind))
            {
                kind = parsedKind;
            }
            else
            {
                kindReadError = UnknownKindMessage;
            }
        }

        Guid? parentId = null;
        string? parentIdReadError = null;
        if (!string.IsNullOrWhiteSpace(body.ParentId))
        {
            if (Guid.TryParse(body.ParentId.Trim(), out var parsedParent))
            {
                parentId = parsedParent;
            }
            else
            {
                parentIdReadError = "Parent id must be a statement id (a GUID).";
            }
        }

        SupportGroupType? groupType = null;
        string? groupTypeReadError = null;
        if (!string.IsNullOrWhiteSpace(body.GroupType))
        {
            if (TryParseGroupType(body.GroupType, out var parsedGroupType))
            {
                groupType = parsedGroupType;
            }
            else
            {
                groupTypeReadError = UnknownGroupTypeMessage;
            }
        }

        return new NewStatement(kind, parentId, body.Text, groupType, body.Name)
        {
            KindReadError = kindReadError,
            ParentIdReadError = parentIdReadError,
            GroupTypeReadError = groupTypeReadError,
        };
    }

    /// <summary>A group type name, matched case-insensitively after trimming.</summary>
    private static bool TryParseGroupType(string value, out SupportGroupType groupType) =>
        GroupTypeNames.TryGetValue(value.Trim(), out groupType);

    /// <summary>Reads a JSON object body. Malformed JSON yields a 400 naming the offending field (or <c>body</c>).</summary>
    private static async Task<(T? Body, IResult? Error)> ReadJsonAsync<T>(
        HttpRequest request,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!request.HasJsonContentType())
        {
            return (null, UnsupportedMediaType());
        }

        try
        {
            var body = await request.ReadFromJsonAsync<T>(options, cancellationToken);
            return body is null
                ? (null, Invalid(new Dictionary<string, string[]> { [RequestFields.Body] = ["A JSON object body is required."] }))
                : (body, null);
        }
        catch (JsonException malformed)
        {
            // A syntax error (wrapped reader exception) concerns the whole body; otherwise a field had the wrong type.
            var field = malformed.InnerException is JsonException ? RequestFields.Body : FieldFromJsonPath(malformed.Path);
            var message = field == RequestFields.Body
                ? "The request body is not a valid JSON object."
                : WrongTypeMessage(field);
            return (null, Invalid(new Dictionary<string, string[]> { [field] = [message] }));
        }
    }

    /// <summary>
    /// Reads a JSON object body without binding it to a type, so PATCH handlers can tell an absent field from one sent as null.
    /// </summary>
    private static async Task<(JsonElement Body, IResult? Error)> ReadJsonObjectAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
        {
            return (default, UnsupportedMediaType());
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? (document.RootElement.Clone(), null)
                : (default, Invalid(new Dictionary<string, string[]> { [RequestFields.Body] = ["A JSON object body is required."] }));
        }
        catch (JsonException)
        {
            return (default, Invalid(new Dictionary<string, string[]> { [RequestFields.Body] = ["The request body is not a valid JSON object."] }));
        }
    }

    /// <summary>Finds a field of a JSON object, matching the name case-insensitively like the typed request bodies do.</summary>
    private static bool TryGetField(JsonElement body, string name, out JsonElement value)
    {
        foreach (var property in body.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Reads a string or null field; any other JSON type returns <c>false</c>.</summary>
    private static bool TryReadString(JsonElement value, out string? text)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                text = value.GetString();
                return true;
            case JsonValueKind.Null:
                text = null;
                return true;
            default:
                text = null;
                return false;
        }
    }

    /// <summary>Reads an array of strings (or null); any other JSON type returns <c>false</c>.</summary>
    private static bool TryReadStringArray(JsonElement value, out IReadOnlyList<string?>? items)
    {
        items = null;
        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Array
            || value.EnumerateArray().Any(item => item.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)))
        {
            return false;
        }

        items = value.EnumerateArray().Select(item => item.GetString()).ToList();
        return true;
    }

    private static string WrongTypeMessage(string field) =>
        field == RequestFields.TopicTags
            ? $"'{field}' has the wrong JSON type; send an array of strings, for example [\"work.remote\"]."
            : $"'{field}' has the wrong JSON type; send a string.";

    private static IResult UnsupportedMediaType() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status415UnsupportedMediaType,
            title: "Unsupported media type",
            detail: "Send the request body as application/json.");

    private static string FieldFromJsonPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("$.", StringComparison.Ordinal))
        {
            return RequestFields.Body;
        }

        var field = path[2..].Split('.', '[')[0];
        return field.Length == 0 ? RequestFields.Body : JsonNamingPolicy.CamelCase.ConvertName(field);
    }

    private static IResult Invalid(IDictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors, title: "The request is invalid.");

    private static IResult NotFound(Guid id) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Discussion not found",
            detail: $"No discussion with id '{id}' exists.");

    private static IResult StatementNotFound(Guid id, Guid statementId) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Statement not found",
            detail: $"No statement with id '{statementId}' exists in discussion '{id}'.");
}
