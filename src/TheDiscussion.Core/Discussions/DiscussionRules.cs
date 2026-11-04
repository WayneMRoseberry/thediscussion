using System.Text.RegularExpressions;

namespace TheDiscussion.Core.Discussions;

/// <summary>Validation limits and structural rules for discussions and statements (D-9, D-10, AC-3).</summary>
public static partial class DiscussionRules
{
    public const int TitleMaxLength = 200;
    public const int StatementTextMaxLength = 5000;
    public const int TopicTagMaxSegments = 5;

    /// <summary>1–5 dot-separated lowercase segments of <c>[a-z0-9-]</c>, for example <c>work.remote</c>.</summary>
    public static bool IsValidTopicTag(string? topicTag) => topicTag is not null && TopicTagPattern().IsMatch(topicTag);

    /// <summary>The kinds of statement that a statement of <paramref name="kind"/> may be attached to.</summary>
    public static IReadOnlyList<StatementKind> AllowedParentKinds(StatementKind kind) => kind switch
    {
        StatementKind.SupportGroup => [StatementKind.Thesis],
        StatementKind.SupportingStatement => [StatementKind.SupportGroup],
        StatementKind.Illustration => [StatementKind.Thesis, StatementKind.SupportingStatement],
        StatementKind.CounterArgument => [StatementKind.Thesis, StatementKind.SupportingStatement],
        StatementKind.Rebuttal => [StatementKind.CounterArgument, StatementKind.Rebuttal],
        _ => [],
    };

    public static bool CanAttach(StatementKind kind, StatementKind parentKind) => AllowedParentKinds(kind).Contains(parentKind);

    /// <summary>Human-readable name of a statement kind, for messages.</summary>
    public static string Describe(StatementKind kind) => kind switch
    {
        StatementKind.Thesis => "the thesis",
        StatementKind.SupportGroup => "a support group",
        StatementKind.SupportingStatement => "a supporting statement",
        StatementKind.Illustration => "an illustration",
        StatementKind.CounterArgument => "a counter argument",
        StatementKind.Rebuttal => "a rebuttal",
        _ => kind.ToString(),
    };

    [GeneratedRegex(@"^[a-z0-9-]+(\.[a-z0-9-]+){0,4}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TopicTagPattern();
}
