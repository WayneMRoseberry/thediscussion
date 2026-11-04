using TheDiscussion.Core.Discussions;

namespace TheDiscussion.Core.Topics;

/// <summary>
/// One node of the topic hierarchy. <see cref="Tag"/> is the full tag (<c>work.remote</c>), <see cref="Name"/> its last
/// segment (<c>remote</c>), <see cref="DiscussionCount"/> the number of distinct discussions tagged with it or any of its
/// sub-tags, and <see cref="Children"/> its direct sub-tags sorted by name.
/// </summary>
public sealed record TopicNode(string Tag, string Name, int DiscussionCount, IReadOnlyList<TopicNode> Children);

/// <summary>
/// Builds the topic hierarchy from the discussions' tags. Tags exist only through discussions: a segment used only as a
/// prefix (<c>work</c> when only <c>work.remote</c> is used) still appears as a node, and a discussion with several tags
/// under one node is counted once there.
/// </summary>
public static class TopicTree
{
    /// <returns>The top-level topics sorted by name, each with its sub-topics.</returns>
    public static IReadOnlyList<TopicNode> Build(IEnumerable<DiscussionSummary> discussions)
    {
        ArgumentNullException.ThrowIfNull(discussions);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var discussion in discussions)
        {
            // Every tag at or above one of this discussion's tags, once per discussion.
            var reached = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in discussion.TopicTags)
            {
                for (var end = tag.IndexOf('.'); end >= 0; end = tag.IndexOf('.', end + 1))
                {
                    reached.Add(tag[..end]);
                }

                reached.Add(tag);
            }

            foreach (var tag in reached)
            {
                counts[tag] = counts.GetValueOrDefault(tag) + 1;
            }
        }

        var childrenOf = counts.Keys
            .Where(tag => tag.Contains('.'))
            .ToLookup(tag => tag[..tag.LastIndexOf('.')], StringComparer.Ordinal);

        IReadOnlyList<TopicNode> NodesFor(IEnumerable<string> tags) => tags
            .Select(tag => (Tag: tag, Name: NameOf(tag)))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(entry => new TopicNode(entry.Tag, entry.Name, counts[entry.Tag], NodesFor(childrenOf[entry.Tag])))
            .ToList();

        return NodesFor(counts.Keys.Where(tag => !tag.Contains('.')));
    }

    /// <summary>The last segment of a tag.</summary>
    public static string NameOf(string tag) => tag[(tag.LastIndexOf('.') + 1)..];
}
