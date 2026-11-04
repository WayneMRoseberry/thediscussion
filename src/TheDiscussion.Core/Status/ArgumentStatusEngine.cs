using TheDiscussion.Core.Discussions;

namespace TheDiscussion.Core.Status;

/// <summary>Status of a discussion's thesis.</summary>
public enum ThesisStatus
{
    /// <summary>The thesis has no supporting statements.</summary>
    Unsupported,

    /// <summary>The thesis is supported and no counter argument exists anywhere in the tree.</summary>
    Open,

    /// <summary>An unrefuted counter argument targets the thesis, or a non-empty support group does not hold.</summary>
    Countered,

    /// <summary>The thesis is supported, has been countered somewhere, and every group still holds.</summary>
    Tested,
}

/// <summary>Computed status of every node that has one. Never stored; recomputed on every read.</summary>
/// <param name="Thesis">Status of the thesis.</param>
/// <param name="Stands">Per supporting statement: <c>true</c> when no unrefuted counter argument targets it.</param>
/// <param name="Refuted">Per counter argument and rebuttal: <c>true</c> when an unrefuted rebuttal targets it.</param>
/// <param name="Holds">Per non-empty support group: whether it holds. Empty groups have no entry (they are ignored).</param>
public sealed record ArgumentStatus(
    ThesisStatus Thesis,
    IReadOnlyDictionary<Guid, bool> Stands,
    IReadOnlyDictionary<Guid, bool> Refuted,
    IReadOnlyDictionary<Guid, bool> Holds);

/// <summary>
/// Pure function computing argument status from a discussion's statements.
/// <list type="bullet">
/// <item>A counter argument or rebuttal is refuted when at least one rebuttal targeting it is not itself refuted (recursively, any depth).</item>
/// <item>A supporting statement stands when no unrefuted counter argument targets it.</item>
/// <item>An all-required group holds when all its supporting statements stand; an any-sufficient group when at least one does. Empty groups are ignored.</item>
/// <item>Illustrations never affect status.</item>
/// <item>Thesis: Unsupported without supporting statements; otherwise Countered when an unrefuted counter argument targets
/// the thesis or a non-empty group does not hold; otherwise Tested when any counter argument exists; otherwise Open.</item>
/// </list>
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

        var stands = new Dictionary<Guid, bool>();
        var holds = new Dictionary<Guid, bool>();
        var supportCount = 0;

        foreach (var group in children[thesis.Id].Where(child => child.Kind == StatementKind.SupportGroup))
        {
            var supports = children[group.Id].Where(child => child.Kind == StatementKind.SupportingStatement).ToList();
            foreach (var support in supports)
            {
                stands[support.Id] = !HasUnrefutedCounter(support.Id, children, refuted);
            }

            supportCount += supports.Count;
            if (supports.Count == 0)
            {
                continue; // Empty groups are ignored.
            }

            holds[group.Id] = group.GroupType == SupportGroupType.AnySufficient
                ? supports.Any(support => stands[support.Id])
                : supports.All(support => stands[support.Id]);
        }

        ThesisStatus status;
        if (supportCount == 0)
        {
            status = ThesisStatus.Unsupported;
        }
        else if (HasUnrefutedCounter(thesis.Id, children, refuted) || holds.Values.Any(groupHolds => !groupHolds))
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
