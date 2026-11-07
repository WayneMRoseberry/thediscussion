using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Status;

namespace TheDiscussion.Core.Tests;

/// <summary>AC-4: argument status rules.</summary>
public class ArgumentStatusEngineTests
{
    private static ArgumentStatus Compute(TreeBuilder tree) => ArgumentStatusEngine.Compute(tree.Statements);

    // ---- Thesis statuses ----

    [Fact]
    public void Thesis_alone_is_unsupported()
    {
        var tree = new TreeBuilder();

        Assert.Equal(ThesisStatus.Unsupported, Compute(tree).Thesis);
    }

    [Fact]
    public void Thesis_with_only_an_empty_group_is_unsupported()
    {
        var tree = new TreeBuilder();
        tree.AllRequired();

        var status = Compute(tree);

        Assert.Equal(ThesisStatus.Unsupported, status.Thesis);
        Assert.Empty(status.Holds);
    }

    [Fact]
    public void Thesis_with_only_illustrations_is_unsupported()
    {
        var tree = new TreeBuilder();
        tree.Illustration(tree.Thesis);

        Assert.Equal(ThesisStatus.Unsupported, Compute(tree).Thesis);
    }

    [Fact]
    public void Unsupported_takes_precedence_over_an_unrefuted_counter_on_the_thesis()
    {
        var tree = new TreeBuilder();
        tree.Counter(tree.Thesis);

        Assert.Equal(ThesisStatus.Unsupported, Compute(tree).Thesis);
    }

    [Fact]
    public void Supported_thesis_without_counters_is_open()
    {
        var tree = new TreeBuilder();
        var support = tree.Support(tree.AllRequired());

        var status = Compute(tree);

        Assert.Equal(ThesisStatus.Open, status.Thesis);
        Assert.True(status.Stands[support.Id]);
    }

    [Fact]
    public void Unrefuted_counter_on_the_thesis_makes_it_countered()
    {
        var tree = new TreeBuilder();
        tree.Support(tree.AllRequired());
        var counter = tree.Counter(tree.Thesis);

        var status = Compute(tree);

        Assert.Equal(ThesisStatus.Countered, status.Thesis);
        Assert.False(status.Refuted[counter.Id]);
    }

    [Fact]
    public void Refuted_counter_on_the_thesis_makes_it_tested()
    {
        var tree = new TreeBuilder();
        tree.Support(tree.AllRequired());
        var counter = tree.Counter(tree.Thesis);
        var rebuttal = tree.Rebuttal(counter);

        var status = Compute(tree);

        Assert.Equal(ThesisStatus.Tested, status.Thesis);
        Assert.True(status.Refuted[counter.Id]);
        Assert.False(status.Refuted[rebuttal.Id]);
    }

    // ---- All-required groups ----

    [Fact]
    public void All_required_group_holds_when_every_support_stands()
    {
        var tree = new TreeBuilder();
        var group = tree.AllRequired();
        tree.Support(group, "a");
        tree.Support(group, "b");

        var status = Compute(tree);

        Assert.True(status.Holds[group.Id]);
        Assert.Equal(ThesisStatus.Open, status.Thesis);
    }

    [Fact]
    public void All_required_group_fails_when_one_support_has_an_unrefuted_counter()
    {
        var tree = new TreeBuilder();
        var group = tree.AllRequired();
        var standing = tree.Support(group, "standing");
        var countered = tree.Support(group, "countered");
        tree.Counter(countered);

        var status = Compute(tree);

        Assert.True(status.Stands[standing.Id]);
        Assert.False(status.Stands[countered.Id]);
        Assert.False(status.Holds[group.Id]);
        Assert.Equal(ThesisStatus.Countered, status.Thesis);
    }

    [Fact]
    public void All_required_group_holds_again_when_the_counter_is_refuted()
    {
        var tree = new TreeBuilder();
        var group = tree.AllRequired();
        var support = tree.Support(group);
        tree.Rebuttal(tree.Counter(support));

        var status = Compute(tree);

        Assert.True(status.Stands[support.Id]);
        Assert.True(status.Holds[group.Id]);
        Assert.Equal(ThesisStatus.Tested, status.Thesis);
    }

    // ---- Any-sufficient groups ----

    [Fact]
    public void Any_sufficient_group_with_one_standing_support_holds()
    {
        var tree = new TreeBuilder();
        var group = tree.AnySufficient();
        var support = tree.Support(group);

        var status = Compute(tree);

        Assert.True(status.Stands[support.Id]);
        Assert.True(status.Holds[group.Id]);
        Assert.Equal(ThesisStatus.Open, status.Thesis);
    }

    [Fact]
    public void Any_sufficient_group_with_one_countered_and_one_standing_support_is_tested()
    {
        // D-13: testing any item in an any-sufficient group counts as considering the whole group.
        var tree = new TreeBuilder();
        var group = tree.AnySufficient();
        var countered = tree.Support(group, "countered");
        var standing = tree.Support(group, "standing");
        var counter = tree.Counter(countered);

        var status = Compute(tree);

        Assert.False(status.Refuted[counter.Id]);
        Assert.False(status.Stands[countered.Id]);
        Assert.True(status.Stands[standing.Id]);
        Assert.True(status.Holds[group.Id]);
        Assert.Equal(ThesisStatus.Tested, status.Thesis);
    }

    [Fact]
    public void Any_sufficient_group_fails_when_every_support_is_countered()
    {
        var tree = new TreeBuilder();
        var group = tree.AnySufficient();
        tree.Counter(tree.Support(group, "a"));
        tree.Counter(tree.Support(group, "b"));

        var status = Compute(tree);

        Assert.False(status.Holds[group.Id]);
        Assert.Equal(ThesisStatus.Countered, status.Thesis);
    }

