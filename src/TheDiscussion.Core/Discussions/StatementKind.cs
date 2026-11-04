namespace TheDiscussion.Core.Discussions;

/// <summary>The kind of a statement in a structured discussion.</summary>
public enum StatementKind
{
    /// <summary>The root point a discussion asserts. Exactly one per discussion.</summary>
    Thesis,

    /// <summary>Groups supporting statements under the thesis; see <see cref="SupportGroupType"/>.</summary>
    SupportGroup,

    /// <summary>An argumentative statement inside a support group.</summary>
    SupportingStatement,

    /// <summary>Illustrates (does not argue for) the thesis or a supporting statement. Never affects status.</summary>
    Illustration,

    /// <summary>Argues against the thesis or a supporting statement.</summary>
    CounterArgument,

    /// <summary>Argues against a counter argument or another rebuttal.</summary>
    Rebuttal,
}
