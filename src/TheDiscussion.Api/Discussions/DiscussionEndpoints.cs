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
        discussions.MapPost("/{id:guid}/statements", AddStatementAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(DiscussionService service, CancellationToken cancellationToken)
    {
        var discussions = await service.ListDiscussionsAsync(cancellationToken);
        return TypedResults.Ok(discussions.Select(DiscussionSummaryResponse.From).ToList());
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
            var created = await service.CreateDiscussionAsync(new NewDiscussion(body!.Title, body.TopicTag, body.Thesis), cancellationToken);
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

        var formatErrors = new ValidationErrors();
        var input = Parse(body!, formatErrors);

        try
        {
            var (statement, discussion) = await service.AddStatementAsync(id, input, formatErrors, cancellationToken);
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

    /// <summary>Turns wire values into domain values, recording a field error for each value that cannot be read.</summary>
    private static NewStatement Parse(AddStatementRequest body, ValidationErrors errors)
    {
        StatementKind? kind = null;
        if (!string.IsNullOrWhiteSpace(body.Kind))
        {
            if (KindNames.TryGetValue(body.Kind.Trim(), out var parsedKind))
            {
                kind = parsedKind;
            }
            else
            {
                errors.Add("kind", $"Kind must be one of: {string.Join(", ", KindNames.Keys)}.");
            }
        }

        Guid? parentId = null;
        if (!string.IsNullOrWhiteSpace(body.ParentId))
        {
            if (Guid.TryParse(body.ParentId.Trim(), out var parsedParent))
            {
                parentId = parsedParent;
            }
            else
            {
                errors.Add("parentId", "Parent id must be a statement id (a GUID).");
            }
        }

        SupportGroupType? groupType = null;
        if (!string.IsNullOrWhiteSpace(body.GroupType))
        {
            if (GroupTypeNames.TryGetValue(body.GroupType.Trim(), out var parsedGroupType))
            {
                groupType = parsedGroupType;
            }
            else
            {
                errors.Add("groupType", $"Group type must be one of: {string.Join(", ", GroupTypeNames.Keys)}.");
            }
        }

        return new NewStatement(kind, parentId, body.Text, groupType);
    }

    /// <summary>Reads a JSON object body. Malformed JSON yields a 400 naming the offending field (or <c>body</c>).</summary>
    private static async Task<(T? Body, IResult? Error)> ReadJsonAsync<T>(
        HttpRequest request,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!request.HasJsonContentType())
        {
            return (null, TypedResults.Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "Unsupported media type",
                detail: "Send the request body as application/json."));
        }

        try
        {
            var body = await request.ReadFromJsonAsync<T>(options, cancellationToken);
            return body is null
                ? (null, Invalid(new Dictionary<string, string[]> { ["body"] = ["A JSON object body is required."] }))
                : (body, null);
        }
        catch (JsonException malformed)
        {
            // A syntax error (wrapped reader exception) concerns the whole body; otherwise a field had the wrong type.
            var field = malformed.InnerException is JsonException ? "body" : FieldFromJsonPath(malformed.Path);
            var message = field == "body"
                ? "The request body is not a valid JSON object."
                : $"'{field}' has the wrong JSON type; send a string.";
            return (null, Invalid(new Dictionary<string, string[]> { [field] = [message] }));
        }
    }

    private static string FieldFromJsonPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("$.", StringComparison.Ordinal))
        {
            return "body";
        }

        var field = path[2..].Split('.', '[')[0];
        return field.Length == 0 ? "body" : JsonNamingPolicy.CamelCase.ConvertName(field);
    }

    private static IResult Invalid(IDictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors, title: "The request is invalid.");

    private static IResult NotFound(Guid id) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Discussion not found",
            detail: $"No discussion with id '{id}' exists.");
}
