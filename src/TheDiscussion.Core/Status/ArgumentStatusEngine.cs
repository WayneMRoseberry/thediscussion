using TheDiscussion.Core.Discussions;

namespace TheDiscussion.Core.Status;

/// <summary>Status of a discussion's thesis.</summary>
public enum ThesisStatus
{
    /// <summary>Every support group directly under the thesis is empty (recursively), including when there are none.</summary>
    Unsupported,

    /// <summary>The thesis is supported and no counter argument exists anywhere in the tree.</summary>
    Open,

    /// <summary>An unrefuted counter argument targets the thesis, or a non-empty group directly under it does not hold.</summary>
    Countered,

    /// <summary>The thesis is supported, has been countered somewhere, and every non-empty group directly under it holds.</summary>
    Tested,
}

/// <summary>Computed status of every node that has one. Never stored; recomputed on every read.</summary>
/// <param name="Thesis">Status of the thesis.</param>
/// <param name="Stands">
/// Per supporting statement: <c>true</c> when no unrefuted counter argument targets it and every non-empty support
/// group directly beneath it (its sub-arguments) holds.
/// </param>
/// <param name="Refuted">Per counter argument and rebuttal: <c>true</c> when an unrefuted rebuttal targets it.</param>
/// <param name="Holds">Per non-empty support group at any depth: whether it holds. Empty groups have no entry (they are ignored).</param>
public sealed record ArgumentStatus(
    ThesisStatus Thesis,
    IReadOnlyDictionary<Guid, bool> Stands,
    IReadOnlyDictionary<Guid, bool> Refuted,
    IReadOnlyDictionary<Guid, bool> Holds);

/// <summary>
/// Pure function computing argument status from a discussion's statements.
/// <list type="bullet">
/// <item>A counter argument or rebuttal is refuted when at least one rebuttal targeting it is not itself refuted (recursively, any depth).</item>
/// <item>A group's members are its supporting statements and its non-empty sub-groups. A group without members is empty,
/// applied recursively, so a group holding only empty sub-groups is empty. Empty groups are ignored.</item>
/// <item>An all-required group holds when every member stands or holds; an any-sufficient group when at least one does.</item>
/// <item>A supporting statement stands when no unrefuted counter argument targets it and every non-empty support group
/// directly beneath it holds.</item>
/// <item>Illustrations never affect status.</item>
/// <item>Thesis: Unsupported when every group directly under it is empty; otherwise Countered when an unrefuted counter
/// argument targets the thesis or a non-empty group directly under it does not hold; otherwise Tested when any counter
/// argument exists; otherwise Open.</item>
/// </list>
/// Every walk over the tree uses an explicit stack, so nesting of any depth cannot overflow the call stack.
/// </summary>
public static class ArgumentStatusEngine
{
    public static ArgumentStatus Compute(IReadOnlyCollection<Statement> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        var thesis = statements.Single(statement => statement.Kind == StatementKind.Thesis);
        var children = statements
            .Where(statement => statement.ParentId is not null)
            .ToLookup(statement => statement.ParentId!.Value);

        var refuted = ComputeRefutation(statements, children);
        var (stands, holds) = ComputeSupport(thesis, children, refuted);

        // Outcomes of the non-empty groups directly under the thesis; empty groups have no holds entry.
        var directOutcomes = children[thesis.Id]
            .Where(child => child.Kind == StatementKind.SupportGroup && holds.ContainsKey(child.Id))
            .Select(group => holds[group.Id])
            .ToList();

        ThesisStatus status;
        if (directOutcomes.Count == 0)
        {
            status = ThesisStatus.Unsupported;
        }
        else if (HasUnrefutedCounter(thesis.Id, children, refuted) || directOutcomes.Any(groupHolds => !groupHolds))
        {
            status = ThesisStatus.Countered;
        }
        else if (statements.Any(statement => statement.Kind == StatementKind.CounterArgument))
        {
            status = ThesisStatus.Tested;
        }
        else
        {
            status = ThesisStatus.Open;
        }

        return new ArgumentStatus(status, stands, refuted, holds);
    }

    private static bool HasUnrefutedCounter(Guid targetId, ILookup<Guid, Statement> children, IReadOnlyDictionary<Guid, bool> refuted) =>
        children[targetId].Any(child => child.Kind == StatementKind.CounterArgument && !refuted[child.Id]);

