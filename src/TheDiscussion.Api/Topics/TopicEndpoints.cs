using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Topics;

namespace TheDiscussion.Api.Topics;

/// <summary>
/// A node of <c>GET /api/v1/topics</c>: the full <c>tag</c>, its last segment as <c>name</c>, the number of distinct
/// discussions tagged with it or a sub-tag (<c>discussionCount</c>), and its direct sub-tags sorted by name.
/// </summary>
public sealed record TopicResponse(string Tag, string Name, int DiscussionCount, IReadOnlyList<TopicResponse> Children)
{
    public static TopicResponse From(TopicNode node) =>
        new(node.Tag, node.Name, node.DiscussionCount, node.Children.Select(From).ToList());
}

/// <summary>REST endpoints under <c>/api/v1/topics</c>.</summary>
internal static class TopicEndpoints
{
    public const string BasePath = "/api/v1/topics";

    public static IEndpointRouteBuilder MapTopicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(BasePath, ListAsync);
        return app;
    }

    /// <summary>The tag tree, top-level topics sorted by name. Tags exist only through discussions, so it may be empty.</summary>
    private static async Task<IResult> ListAsync(DiscussionService service, CancellationToken cancellationToken)
    {
        var topics = await service.GetTopicsAsync(cancellationToken);
        return TypedResults.Ok(topics.Select(TopicResponse.From).ToList());
    }
}
