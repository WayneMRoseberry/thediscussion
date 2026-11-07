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
    [InlineData(StatementKind.SupportGroup, StatementKind.SupportGroup)]
    [InlineData(StatementKind.SupportGroup, StatementKind.SupportingStatement)]
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
            (StatementKind.SupportGroup, StatementKind.SupportGroup),
            (StatementKind.SupportGroup, StatementKind.SupportingStatement),
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

    // ---- Nested-named-groups AC-1 (UC-1 decision points; Cluster A "Kind being added" x "Parent kind") ----

    public static TheoryData<StatementKind, StatementKind[]> ExpectedParentKinds => new()
    {
        { StatementKind.SupportGroup, [StatementKind.Thesis, StatementKind.SupportGroup, StatementKind.SupportingStatement] },
        { StatementKind.SupportingStatement, [StatementKind.SupportGroup] },
        { StatementKind.Illustration, [StatementKind.Thesis, StatementKind.SupportingStatement] },
        { StatementKind.CounterArgument, [StatementKind.Thesis, StatementKind.SupportingStatement] },
        { StatementKind.Rebuttal, [StatementKind.CounterArgument, StatementKind.Rebuttal] },
    };

    [Theory]
    [MemberData(nameof(ExpectedParentKinds))]
    public void Allowed_parent_kinds_of_every_addable_kind_are_exactly_the_listed_ones(StatementKind kind, StatementKind[] expected)
    {
        Assert.Equal(expected.Order(), DiscussionRules.AllowedParentKinds(kind).Order());

        foreach (var parent in Enum.GetValues<StatementKind>())
        {
            Assert.True(
                expected.Contains(parent) == DiscussionRules.CanAttach(kind, parent),
                $"CanAttach({kind}, {parent}) should be {expected.Contains(parent)}.");
        }
    }

    [Fact]
    public void A_support_group_has_three_allowed_parents_the_thesis_a_group_and_a_supporting_statement()
    {
        Assert.Equal(
            [StatementKind.Thesis, StatementKind.SupportGroup, StatementKind.SupportingStatement],
            DiscussionRules.AllowedParentKinds(StatementKind.SupportGroup).Order());
        Assert.False(DiscussionRules.CanAttach(StatementKind.SupportGroup, StatementKind.Illustration));
        Assert.False(DiscussionRules.CanAttach(StatementKind.SupportGroup, StatementKind.CounterArgument));
        Assert.False(DiscussionRules.CanAttach(StatementKind.SupportGroup, StatementKind.Rebuttal));
    }

    [Fact]
    public void Counter_arguments_and_illustrations_still_cannot_target_groups()
    {
        Assert.False(DiscussionRules.CanAttach(StatementKind.CounterArgument, StatementKind.SupportGroup));
        Assert.False(DiscussionRules.CanAttach(StatementKind.Illustration, StatementKind.SupportGroup));
        Assert.False(DiscussionRules.CanAttach(StatementKind.Rebuttal, StatementKind.SupportGroup));
        Assert.False(DiscussionRules.CanAttach(StatementKind.SupportingStatement, StatementKind.SupportingStatement));
        Assert.False(DiscussionRules.CanAttach(StatementKind.SupportingStatement, StatementKind.Thesis));
    }

    [Fact]
    public void The_thesis_can_never_be_attached()
    {
        Assert.Empty(DiscussionRules.AllowedParentKinds(StatementKind.Thesis));
    }
}