    [Fact]
    public void Any_non_empty_group_that_fails_makes_the_thesis_countered_even_if_another_holds()
    {
        var tree = new TreeBuilder();
        tree.Support(tree.AnySufficient());
        var failing = tree.AllRequired();
        tree.Counter(tree.Support(failing));

        var status = Compute(tree);

        Assert.False(status.Holds[failing.Id]);
        Assert.Equal(ThesisStatus.Countered, status.Thesis);
    }

    // ---- Empty groups and illustrations ----

    [Fact]
    public void Empty_group_is_ignored_next_to_a_holding_group()
    {
        var tree = new TreeBuilder();
        var empty = tree.AllRequired();
        var holding = tree.AnySufficient();
        tree.Support(holding);

        var status = Compute(tree);

        Assert.False(status.Holds.ContainsKey(empty.Id));
        Assert.True(status.Holds[holding.Id]);
        Assert.Equal(ThesisStatus.Open, status.Thesis);
    }

    [Fact]
    public void Illustrations_have_no_effect_on_status()
    {
        var plain = new TreeBuilder();
        var plainSupport = plain.Support(plain.AllRequired());
        plain.Rebuttal(plain.Counter(plain.Thesis));

        var illustrated = new TreeBuilder();
        var illustratedSupport = illustrated.Support(illustrated.AllRequired());
        illustrated.Rebuttal(illustrated.Counter(illustrated.Thesis));
        illustrated.Illustration(illustrated.Thesis);
        illustrated.Illustration(illustratedSupport);

        var withoutIllustrations = Compute(plain);
        var withIllustrations = Compute(illustrated);

        Assert.Equal(ThesisStatus.Tested, withoutIllustrations.Thesis);
        Assert.Equal(withoutIllustrations.Thesis, withIllustrations.Thesis);
        Assert.Equal(withoutIllustrations.Stands[plainSupport.Id], withIllustrations.Stands[illustratedSupport.Id]);
        Assert.DoesNotContain(
            illustrated.Statements.Where(s => s.Kind == StatementKind.Illustration),
            s => withIllustrations.Stands.ContainsKey(s.Id) || withIllustrations.Refuted.ContainsKey(s.Id));
    }

    [Fact]
    public void Illustration_alone_does_not_open_a_thesis_with_supports()
    {
        var tree = new TreeBuilder();
        var support = tree.Support(tree.AllRequired());
        tree.Illustration(support);

        Assert.Equal(ThesisStatus.Open, Compute(tree).Thesis);
    }

    // ---- Refutation at depth ----

    [Fact]
    public void Refutation_alternates_down_a_chain_of_four_rebuttals()
    {
        // counter <- r1 <- r2 <- r3 <- r4: r4 unrefuted, r3 refuted, r2 unrefuted, r1 refuted, counter unrefuted.
        var tree = new TreeBuilder();
        tree.Support(tree.AllRequired());
        var counter = tree.Counter(tree.Thesis);
        var r1 = tree.Rebuttal(counter);
        var r2 = tree.Rebuttal(r1);
        var r3 = tree.Rebuttal(r2);
        var r4 = tree.Rebuttal(r3);

        var status = Compute(tree);

        Assert.False(status.Refuted[r4.Id]);
        Assert.True(status.Refuted[r3.Id]);
        Assert.False(status.Refuted[r2.Id]);
        Assert.True(status.Refuted[r1.Id]);
        Assert.False(status.Refuted[counter.Id]);
        Assert.Equal(ThesisStatus.Countered, status.Thesis);
    }

    [Fact]
    public void A_fifth_rebuttal_flips_every_level_of_the_chain()
    {
        var tree = new TreeBuilder();
        tree.Support(tree.AllRequired());
        var counter = tree.Counter(tree.Thesis);
        var chain = new List<Statement> { counter };
        for (var depth = 1; depth <= 5; depth++)
        {
            chain.Add(tree.Rebuttal(chain[^1]));
        }

        var status = Compute(tree);

        // The deepest rebuttal is unrefuted; refutation alternates back up to the counter.
        for (var level = chain.Count - 1; level >= 0; level--)
        {
            var expectedRefuted = (chain.Count - 1 - level) % 2 == 1;
            Assert.Equal(expectedRefuted, status.Refuted[chain[level].Id]);
        }

        Assert.True(status.Refuted[counter.Id]);
        Assert.Equal(ThesisStatus.Tested, status.Thesis);
    }

    [Fact]
    public void Refutation_at_depth_reaches_counters_on_supporting_statements()
    {
        var tree = new TreeBuilder();
        var group = tree.AllRequired();
        var support = tree.Support(group);
        var counter = tree.Counter(support);
        var r1 = tree.Rebuttal(counter);
        var r2 = tree.Rebuttal(r1);
        var r3 = tree.Rebuttal(r2);
        tree.Rebuttal(r3);

        var status = Compute(tree);

        Assert.False(status.Refuted[counter.Id]);
        Assert.False(status.Stands[support.Id]);
        Assert.False(status.Holds[group.Id]);
        Assert.Equal(ThesisStatus.Countered, status.Thesis);
    }

