namespace TheDiscussion.Core.Discussions;

/// <summary>How the supporting statements in a group relate to each other.</summary>
public enum SupportGroupType
{
    /// <summary>The group holds only when every supporting statement in it stands.</summary>
    AllRequired,

    /// <summary>The group holds when at least one supporting statement in it stands.</summary>
    AnySufficient,
}