    /// <summary>
    /// <c>stands</c> for every supporting statement and <c>holds</c> for every non-empty group reachable from the thesis
    /// through groups, supporting statements, sub-groups and sub-argument groups. Computed bottom-up by a post-order walk
    /// with an explicit stack so that arbitrarily deep nesting cannot overflow the call stack.
    /// </summary>
    private static (Dictionary<Guid, bool> Stands, Dictionary<Guid, bool> Holds) ComputeSupport(
        Statement thesis,
        ILookup<Guid, Statement> children,
        IReadOnlyDictionary<Guid, bool> refuted)
    {
        var stands = new Dictionary<Guid, bool>();
        var holds = new Dictionary<Guid, bool>();
        var visiting = new HashSet<Guid>();
        var done = new HashSet<Guid>();
        var stack = new Stack<(Statement Node, bool ChildrenDone)>();

        foreach (var group in children[thesis.Id].Where(child => child.Kind == StatementKind.SupportGroup))
        {
            stack.Push((group, false));
        }

        while (stack.Count > 0)
        {
            var (node, childrenDone) = stack.Pop();
            if (done.Contains(node.Id))
            {
                continue;
            }

            var supportChildren = SupportChildren(node, children);
            if (!childrenDone)
            {
                if (!visiting.Add(node.Id))
                {
                    continue; // Defensive: a malformed parent cycle; the first visit finishes it.
                }

                stack.Push((node, true));
                foreach (var child in supportChildren.Where(child => !done.Contains(child.Id)))
                {
                    stack.Push((child, false));
                }

                continue;
            }

            done.Add(node.Id);
            if (node.Kind == StatementKind.SupportingStatement)
            {
                // Empty sub-argument groups have no holds entry and are ignored.
                stands[node.Id] = !HasUnrefutedCounter(node.Id, children, refuted)
                    && supportChildren.All(group => !holds.TryGetValue(group.Id, out var groupHolds) || groupHolds);
                continue;
            }

            var memberOutcomes = new List<bool>();
            foreach (var child in supportChildren)
            {
                var outcomes = child.Kind == StatementKind.SupportingStatement ? stands : holds;
                if (outcomes.TryGetValue(child.Id, out var outcome))
                {
                    memberOutcomes.Add(outcome); // A sub-group without an entry is empty and so not a member.
                }
            }

            if (memberOutcomes.Count == 0)
            {
                continue; // Empty group: ignored, no holds entry.
            }

            holds[node.Id] = node.GroupType == SupportGroupType.AnySufficient
                ? memberOutcomes.Any(outcome => outcome)
                : memberOutcomes.All(outcome => outcome);
        }

        return (stands, holds);
    }

    /// <summary>
    /// The children that take part in support: a group's supporting statements and sub-groups, or a supporting
    /// statement's sub-argument groups. Counter arguments are handled through refutation; illustrations never count.
    /// </summary>
    private static List<Statement> SupportChildren(Statement node, ILookup<Guid, Statement> children) =>
        node.Kind == StatementKind.SupportGroup
            ? children[node.Id].Where(child => child.Kind is StatementKind.SupportingStatement or StatementKind.SupportGroup).ToList()
            : children[node.Id].Where(child => child.Kind == StatementKind.SupportGroup).ToList();

    /// <summary>
    /// Refutation for every counter argument and rebuttal, computed bottom-up with an explicit stack so that
    /// arbitrarily deep rebuttal chains cannot overflow the call stack.
    /// </summary>
    private static Dictionary<Guid, bool> ComputeRefutation(IReadOnlyCollection<Statement> statements, ILookup<Guid, Statement> children)
    {
        var refuted = new Dictionary<Guid, bool>();
        var visiting = new HashSet<Guid>();
        var stack = new Stack<(Guid Id, bool ChildrenDone)>();

        foreach (var root in statements.Where(IsContestable))
        {
            stack.Push((root.Id, false));
            while (stack.Count > 0)
            {
                var (id, childrenDone) = stack.Pop();
                if (refuted.ContainsKey(id))
                {
                    continue;
                }

                var rebuttals = children[id].Where(child => child.Kind == StatementKind.Rebuttal);
                if (!childrenDone)
                {
                    if (!visiting.Add(id))
                    {
                        continue; // Defensive: a malformed cycle; the first visit finishes it.
                    }

                    stack.Push((id, true));
                    foreach (var rebuttal in rebuttals.Where(rebuttal => !refuted.ContainsKey(rebuttal.Id)))
                    {
                        stack.Push((rebuttal.Id, false));
                    }

                    continue;
                }

                refuted[id] = rebuttals.Any(rebuttal => refuted.TryGetValue(rebuttal.Id, out var isRefuted) && !isRefuted);
            }
        }

        return refuted;
    }

    private static bool IsContestable(Statement statement) =>
        statement.Kind is StatementKind.CounterArgument or StatementKind.Rebuttal;
}