    [Fact]
    public void One_unrefuted_rebuttal_is_enough_to_refute_even_if_others_are_refuted()
    {
        var tree = new TreeBuilder();
        tree.Support(tree.AllRequired());
        var counter = tree.Counter(tree.Thesis);
        var refutedRebuttal = tree.Rebuttal(counter, "refuted");
        tree.Rebuttal(refutedRebuttal, "answer");
        var standingRebuttal = tree.Rebuttal(counter, "standing");

        var status = Compute(tree);

        Assert.True(status.Refuted[refutedRebuttal.Id]);
        Assert.False(status.Refuted[standingRebuttal.Id]);
        Assert.True(status.Refuted[counter.Id]);
        Assert.Equal(ThesisStatus.Tested, status.Thesis);
    }

    [Fact]
    public void Very_deep_rebuttal_chains_do_not_overflow_the_stack()
    {
        var tree = new TreeBuilder();
        tree.Support(tree.AllRequired());
        var current = tree.Counter(tree.Thesis);
        const int depth = 20_000;
        for (var level = 0; level < depth; level++)
        {
            current = tree.Rebuttal(current);
        }

        var status = Compute(tree);

        // An even number of rebuttals below the counter: the counter is unrefuted.
        Assert.Equal(ThesisStatus.Countered, status.Thesis);
        Assert.False(status.Refuted[current.Id]);
    }

    [Fact]
    public void Every_supporting_statement_counter_and_rebuttal_gets_a_status_entry()
    {
        var tree = new TreeBuilder();
        var support = tree.Support(tree.AnySufficient());
        var counter = tree.Counter(support);
        var rebuttal = tree.Rebuttal(counter);
        var thesisCounter = tree.Counter(tree.Thesis);

        var status = Compute(tree);

        Assert.Equal([support.Id], status.Stands.Keys);
        Assert.Equal(new[] { counter.Id, rebuttal.Id, thesisCounter.Id }.Order(), status.Refuted.Keys.Order());
    }

    // =====================================================================================================
    // nested-named-groups AC-2 (UC-2): status over nested groups and sub-arguments at any depth.
    // =====================================================================================================

    // ---- UC-2 A2: failure through nesting ----

    [Fact]
    public void All_required_group_fails_through_a_nested_sub_group_that_does_not_hold()
    {
        var tree = new TreeBuilder();
        var parent = tree.AllRequired();
        var first = tree.Support(parent, "first");
        var second = tree.Support(parent, "second");
        var subGroup = tree.AllRequired(parent);
        var countered = tree.Support(subGroup, "countered");
        tree.Counter(countered);

        var status = Compute(tree);

        Assert.True(status.Stands[first.Id]);
        Assert.True(status.Stands[second.Id]);
        Assert.False(status.Stands[countered.Id]);
        Assert.False(status.Holds[subGroup.Id]);
        Assert.False(status.Holds[parent.Id]);
        Assert.Equal(ThesisStatus.Countered, status.Thesis);
    }

    // ---- UC-2 A1: rescue by an any-sufficient sub-group ----

    [Fact]
    public void Any_sufficient_sub_group_rescues_its_all_required_parent()
    {
        var tree = new TreeBuilder();
        var parent = tree.AllRequired();
        var direct = tree.Support(parent, "direct");
        var subGroup = tree.AnySufficient(parent);
        var standing = tree.Support(subGroup, "standing");
        var countered = tree.Support(subGroup, "countered");
        var counter = tree.Counter(countered);

        var status = Compute(tree);

        Assert.False(status.Refuted[counter.Id]);
        Assert.True(status.Stands[direct.Id]);
        Assert.True(status.Stands[standing.Id]);
        Assert.False(status.Stands[countered.Id]);
        Assert.True(status.Holds[subGroup.Id]);
        Assert.True(status.Holds[parent.Id]);
        Assert.Equal(ThesisStatus.Tested, status.Thesis);
    }

    // ---- UC-2 A3: a support knocked down by its own sub-argument ----

    [Fact]
    public void Support_is_knocked_down_by_its_own_failing_sub_argument()
    {
        var tree = new TreeBuilder();
        var group = tree.AllRequired();
        var support = tree.Support(group, "support without a counter of its own");
        var subArgument = tree.AllRequired(support);
        var countered = tree.Support(subArgument, "countered");
        tree.Counter(countered);

        var status = Compute(tree);

        Assert.False(status.Stands[countered.Id]);
        Assert.False(status.Holds[subArgument.Id]);
        Assert.False(status.Stands[support.Id]);
        Assert.False(status.Holds[group.Id]);
        Assert.Equal(ThesisStatus.Countered, status.Thesis);
    }

    [Fact]
    public void Support_stands_when_its_sub_argument_holds()
    {
        // Control case for UC-2 A3: the same tree, but the counter in the sub-argument is refuted.
        var tree = new TreeBuilder();
        var group = tree.AllRequired();
        var support = tree.Support(group, "support without a counter of its own");
        var subArgument = tree.AllRequired(support);
        var inner = tree.Support(subArgument, "countered then defended");
        tree.Rebuttal(tree.Counter(inner));

        var status = Compute(tree);

        Assert.True(status.Stands[inner.Id]);
        Assert.True(status.Holds[subArgument.Id]);
        Assert.True(status.Stands[support.Id]);
        Assert.True(status.Holds[group.Id]);
        Assert.Equal(ThesisStatus.Tested, status.Thesis);
    }

