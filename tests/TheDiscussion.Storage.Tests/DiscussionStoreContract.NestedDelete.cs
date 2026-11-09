using TheDiscussion.Core.Discussions;
using TheDiscussion.Core.Storage;

namespace TheDiscussion.Storage.Tests;

/// <summary>
/// nested-named-groups AC-5 (UC-5 main flow step 4 and A1; Cluster C "Delete target kind", "Subtree composition under
/// the delete target" and "Tree depth"): cascade delete of nested groups and sub-arguments, on every provider.
/// After each delete the deleted subtree is entirely gone, and the rest of the tree is intact, in creation order, with
/// its <c>parentId</c> values unchanged (the survivors compare equal to the statements as they were added).
/// </summary>
public abstract partial class DiscussionStoreContract
{
    [SkippableFact]
    public async Task Deleting_a_top_level_group_removes_its_whole_mixed_nested_subtree_and_nothing_else()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Delete a top-level group", "contract.nested-delete");
        await store.CreateDiscussionAsync(discussion, thesis);

        // Survivors are added before and after the doomed subtree, so the deleted rows are interleaved with kept ones.
        var keep = NamedGroup(discussion.Id, thesis.Id, SupportGroupType.AnySufficient, "Kept group");
        var keptSupport = Child(discussion.Id, StatementKind.SupportingStatement, keep.Id, "Kept support");
        var keptSubGroup = NamedGroup(discussion.Id, keep.Id, SupportGroupType.AllRequired, "Kept sub-group");
        await AddAllAsync(store, keep, keptSupport, keptSubGroup);
        var doomed = await AddMixedSubtreeAsync(store, discussion.Id, thesis.Id);
        var thesisCounter = Child(discussion.Id, StatementKind.CounterArgument, thesis.Id, "Kept thesis counter");
        var thesisRebuttal = Child(discussion.Id, StatementKind.Rebuttal, thesisCounter.Id, "Kept rebuttal");
        var thesisIllustration = Child(discussion.Id, StatementKind.Illustration, thesis.Id, "Kept illustration");
        await AddAllAsync(store, thesisCounter, thesisRebuttal, thesisIllustration);

        await store.DeleteStatementAsync(discussion.Id, doomed.Root.Id);

