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
}
