using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Topics;

namespace TheDiscussion.Core.Tests;

/// <summary>discussion-crud-and-tags AC-5: the topic hierarchy and its distinct-discussion counts.</summary>
public class TopicTreeTests
{
    [Fact]
    public void No_discussions_give_no_topics() => Assert.Empty(TopicTree.Build([]));

    [Fact]
    public void The_reference_hierarchy_is_built_with_counts_and_children_sorted_by_name()
    {
        var topics = TopicTree.Build(
        [
            Discussion("work.remote", "management"),
            Discussion("work.remote"),
            Discussion("work.remote.tools", "work.remote"),
            Discussion("work.remote"),
            Discussion("work.hours"),
            Discussion("work.hours", "science.climate"),
            Discussion("work"),
            Discussion("science.climate"),
            Discussion("science.climate"),
        ]);

        Assert.Equal(
            [
                Node("management", 1),
                Node("science", 3, Node("science.climate", 3)),
                Node("work", 7, Node("work.hours", 2), Node("work.remote", 4, Node("work.remote.tools", 1))),
            ],
            topics,
            TopicComparer.Instance);
    }

    [Fact]
    public void A_segment_used_only_as_a_prefix_is_still_a_node()
    {
        var topics = TopicTree.Build([Discussion("work.remote.tools")]);

        var work = Assert.Single(topics);
        Assert.Equal(("work", "work", 1), (work.Tag, work.Name, work.DiscussionCount));
        var remote = Assert.Single(work.Children);
        Assert.Equal(("work.remote", "remote", 1), (remote.Tag, remote.Name, remote.DiscussionCount));
        var tools = Assert.Single(remote.Children);
        Assert.Equal(("work.remote.tools", "tools", 1), (tools.Tag, tools.Name, tools.DiscussionCount));
        Assert.Empty(tools.Children);
    }

    [Fact]
    public void A_discussion_with_two_tags_under_one_parent_is_counted_once_there()
    {
        var topics = TopicTree.Build([Discussion("work.remote", "work.hours")]);

        var work = Assert.Single(topics);
        Assert.Equal(1, work.DiscussionCount);
        Assert.Equal([("hours", 1), ("remote", 1)], work.Children.Select(child => (child.Name, child.DiscussionCount)));
    }

    [Fact]
    public void Names_sort_ordinally_and_similar_prefixes_stay_separate()
    {
        var topics = TopicTree.Build([Discussion("works"), Discussion("work-life"), Discussion("work"), Discussion("2024")]);

        Assert.Equal(["2024", "work", "work-life", "works"], topics.Select(topic => topic.Name));
        Assert.All(topics, topic => Assert.Equal(1, topic.DiscussionCount));
    }

    private static DiscussionSummary Discussion(params string[] tags) =>
        new(Guid.NewGuid(), "Title", tags, DateTimeOffset.UnixEpoch);

    private static TopicNode Node(string tag, int count, params TopicNode[] children) =>
        new(tag, TopicTree.NameOf(tag), count, children);

    private sealed class TopicComparer : IEqualityComparer<TopicNode>
    {
        public static TopicComparer Instance { get; } = new();

        public bool Equals(TopicNode? x, TopicNode? y) =>
            x is not null && y is not null
            && x.Tag == y.Tag && x.Name == y.Name && x.DiscussionCount == y.DiscussionCount
            && x.Children.SequenceEqual(y.Children, this);

        public int GetHashCode(TopicNode obj) => obj.Tag.GetHashCode(StringComparison.Ordinal);
    }
}