        var stored = await store.GetDiscussionAsync(discussion.Id);
        Assert.Equal(
            [thesis, keep, keptSupport, keptSubGroup, thesisCounter, thesisRebuttal, thesisIllustration],
            stored!.Statements);
        await AssertGoneAsync(store, discussion.Id, doomed.All);
    }

    [SkippableFact]
    public async Task Deleting_a_nested_group_in_the_middle_of_a_chain_removes_only_its_subtree()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Delete a nested group", "contract.nested-delete");
        await store.CreateDiscussionAsync(discussion, thesis);
        var tree = await AddMixedSubtreeAsync(store, discussion.Id, thesis.Id);

        // "sub" sits at depth 2 (under the top-level group); its ancestors and its siblings stay.
        await store.DeleteStatementAsync(discussion.Id, tree["sub"].Id);

        var doomed = tree.SubtreeOf("sub");
        Assert.Equal(["sub", "s3", "subsub", "s4", "sa2", "s5", "i2"], doomed.Select(tree.NameOf));
        var stored = await store.GetDiscussionAsync(discussion.Id);
        Assert.Equal([thesis, .. tree.All.Except(doomed)], stored!.Statements);
        await AssertGoneAsync(store, discussion.Id, doomed);
    }

    [SkippableFact]
    public async Task Deleting_a_supporting_statement_removes_its_sub_argument_group_and_everything_beneath_it()
    {
        // UC-5 A1.
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Delete a support with a sub-argument", "contract.nested-delete");
        await store.CreateDiscussionAsync(discussion, thesis);
        var tree = await AddMixedSubtreeAsync(store, discussion.Id, thesis.Id);

        await store.DeleteStatementAsync(discussion.Id, tree["s1"].Id);

        var doomed = tree.SubtreeOf("s1");
        Assert.Equal(["s1", "i1", "c1", "r1", "r2", "sa1", "s2", "c2"], doomed.Select(tree.NameOf));
        var stored = await store.GetDiscussionAsync(discussion.Id);
        Assert.Equal([thesis, .. tree.All.Except(doomed)], stored!.Statements);
        await AssertGoneAsync(store, discussion.Id, doomed);
    }

    [SkippableFact]
    public async Task Deleting_a_counter_a_rebuttal_and_an_illustration_inside_a_nested_tree_removes_only_their_subtrees()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Delete leaves of a nested tree", "contract.nested-delete");
        await store.CreateDiscussionAsync(discussion, thesis);
        var tree = await AddMixedSubtreeAsync(store, discussion.Id, thesis.Id);
        var remaining = tree.All.ToList();

        // c2: a counter on a support inside a sub-argument group. r1: a rebuttal with a rebuttal beneath it.
        // i2: an illustration at the bottom of the deepest branch.
        foreach (var (target, expectedDoomed) in new (string Target, string[] Doomed)[] { ("c2", ["c2"]), ("r1", ["r1", "r2"]), ("i2", ["i2"]) })
        {
            await store.DeleteStatementAsync(discussion.Id, tree[target].Id);

            var doomed = tree.SubtreeOf(target);
            Assert.Equal(expectedDoomed, doomed.Select(tree.NameOf));
            remaining.RemoveAll(doomed.Contains);
            var stored = await store.GetDiscussionAsync(discussion.Id);
            Assert.Equal([thesis, .. remaining], stored!.Statements);
            await AssertGoneAsync(store, discussion.Id, doomed);
        }
    }

    [SkippableFact]
    public async Task Deleting_a_subtree_that_spans_every_statement_but_the_thesis_leaves_only_the_thesis()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Delete everything but the thesis", "contract.nested-delete");
        await store.CreateDiscussionAsync(discussion, thesis);
        var tree = await AddMixedSubtreeAsync(store, discussion.Id, thesis.Id);
        Assert.Equal(tree.All.Count + 1, (await store.GetDiscussionAsync(discussion.Id))!.Statements.Count);

        await store.DeleteStatementAsync(discussion.Id, tree.Root.Id);

        var stored = await store.GetDiscussionAsync(discussion.Id);
        Assert.Equal([thesis], stored!.Statements);
        await AssertGoneAsync(store, discussion.Id, tree.All);
    }

    [SkippableTheory]
    [InlineData(1000)]
    [InlineData(1001)]
    public async Task Deleting_the_root_of_a_deep_subtree_removes_every_level(int levels)
    {
        // Cluster C "Tree depth (store, read, serialize, delete)": no depth limit (D-4), and no recursion that overflows.
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion($"Delete a {levels}-level subtree", "contract.nested-delete.deep");
        await store.CreateDiscussionAsync(discussion, thesis);
        var top = NamedGroup(discussion.Id, thesis.Id, SupportGroupType.AllRequired, "Top");
        var sibling = Child(discussion.Id, StatementKind.SupportingStatement, top.Id, "Kept sibling support");
        await AddAllAsync(store, top, sibling);
        var chain = await AddAlternatingChainAsync(store, discussion.Id, top.Id, levels);

        // The deep tree is stored and read back intact before the delete.
        var before = await store.GetDiscussionAsync(discussion.Id);
        Assert.Equal([thesis, top, sibling, .. chain], before!.Statements);

        await store.DeleteStatementAsync(discussion.Id, chain[0].Id);

        var after = await store.GetDiscussionAsync(discussion.Id);
        Assert.Equal([thesis, top, sibling], after!.Statements);
        await AssertGoneAsync(store, discussion.Id, [chain[0], chain[levels / 2], chain[^1]]);
    }

    [SkippableFact]
    public async Task Deleting_a_discussion_with_a_1000_level_tree_removes_it_and_every_statement()
    {
        var store = CreateStore();
        var (discussion, thesis) = NewDiscussion("Delete a deep discussion", "contract.nested-delete.deep");
        var (other, otherThesis) = NewDiscussion("Kept discussion", "contract.nested-delete.deep");
        await store.CreateDiscussionAsync(discussion, thesis);
        await store.CreateDiscussionAsync(other, otherThesis);
        var otherGroup = NamedGroup(other.Id, otherThesis.Id, SupportGroupType.AllRequired, "Kept");
        await store.AddStatementAsync(otherGroup);
        await AddAlternatingChainAsync(store, discussion.Id, thesis.Id, 1000);

        Assert.True(await store.DeleteDiscussionAsync(discussion.Id));

        Assert.Null(await store.GetDiscussionAsync(discussion.Id));
        Assert.Equal([otherThesis, otherGroup], (await store.GetDiscussionAsync(other.Id))!.Statements);
    }

    /// <summary>
    /// Adds this subtree under <paramref name="parentId"/> (the thesis), in this creation order:
    /// <code>
    /// root     group (allRequired)
    ///   s1       supporting statement
    ///     i1       illustration
    ///     c1       counter argument
    ///       r1       rebuttal
    ///         r2       rebuttal
    ///     sa1      sub-argument group (anySufficient)
    ///       s2       supporting statement
    ///         c2       counter argument
    ///   sub      sub-group (anySufficient)
    ///     s3       supporting statement
    ///     subsub   sub-group (allRequired)
    ///       s4       supporting statement
    ///         sa2      sub-argument group (allRequired)
    ///           s5       supporting statement
    ///             i2       illustration
    /// </code>
    /// </summary>
    private static async Task<StatementTree> AddMixedSubtreeAsync(IDiscussionStore store, Guid discussionId, Guid parentId)
    {
        var tree = new StatementTree();
        void AddGroup(string name, string? parent, SupportGroupType type) =>
            tree.Add(name, NamedGroup(discussionId, parent is null ? parentId : tree[parent].Id, type, $"Group {name}"));
        void AddChild(string name, StatementKind kind, string parent) =>
            tree.Add(name, Child(discussionId, kind, tree[parent].Id, $"Statement {name}"));

        AddGroup("root", null, SupportGroupType.AllRequired);
        AddChild("s1", StatementKind.SupportingStatement, "root");
        AddChild("i1", StatementKind.Illustration, "s1");
        AddChild("c1", StatementKind.CounterArgument, "s1");
        AddChild("r1", StatementKind.Rebuttal, "c1");
        AddChild("r2", StatementKind.Rebuttal, "r1");
        AddGroup("sa1", "s1", SupportGroupType.AnySufficient);
        AddChild("s2", StatementKind.SupportingStatement, "sa1");
        AddChild("c2", StatementKind.CounterArgument, "s2");
        AddGroup("sub", "root", SupportGroupType.AnySufficient);
        AddChild("s3", StatementKind.SupportingStatement, "sub");
        AddGroup("subsub", "sub", SupportGroupType.AllRequired);
        AddChild("s4", StatementKind.SupportingStatement, "subsub");
        AddGroup("sa2", "s4", SupportGroupType.AllRequired);
        AddChild("s5", StatementKind.SupportingStatement, "sa2");
        AddChild("i2", StatementKind.Illustration, "s5");

        await AddAllAsync(store, [.. tree.All]);
        return tree;
    }

    /// <summary>
    /// Adds a chain of <paramref name="levels"/> statements under <paramref name="parentId"/>, alternating a group and a
    /// supporting statement (the first is a group), each the parent of the next. Returns them in creation order.
    /// </summary>
    private static async Task<List<Statement>> AddAlternatingChainAsync(IDiscussionStore store, Guid discussionId, Guid parentId, int levels)
    {
        var chain = new List<Statement>(levels);
        for (var depth = 1; depth <= levels; depth++)
        {
            var statement = depth % 2 == 1
                ? NamedGroup(discussionId, parentId, depth % 4 == 1 ? SupportGroupType.AllRequired : SupportGroupType.AnySufficient, $"Group at depth {depth}")
                : Child(discussionId, StatementKind.SupportingStatement, parentId, $"Support at depth {depth}");
            await store.AddStatementAsync(statement);
            chain.Add(statement);
            parentId = statement.Id;
        }

        return chain;
    }

    private static async Task AddAllAsync(IDiscussionStore store, params Statement[] statements)
    {
        foreach (var statement in statements)
        {
            await store.AddStatementAsync(statement);
        }
    }

    /// <summary>Each deleted statement is gone from storage: deleting it again is not found, and nothing can be added under it.</summary>
    private static async Task AssertGoneAsync(IDiscussionStore store, Guid discussionId, IEnumerable<Statement> deleted)
    {
        foreach (var statement in deleted)
        {
            await Assert.ThrowsAsync<StatementNotFoundException>(() => store.DeleteStatementAsync(discussionId, statement.Id));
            await Assert.ThrowsAsync<ParentStatementNotFoundException>(() =>
                store.AddStatementAsync(Child(discussionId, StatementKind.Illustration, statement.Id, "Under a deleted statement")));
        }
    }

    /// <summary>Statements added by a test, by name, in creation order.</summary>
    private sealed class StatementTree
    {
        private readonly List<(string Name, Statement Statement)> entries = [];

        public Statement this[string name] => entries.Single(entry => entry.Name == name).Statement;

        public Statement Root => entries[0].Statement;

        public IReadOnlyList<Statement> All => entries.Select(entry => entry.Statement).ToList();

        public void Add(string name, Statement statement) => entries.Add((name, statement));

        public string NameOf(Statement statement) => entries.Single(entry => entry.Statement.Id == statement.Id).Name;

        /// <summary>The named statement and every statement beneath it, at any depth, in creation order.</summary>
        public IReadOnlyList<Statement> SubtreeOf(string name)
        {
            var ids = new HashSet<Guid> { this[name].Id };
            foreach (var (_, statement) in entries)
            {
                // Parents are added before their children, so one pass in creation order finds the whole subtree.
                if (statement.ParentId is { } parentId && ids.Contains(parentId))
                {
                    ids.Add(statement.Id);
                }
            }

            return entries.Select(entry => entry.Statement).Where(statement => ids.Contains(statement.Id)).ToList();
        }
    }
}