    // ---- UC-2 A4 and Cluster A "Empty vs non-empty sub-groups" ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Groups_holding_only_empty_sub_groups_are_ignored_and_the_thesis_is_unsupported(bool unrefutedCounterOnThesis)
    {
        var tree = new TreeBuilder();
        var first = tree.AllRequired();
        var firstChild = tree.AnySufficient(first);
        var firstGrandchild = tree.AllRequired(firstChild);
        var second = tree.AnySufficient();
        var secondChild = tree.AllRequired(second);
        var secondGrandchild = tree.AnySufficient(secondChild);
        tree.AllRequired(secondChild);
        if (unrefutedCounterOnThesis)
        {
            tree.Counter(tree.Thesis);
        }

        var status = Compute(tree);

        Assert.Empty(status.Holds);
        foreach (var group in new[] { first, firstChild, firstGrandchild, second, secondChild, secondGrandchild })
        {
            Assert.False(status.Holds.ContainsKey(group.Id));
        }

        Assert.Equal(ThesisStatus.Unsupported, status.Thesis);
    }

    [Fact]
    public void Empty_sub_group_does_not_count_against_its_all_required_parent()
    {
        var tree = new TreeBuilder();
        var parent = tree.AllRequired();
        var support = tree.Support(parent);
        var empty = tree.AllRequired(parent);

        var status = Compute(tree);

        Assert.True(status.Stands[support.Id]);
        Assert.False(status.Holds.ContainsKey(empty.Id));
        Assert.True(status.Holds[parent.Id]);
        Assert.Equal(ThesisStatus.Open, status.Thesis);
    }

    [Fact]
    public void Group_whose_only_child_is_an_empty_sub_group_is_itself_empty()
    {
        var tree = new TreeBuilder();
        var holding = tree.AllRequired();
        tree.Support(holding);
        var parentOfEmpty = tree.AllRequired();
        var empty = tree.AnySufficient(parentOfEmpty);

        var status = Compute(tree);

        Assert.False(status.Holds.ContainsKey(empty.Id));
        Assert.False(status.Holds.ContainsKey(parentOfEmpty.Id));
        Assert.True(status.Holds[holding.Id]);
        Assert.Equal(ThesisStatus.Open, status.Thesis);
    }

    // ---- Cluster A "Nesting depth" (1,000, 1,001, 10,000) and "Alternation of group and support levels" ----
    // A stack overflow kills the test host, so a passing run is the proof that no walk recurses over the tree.

    [Theory]
    [InlineData(1_000)]
    [InlineData(1_001)]
    [InlineData(10_000)]
    public void Deep_all_required_chain_with_a_standing_leaf_holds_at_every_level(int levels)
    {
        var tree = new TreeBuilder();
        var chain = GroupChain(tree, levels);
        var leaf = tree.Support(chain[^1]);

        var status = Compute(tree);

        Assert.True(status.Stands[leaf.Id]);
        Assert.Equal(levels, status.Holds.Count);
        Assert.All(chain, group => Assert.True(status.Holds[group.Id]));
        Assert.Equal(ThesisStatus.Open, status.Thesis);
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(1_001)]
    [InlineData(10_000)]
    public void Deep_all_required_chain_with_a_countered_leaf_fails_at_every_level(int levels)
    {
        var tree = new TreeBuilder();
        var chain = GroupChain(tree, levels);
        var leaf = tree.Support(chain[^1]);
        tree.Counter(leaf);

        var status = Compute(tree);

        Assert.False(status.Stands[leaf.Id]);
        Assert.All(chain, group => Assert.False(status.Holds[group.Id]));
        Assert.Equal(ThesisStatus.Countered, status.Thesis);
    }

    [Theory]
    [InlineData(1_000, false)]
    [InlineData(1_000, true)]
    [InlineData(1_001, false)]
    [InlineData(1_001, true)]
    public void Alternating_group_and_support_chain_has_correct_status_at_every_level(int pairs, bool counterAtTheBottom)
    {
        // group > support > group > support ... : 2 * pairs levels below the thesis, with a leaf support at the bottom.
        var tree = new TreeBuilder();
        var groups = new List<Statement>();
        var supports = new List<Statement>();
        Statement? parent = null;
        for (var level = 0; level < pairs; level++)
        {
            var type = level % 2 == 0 ? SupportGroupType.AllRequired : SupportGroupType.AnySufficient;
            var group = tree.Group(type, parent);
            var support = tree.Support(group);
            groups.Add(group);
            supports.Add(support);
            parent = support;
        }

        if (counterAtTheBottom)
        {
            tree.Counter(supports[^1]);
        }

        var status = Compute(tree);

        // Every group has exactly one member (the support below it), so each level mirrors the bottom outcome.
        var expected = !counterAtTheBottom;
        Assert.All(groups, group => Assert.Equal(expected, status.Holds[group.Id]));
        Assert.All(supports, support => Assert.Equal(expected, status.Stands[support.Id]));
        Assert.Equal(pairs, status.Holds.Count);
        Assert.Equal(pairs, status.Stands.Count);
        Assert.Equal(counterAtTheBottom ? ThesisStatus.Countered : ThesisStatus.Open, status.Thesis);
    }

    [Fact]
    public void Recursively_empty_chain_of_1000_levels_is_ignored_and_the_thesis_is_unsupported()
    {
        var tree = new TreeBuilder();
        GroupChain(tree, 1_000);

        var status = Compute(tree);

        Assert.Empty(status.Holds);
        Assert.Equal(ThesisStatus.Unsupported, status.Thesis);
    }

    [Fact]
    public void Single_leaf_support_at_the_bottom_of_a_1000_level_chain_makes_every_level_non_empty_and_holding()
    {
        var tree = new TreeBuilder();
        var chain = GroupChain(tree, 1_000, alternateTypes: true);
        tree.Support(chain[^1]);

        var status = Compute(tree);

        Assert.Equal(chain.Count, status.Holds.Count);
        Assert.All(chain, group => Assert.True(status.Holds[group.Id]));
        Assert.Equal(ThesisStatus.Open, status.Thesis);
    }

