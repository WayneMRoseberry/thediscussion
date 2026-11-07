using TheDiscussion.Core.Discussions;

namespace TheDiscussion.Core.Tests;

/// <summary>Builds statement lists for status tests without any storage.</summary>
internal sealed class TreeBuilder
{
    private readonly Guid discussionId = Guid.NewGuid();
    private readonly List<Statement> statements = [];

    public TreeBuilder()
    {
        Thesis = Add(StatementKind.Thesis, null, "Thesis");
    }

    public Statement Thesis { get; }

    public IReadOnlyList<Statement> Statements => statements;

    public Statement AllRequired() => AddGroup(SupportGroupType.AllRequired);

    public Statement AnySufficient() => AddGroup(SupportGroupType.AnySufficient);

    /// <summary>Adds an all-required group under <paramref name="parent"/>: a group (sub-group) or a supporting statement (sub-argument).</summary>
    public Statement AllRequired(Statement parent) => AddGroup(SupportGroupType.AllRequired, parent);

    /// <summary>Adds an any-sufficient group under <paramref name="parent"/>: a group (sub-group) or a supporting statement (sub-argument).</summary>
    public Statement AnySufficient(Statement parent) => AddGroup(SupportGroupType.AnySufficient, parent);

    /// <summary>Adds a group of <paramref name="type"/> under <paramref name="parent"/>, or under the thesis when it is <c>null</c>.</summary>
    public Statement Group(SupportGroupType type, Statement? parent = null) => AddGroup(type, parent);

    public Statement Support(Statement group, string text = "Support") => Add(StatementKind.SupportingStatement, group, text);

    public Statement Illustration(Statement target, string text = "Illustration") => Add(StatementKind.Illustration, target, text);

    public Statement Counter(Statement target, string text = "Counter") => Add(StatementKind.CounterArgument, target, text);

    public Statement Rebuttal(Statement target, string text = "Rebuttal") => Add(StatementKind.Rebuttal, target, text);

    private Statement AddGroup(SupportGroupType type, Statement? parent = null)
    {
        var group = new Statement(Guid.NewGuid(), discussionId, StatementKind.SupportGroup, (parent ?? Thesis).Id, null, type, DateTimeOffset.UnixEpoch);
        statements.Add(group);
        return group;
    }

    private Statement Add(StatementKind kind, Statement? parent, string text)
    {
        var statement = new Statement(Guid.NewGuid(), discussionId, kind, parent?.Id, text, null, DateTimeOffset.UnixEpoch);
        statements.Add(statement);
        return statement;
    }
}
