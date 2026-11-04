namespace TheDiscussion.Architecture.Tests;

internal static class RepositoryRoot
{
    private static readonly Lazy<string> Root = new(Find);

    public static string Path => Root.Value;

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "TheDiscussion.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find TheDiscussion.sln above " + AppContext.BaseDirectory);
    }
}