    // ---- Cluster A "Member mix of a group" and "groupType" ----

    [Theory]
    [InlineData("supports only", SupportGroupType.AllRequired)]
    [InlineData("supports only", SupportGroupType.AnySufficient)]
    [InlineData("non-empty sub-groups only", SupportGroupType.AllRequired)]
    [InlineData("non-empty sub-groups only", SupportGroupType.AnySufficient)]
    [InlineData("mixed", SupportGroupType.AllRequired)]
    [InlineData("mixed", SupportGroupType.AnySufficient)]
    [InlineData("exactly one support", SupportGroupType.AllRequired)]
    [InlineData("exactly one support", SupportGroupType.AnySufficient)]
    [InlineData("exactly one sub-group", SupportGroupType.AllRequired)]
    [InlineData("exactly one sub-group", SupportGroupType.AnySufficient)]
    public void Group_with_only_standing_members_holds_whatever_the_member_mix(string mix, SupportGroupType type)
    {
        var tree = new TreeBuilder();
        var top = tree.AllRequired();
        var group = tree.Group(type, top);
        var subGroups = new List<Statement>();
        switch (mix)
        {
            case "supports only":
                tree.Support(group, "a");
                tree.Support(group, "b");
                break;
            case "non-empty sub-groups only":
                subGroups.Add(SubGroupWithSupport(tree, group, standing: true));
                subGroups.Add(SubGroupWithSupport(tree, group, standing: true));
                break;
            case "mixed":
                tree.Support(group, "a");
                subGroups.Add(SubGroupWithSupport(tree, group, standing: true));
                break;
            case "exactly one support":
                tree.Support(group);
                break;
            case "exactly one sub-group":
                subGroups.Add(SubGroupWithSupport(tree, group, standing: true));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mix), mix, null);
        }

        var status = Compute(tree);

        Assert.All(subGroups, subGroup => Assert.True(status.Holds[subGroup.Id], mix));
        Assert.True(status.Holds[group.Id], mix);
        Assert.True(status.Holds[top.Id], mix);
        Assert.Equal(ThesisStatus.Open, status.Thesis);
    }

    [Theory]
    [InlineData("exactly one failing support", SupportGroupType.AllRequired)]
    [InlineData("exactly one failing support", SupportGroupType.AnySufficient)]
    [InlineData("exactly one failing sub-group", SupportGroupType.AllRequired)]
    [InlineData("exactly one failing sub-group", SupportGroupType.AnySufficient)]
    public void Group_whose_single_member_fails_does_not_hold(string mix, SupportGroupType type)
    {
        var tree = new TreeBuilder();
        var group = tree.Group(type, tree.AllRequired());
        if (mix == "exactly one failing support")
        {
            tree.Counter(tree.Support(group));
        }
        else
        {
            SubGroupWithSupport(tree, group, standing: false);
        }

        var status = Compute(tree);

        Assert.False(status.Holds[group.Id], mix);
        Assert.Equal(ThesisStatus.Countered, status.Thesis);
    }

    [Theory]
    [InlineData("standing support, failing support", SupportGroupType.AllRequired, false)]
    [InlineData("standing support, failing support", SupportGroupType.AnySufficient, true)]
    [InlineData("standing support, failing sub-group", SupportGroupType.AllRequired, false)]
    [InlineData("standing support, failing sub-group", SupportGroupType.AnySufficient, true)]
    [InlineData("holding sub-group, failing support", SupportGroupType.AllRequired, false)]
    [InlineData("holding sub-group, failing support", SupportGroupType.AnySufficient, true)]
    [InlineData("holding sub-group, failing sub-group", SupportGroupType.AllRequired, false)]
    [InlineData("holding sub-group, failing sub-group", SupportGroupType.AnySufficient, true)]
    public void One_standing_and_one_failing_member_holds_only_under_any_sufficient(string mix, SupportGroupType type, bool expectedHolds)
    {
        var tree = new TreeBuilder();
        var group = tree.Group(type);
        if (mix.StartsWith("standing support", StringComparison.Ordinal))
        {
            tree.Support(group, "standing");
        }
        else
        {
            SubGroupWithSupport(tree, group, standing: true);
        }

        if (mix.EndsWith("failing support", StringComparison.Ordinal))
        {
            tree.Counter(tree.Support(group, "failing"));
        }
        else
        {
            SubGroupWithSupport(tree, group, standing: false);
        }

        var status = Compute(tree);

        Assert.Equal(expectedHolds, status.Holds[group.Id]);
        Assert.Equal(expectedHolds ? ThesisStatus.Tested : ThesisStatus.Countered, status.Thesis);
    }

    [Fact]
    public void All_required_parent_with_an_any_sufficient_child_holds_when_the_child_holds_through_one_member()
    {
        var tree = new TreeBuilder();
        var parent = tree.AllRequired();
        var child = tree.AnySufficient(parent);
        tree.Support(child, "standing");
        tree.Counter(tree.Support(child, "countered"));

        var status = Compute(tree);

        Assert.True(status.Holds[child.Id]);
        Assert.True(status.Holds[parent.Id]);
        Assert.Equal(ThesisStatus.Tested, status.Thesis);
    }

    [Fact]
    public void Any_sufficient_parent_with_an_all_required_child_depends_on_its_other_members()
    {
        var rescued = new TreeBuilder();
        var rescuedParent = rescued.AnySufficient();
        var rescuedChild = rescued.AllRequired(rescuedParent);
        rescued.Support(rescuedChild, "standing");
        rescued.Counter(rescued.Support(rescuedChild, "countered"));
        rescued.Support(rescuedParent, "standing sibling");

        var alone = new TreeBuilder();
        var aloneParent = alone.AnySufficient();
        var aloneChild = alone.AllRequired(aloneParent);
        alone.Support(aloneChild, "standing");
        alone.Counter(alone.Support(aloneChild, "countered"));

        var rescuedStatus = Compute(rescued);
        var aloneStatus = Compute(alone);

        Assert.False(rescuedStatus.Holds[rescuedChild.Id]);
        Assert.True(rescuedStatus.Holds[rescuedParent.Id]);
        Assert.Equal(ThesisStatus.Tested, rescuedStatus.Thesis);
        Assert.False(aloneStatus.Holds[aloneChild.Id]);
        Assert.False(aloneStatus.Holds[aloneParent.Id]);
        Assert.Equal(ThesisStatus.Countered, aloneStatus.Thesis);
    }

    // ---- Cluster A "Counter on a supporting statement", on a support inside a group at depth 3 ----

    [Theory]
    [InlineData("no counter", true)]
    [InlineData("an unrefuted counter", false, 0)]
    [InlineData("a counter refuted by an unrefuted rebuttal", true, 1)]
    [InlineData("several counters, one unrefuted", false, 1, 0, 3)]
    [InlineData("several counters, all refuted", true, 1, 3, 5)]
    [InlineData("rebuttal chain of even length 4", false, 4)]
    [InlineData("rebuttal chain of odd length 5", true, 5)]
    [InlineData("deep rebuttal chain of even length 2,000", false, 2_000)]
    [InlineData("deep rebuttal chain of odd length 2,001", true, 2_001)]
    public void Counters_on_a_nested_supporting_statement_decide_whether_it_stands(string scenario, bool expectedStands, params int[] rebuttalChains)
    {
        var tree = new TreeBuilder();
        var chain = GroupChain(tree, 3);
        var support = tree.Support(chain[^1]);
        foreach (var length in rebuttalChains)
        {
            RebuttalChain(tree, tree.Counter(support), length);
        }

        var status = Compute(tree);

        Assert.True(expectedStands == status.Stands[support.Id], scenario);
        Assert.All(chain, group => Assert.Equal(expectedStands, status.Holds[group.Id]));
        var expectedThesis = !expectedStands
            ? ThesisStatus.Countered
            : rebuttalChains.Length > 0 ? ThesisStatus.Tested : ThesisStatus.Open;
        Assert.Equal(expectedThesis, status.Thesis);
    }

    // ---- Cluster A "Counter on the thesis", with nested groups ----

    [Fact]
    public void One_unrefuted_counter_among_refuted_ones_on_the_thesis_makes_a_nested_tree_countered()
    {
        var tree = new TreeBuilder();
        var chain = GroupChain(tree, 3);
        tree.Support(chain[^1]);
        tree.Rebuttal(tree.Counter(tree.Thesis, "refuted 1"));
        tree.Rebuttal(tree.Counter(tree.Thesis, "refuted 2"));
        var unrefuted = tree.Counter(tree.Thesis, "unrefuted");

        var status = Compute(tree);

        Assert.False(status.Refuted[unrefuted.Id]);
        Assert.All(chain, group => Assert.True(status.Holds[group.Id]));
        Assert.Equal(ThesisStatus.Countered, status.Thesis);
    }

    [Fact]
    public void All_counters_on_the_thesis_refuted_and_every_direct_group_holding_makes_a_nested_tree_tested()
    {
        var tree = new TreeBuilder();
        var first = GroupChain(tree, 3);
        tree.Support(first[^1]);
        var second = tree.AnySufficient();
        SubGroupWithSupport(tree, second, standing: true);
        tree.Rebuttal(tree.Counter(tree.Thesis, "refuted 1"));
        RebuttalChain(tree, tree.Counter(tree.Thesis, "refuted 2"), 3);

        var status = Compute(tree);

        Assert.True(status.Holds[first[0].Id]);
        Assert.True(status.Holds[second.Id]);
        Assert.Equal(ThesisStatus.Tested, status.Thesis);
    }

    // ---- Cluster A "Sub-argument outcome under a support" ----

    [Theory]
    [InlineData("no sub-argument", true)]
    [InlineData("all non-empty sub-arguments hold", true)]
    [InlineData("one fails among several that hold", false)]
    [InlineData("only empty sub-arguments", true)]
    [InlineData("empty sub-argument next to a holding one", true)]
    public void Sub_arguments_decide_whether_their_supporting_statement_stands(string scenario, bool expectedStands)
    {
        var tree = new TreeBuilder();
        var group = tree.AllRequired(tree.AnySufficient());
        var support = tree.Support(group);
        var subArguments = new List<Statement>();
        switch (scenario)
        {
            case "no sub-argument":
                break;
            case "all non-empty sub-arguments hold":
                subArguments.Add(SubGroupWithSupport(tree, support, standing: true));
                subArguments.Add(SubGroupWithSupport(tree, support, standing: true, SupportGroupType.AnySufficient));
                break;
            case "one fails among several that hold":
                subArguments.Add(SubGroupWithSupport(tree, support, standing: true));
                subArguments.Add(SubGroupWithSupport(tree, support, standing: false));
                subArguments.Add(SubGroupWithSupport(tree, support, standing: true, SupportGroupType.AnySufficient));
                break;
            case "only empty sub-arguments":
                tree.AllRequired(support);
                tree.AnySufficient(tree.AnySufficient(support));
                break;
            case "empty sub-argument next to a holding one":
                tree.AllRequired(support);
                subArguments.Add(SubGroupWithSupport(tree, support, standing: true));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        var status = Compute(tree);

        Assert.True(expectedStands == status.Stands[support.Id], scenario);
        Assert.True(expectedStands == status.Holds[group.Id], scenario);
        Assert.Equal(subArguments.Count, status.Holds.Keys.Intersect(subArguments.Select(subArgument => subArgument.Id)).Count());
        Assert.Equal(expectedStands ? ThesisStatus.Open : ThesisStatus.Countered, status.Thesis);
    }

    // ---- Cluster A "Counter anywhere in tree" ----

    [Fact]
    public void Exactly_one_refuted_counter_deep_in_the_tree_makes_the_thesis_tested()
    {
        var tree = new TreeBuilder();
        var chain = GroupChain(tree, 5, alternateTypes: true);
        var support = tree.Support(chain[^1]);
        var counter = tree.Counter(support);
        tree.Rebuttal(counter);

        var status = Compute(tree);

        Assert.True(status.Refuted[counter.Id]);
        Assert.All(chain, group => Assert.True(status.Holds[group.Id]));
        Assert.Equal(ThesisStatus.Tested, status.Thesis);
    }

    [Fact]
    public void No_counters_anywhere_in_a_nested_tree_leaves_the_thesis_open()
    {
        var tree = new TreeBuilder();
        var chain = GroupChain(tree, 5, alternateTypes: true);
        var support = tree.Support(chain[^1]);
        SubGroupWithSupport(tree, support, standing: true);

        Assert.Equal(ThesisStatus.Open, Compute(tree).Thesis);
    }

    [Fact]
    public void Unrefuted_counter_deep_in_a_holding_any_sufficient_group_still_gives_tested()
    {
        // D-13 at depth: the countered support is rescued by its standing sibling.
        var tree = new TreeBuilder();
        var chain = GroupChain(tree, 4);
        var anySufficient = tree.AnySufficient(chain[^1]);
        var countered = tree.Support(anySufficient, "countered");
        tree.Support(anySufficient, "standing");
        var counter = tree.Counter(countered);

        var status = Compute(tree);

        Assert.False(status.Refuted[counter.Id]);
        Assert.False(status.Stands[countered.Id]);
        Assert.True(status.Holds[anySufficient.Id]);
        Assert.All(chain, group => Assert.True(status.Holds[group.Id]));
        Assert.Equal(ThesisStatus.Tested, status.Thesis);
    }

    // ---- UC-2 A5 and Cluster A "Illustrations present" ----

    [Fact]
    public void Illustrations_on_the_thesis_and_deep_supports_do_not_change_any_nested_status()
    {
        var tree = new TreeBuilder();
        var chain = GroupChain(tree, 4, alternateTypes: true);
        var deep = tree.Support(chain[^1], "deep");
        var subArgument = tree.AllRequired(deep);
        var deeper = tree.Support(subArgument, "deeper");
        tree.Rebuttal(tree.Counter(deeper));
        var countered = tree.Support(tree.AnySufficient(chain[1]), "countered");
        tree.Counter(countered);
        for (var index = 0; index < 5; index++)
        {
            tree.Illustration(tree.Thesis);
            tree.Illustration(deep);
            tree.Illustration(deeper);
            tree.Illustration(countered);
        }

        var withIllustrations = Compute(tree);
        var withoutIllustrations = ArgumentStatusEngine.Compute(
            tree.Statements.Where(statement => statement.Kind != StatementKind.Illustration).ToList());

        AssertSameStatus(withoutIllustrations, withIllustrations);
        Assert.DoesNotContain(
            tree.Statements.Where(statement => statement.Kind == StatementKind.Illustration),
            statement => withIllustrations.Stands.ContainsKey(statement.Id)
                || withIllustrations.Refuted.ContainsKey(statement.Id)
                || withIllustrations.Holds.ContainsKey(statement.Id));
    }

    // ---- Cluster A "Thesis status": every status with nested trees, and single-statement transitions ----

    [Fact]
    public void Adding_one_statement_at_a_time_moves_a_nested_tree_through_every_thesis_status()
    {
        var tree = new TreeBuilder();
        var chain = GroupChain(tree, 4);
        Assert.Equal(ThesisStatus.Unsupported, Compute(tree).Thesis);

        var support = tree.Support(chain[^1]);
        Assert.Equal(ThesisStatus.Open, Compute(tree).Thesis);

        var counter = tree.Counter(support);
        var countered = Compute(tree);
        Assert.False(countered.Stands[support.Id]);
        Assert.All(chain, group => Assert.False(countered.Holds[group.Id]));
        Assert.Equal(ThesisStatus.Countered, countered.Thesis);

        tree.Rebuttal(counter);
        var tested = Compute(tree);
        Assert.True(tested.Stands[support.Id]);
        Assert.All(chain, group => Assert.True(tested.Holds[group.Id]));
        Assert.Equal(ThesisStatus.Tested, tested.Thesis);
    }

    // ---- Cluster A "holds presence": true, false and absent at depth 3 or more and on sub-argument groups ----

    [Fact]
    public void Holds_is_true_false_or_absent_on_deep_groups_and_sub_argument_groups()
    {
        var tree = new TreeBuilder();
        var depth1 = tree.AnySufficient();
        var depth2 = tree.AnySufficient(depth1);
        var holdingAtDepth3 = tree.AllRequired(depth2);
        var support = tree.Support(holdingAtDepth3, "support with sub-arguments");
        var failingAtDepth3 = tree.AllRequired(depth2);
        tree.Counter(tree.Support(failingAtDepth3, "countered"));
        var emptyAtDepth3 = tree.AllRequired(depth2);
        var emptyAtDepth4 = tree.AnySufficient(emptyAtDepth3);
        var holdingSubArgument = SubGroupWithSupport(tree, support, standing: true);
        var emptySubArgument = tree.AllRequired(support);
        var knockedDown = tree.Support(depth2, "knocked down");
        var failingSubArgument = SubGroupWithSupport(tree, knockedDown, standing: false);

        var status = Compute(tree);

        Assert.True(status.Holds[holdingAtDepth3.Id]);
        Assert.False(status.Holds[failingAtDepth3.Id]);
        Assert.False(status.Holds.ContainsKey(emptyAtDepth3.Id));
        Assert.False(status.Holds.ContainsKey(emptyAtDepth4.Id));
        Assert.True(status.Holds[holdingSubArgument.Id]);
        Assert.False(status.Holds[failingSubArgument.Id]);
        Assert.False(status.Holds.ContainsKey(emptySubArgument.Id));
        Assert.True(status.Stands[support.Id]);
        Assert.False(status.Stands[knockedDown.Id]);
        Assert.True(status.Holds[depth2.Id]);
        Assert.True(status.Holds[depth1.Id]);
        Assert.Equal(ThesisStatus.Tested, status.Thesis);
    }

    // ---- UC-2 postconditions: deterministic and order-independent; UC-2 E1 defensive cycle guard ----

    [Fact]
    public void Computing_a_nested_tree_twice_or_in_another_order_gives_the_same_result()
    {
        var tree = new TreeBuilder();
        var chain = GroupChain(tree, 6, alternateTypes: true);
        var deep = tree.Support(chain[^1]);
        SubGroupWithSupport(tree, deep, standing: false);
        tree.Support(chain[2], "sibling");
        var rescue = tree.AnySufficient(chain[1]);
        tree.Support(rescue, "standing");
        RebuttalChain(tree, tree.Counter(tree.Support(rescue, "countered")), 4);
        tree.AllRequired(chain[3]);
        RebuttalChain(tree, tree.Counter(tree.Thesis), 3);
        tree.Illustration(deep);

        var random = new Random(20261107);
        var first = Compute(tree);
        var second = Compute(tree);
        var reversed = ArgumentStatusEngine.Compute(tree.Statements.Reverse().ToList());
        var shuffled = ArgumentStatusEngine.Compute(tree.Statements.OrderBy(_ => random.Next()).ToList());

        AssertSameStatus(first, second);
        AssertSameStatus(first, reversed);
        AssertSameStatus(first, shuffled);
    }

    [Fact]
    public void Malformed_parent_cycle_among_groups_does_not_loop_forever()
    {
        // The API never produces a cycle; the engine still guards against one, as the refutation pass does.
        var tree = new TreeBuilder();
        var group = tree.AllRequired();
        var support = tree.Support(group);
        var statements = tree.Statements.Append(group with { ParentId = support.Id }).ToList();

        var status = ArgumentStatusEngine.Compute(statements);

        Assert.True(status.Stands[support.Id]);
        Assert.True(status.Holds[group.Id]);
        Assert.Equal(ThesisStatus.Open, status.Thesis);
    }

    // ---- Helpers for the nested-group tests ----

    /// <summary>A chain of <paramref name="levels"/> groups, the first directly under the thesis, each inside the previous one.</summary>
    private static List<Statement> GroupChain(TreeBuilder tree, int levels, bool alternateTypes = false)
    {
        var chain = new List<Statement>(levels);
        Statement? parent = null;
        for (var level = 0; level < levels; level++)
        {
            var type = alternateTypes && level % 2 == 1 ? SupportGroupType.AnySufficient : SupportGroupType.AllRequired;
            parent = tree.Group(type, parent);
            chain.Add(parent);
        }

        return chain;
    }

    /// <summary>A group under <paramref name="parent"/> holding one support that stands, or one with an unrefuted counter.</summary>
    private static Statement SubGroupWithSupport(TreeBuilder tree, Statement parent, bool standing, SupportGroupType type = SupportGroupType.AllRequired)
    {
        var group = tree.Group(type, parent);
        var support = tree.Support(group, standing ? "standing" : "countered");
        if (!standing)
        {
            tree.Counter(support);
        }

        return group;
    }

    /// <summary>Adds <paramref name="length"/> rebuttals in a chain below <paramref name="counter"/>.</summary>
    private static void RebuttalChain(TreeBuilder tree, Statement counter, int length)
    {
        var current = counter;
        for (var level = 0; level < length; level++)
        {
            current = tree.Rebuttal(current);
        }
    }

    private static void AssertSameStatus(ArgumentStatus expected, ArgumentStatus actual)
    {
        Assert.Equal(expected.Thesis, actual.Thesis);
        Assert.Equal(expected.Stands.OrderBy(entry => entry.Key), actual.Stands.OrderBy(entry => entry.Key));
        Assert.Equal(expected.Refuted.OrderBy(entry => entry.Key), actual.Refuted.OrderBy(entry => entry.Key));
        Assert.Equal(expected.Holds.OrderBy(entry => entry.Key), actual.Holds.OrderBy(entry => entry.Key));
    }
}
