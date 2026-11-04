using TheDiscussion.Core.Discussions;

namespace TheDiscussion.Core.Tests;

/// <summary>D-9 topic tag format and the parent-kind rules from AC-3.</summary>
public class DiscussionRulesTests
{
    [Theory]
    [InlineData("work")]
    [InlineData("work.remote")]
    [InlineData("a.b.c.d.e")]
    [InlineData("science-fiction.2024")]
    [InlineData("0")]
    public void Valid_topic_tags_are_accepted(string tag) => Assert.True(DiscussionRules.IsValidTopicTag(tag));

    [Theory]
    [InlineData("")]
    [InlineData("Work.remote")]
    [InlineData("work..remote")]
    [InlineData(".work")]
    [InlineData("work.")]
    [InlineData("a.b.c.d.e.f")]
    [InlineData("work_remote")]
    [InlineData("work remote")]
    [InlineData("work.remote\n")]
    [InlineData("wörk")]
    public void Invalid_topic_tags_are_rejected(string tag) => Assert.False(DiscussionRules.IsValidTopicTag(tag));

    [Theory]
    [InlineData(StatementKind.SupportGroup, StatementKind.Thesis)]
    [InlineData(StatementKind.SupportingStatement, StatementKind.SupportGroup)]
    [InlineData(StatementKind.Illustration, StatementKind.Thesis)]
    [InlineData(StatementKind.Illustration, StatementKind.SupportingStatement)]
    [InlineData(StatementKind.CounterArgument, StatementKind.Thesis)]
    [InlineData(StatementKind.CounterArgument, StatementKind.SupportingStatement)]
    [InlineData(StatementKind.Rebuttal, StatementKind.CounterArgument)]
    [InlineData(StatementKind.Rebuttal, StatementKind.Rebuttal)]
    public void Allowed_parent_kinds(StatementKind kind, StatementKind parent) => Assert.True(DiscussionRules.CanAttach(kind, parent));

    [Fact]
    public void Every_other_parent_kind_is_rejected()
    {
        var allowed = new HashSet<(StatementKind, StatementKind)>
        {
            (StatementKind.SupportGroup, StatementKind.Thesis),
            (StatementKind.SupportingStatement, StatementKind.SupportGroup),
            (StatementKind.Illustration, StatementKind.Thesis),
            (StatementKind.Illustration, StatementKind.SupportingStatement),
            (StatementKind.CounterArgument, StatementKind.Thesis),
            (StatementKind.CounterArgument, StatementKind.SupportingStatement),
            (StatementKind.Rebuttal, StatementKind.CounterArgument),
            (StatementKind.Rebuttal, StatementKind.Rebuttal),
        };

        foreach (var kind in Enum.GetValues<StatementKind>())
        {
            foreach (var parent in Enum.GetValues<StatementKind>())
            {
                Assert.Equal(allowed.Contains((kind, parent)), DiscussionRules.CanAttach(kind, parent));
            }
        }
    }

    [Fact]
    public void Illustrations_cannot_be_targeted_by_counters_or_rebuttals()
    {
        Assert.False(DiscussionRules.CanAttach(StatementKind.CounterArgument, StatementKind.Illustration));
        Assert.False(DiscussionRules.CanAttach(StatementKind.Rebuttal, StatementKind.Illustration));
    }
}
