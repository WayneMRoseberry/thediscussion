using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TheDiscussion.Storage.Postgres;

/// <summary>
/// Used only by the <c>dotnet ef</c> tool. Adding a migration does not connect to a database;
/// commands that do connect (for example <c>dotnet ef database update</c>) use THEDISCUSSION_DESIGN_POSTGRES.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DiscussionDbContext>
{
    public DiscussionDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("THEDISCUSSION_DESIGN_POSTGRES")
            ?? "Host=localhost;Database=thediscussion;Username=thediscussion";
        var options = new DbContextOptionsBuilder<DiscussionDbContext>();
        PostgresOptions.Configure(options, connectionString);
        return new DiscussionDbContext(options.Options);
    }
}
