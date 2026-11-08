using System.Text.RegularExpressions;

namespace TheDiscussion.Core.Discussions;

/// <summary>Validation limits and structural rules for discussions and statements (D-9, D-10, AC-3).</summary>
public static partial class DiscussionRules
{
    public const int TitleMaxLength = 200;
    public const int StatementTextMaxLength = 5000;

    /// <summary>
    /// Longest support group name, counted after trimming in UTF-16 code units (<see cref="string.Length"/>) like the
    /// text limits, so an emoji made of a surrogate pair counts as 2.
    /// </summary>
    public const int GroupNameMaxLength = 100;

    public const int TopicTagMaxSegments = 5;
    public const int TopicTagsMin = 1;
    public const int TopicTagsMax = 5;

    /// <summary>
    /// The group name trimmed the same way statement text is, when it is valid: 1 to <see cref="GroupNameMaxLength"/>
    /// characters after trimming. Returns <c>null</c> when the name is missing, empty, whitespace-only or too long.
    /// </summary>
    public static string? NormalizeGroupName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > GroupNameMaxLength ? null : trimmed;
    }

    /// <summary>1–5 dot-separated lowercase segments of <c>[a-z0-9-]</c>, for example <c>work.remote</c>.</summary>
    public static bool IsValidTopicTag(string? topicTag) => topicTag is not null && TopicTagPattern().IsMatch(topicTag);

    /// <summary>
    /// Whether <paramref name="topicTag"/> is <paramref name="parentTag"/> itself or one of its sub-tags. Only whole
    /// segments match: <c>work.remote</c> is under <c>work</c>, but <c>work</c> is not under <c>wor</c>.
    /// </summary>
    public static bool IsAtOrUnder(string topicTag, string parentTag) =>
        topicTag.Length == parentTag.Length
            ? string.Equals(topicTag, parentTag, StringComparison.Ordinal)
            : topicTag.Length > parentTag.Length
              && topicTag[parentTag.Length] == '.'
              && topicTag.StartsWith(parentTag, StringComparison.Ordinal);

    /// <summary>
    /// The kinds of statement that a statement of <paramref name="kind"/> may be attached to. Only the parent's own kind
    /// matters, never its depth: support groups nest under groups and under supporting statements without limit (D-1, D-4).
    /// </summary>
    public static IReadOnlyList<StatementKind> AllowedParentKinds(StatementKind kind) => kind switch
    {
        StatementKind.SupportGroup => [StatementKind.Thesis, StatementKind.SupportGroup, StatementKind.SupportingStatement],
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
