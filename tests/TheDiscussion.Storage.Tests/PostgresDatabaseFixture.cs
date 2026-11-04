using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Xunit.Abstractions;
using Xunit.Sdk;
using TheDiscussion.Storage.Postgres;

namespace TheDiscussion.Storage.Tests;

/// <summary>
/// Creates a throwaway PostgreSQL database for one test class, applies the EF Core migrations to it,
/// and drops it afterwards.
/// <list type="bullet">
/// <item><c>THEDISCUSSION_TEST_POSTGRES</c>: connection string to a server where the user may create databases.
/// When it is not set, PostgreSQL tests are skipped with <see cref="SkipReason"/>.</item>
/// <item><c>THEDISCUSSION_REQUIRE_POSTGRES_TESTS=true</c> (set in CI): a missing connection string fails instead of skipping.</item>
/// </list>
/// </summary>
public sealed class PostgresDatabaseFixture(IMessageSink diagnostics) : IAsyncLifetime
{
    public const string ConnectionStringVariable = "THEDISCUSSION_TEST_POSTGRES";
    public const string RequireVariable = "THEDISCUSSION_REQUIRE_POSTGRES_TESTS";

    private string? serverConnectionString;
    private string? databaseName;

    /// <summary>Why PostgreSQL tests are skipped, or <c>null</c> when a database is available.</summary>
    public string? SkipReason { get; private set; }

    public IDbContextFactory<DiscussionDbContext> ContextFactory =>
        contextFactory ?? throw new InvalidOperationException("The PostgreSQL test database is not available.");

    private PooledDbContextFactory<DiscussionDbContext>? contextFactory;

    public async Task InitializeAsync()
    {
        serverConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(serverConnectionString))
        {
            if (string.Equals(Environment.GetEnvironmentVariable(RequireVariable), "true", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{RequireVariable} is true but {ConnectionStringVariable} is not set: PostgreSQL contract tests cannot run.");
            }

            SkipReason =
                $"PostgreSQL contract tests skipped: set {ConnectionStringVariable} to an Npgsql connection string " +
                "(for example Host=localhost;Username=postgres;Password=postgres) to run them.";
            diagnostics.OnMessage(new DiagnosticMessage(SkipReason));
            return;
        }

        databaseName = $"thediscussion_test_{Guid.NewGuid():N}";
        await ExecuteOnServerAsync($"CREATE DATABASE \"{databaseName}\"");

        var testConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = databaseName }.ConnectionString;
        var options = new DbContextOptionsBuilder<DiscussionDbContext>();
        PostgresOptions.Configure(options, testConnectionString);
        contextFactory = new PooledDbContextFactory<DiscussionDbContext>(options.Options);

        // The schema comes from the real migrations, exactly as the API applies them.
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (databaseName is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(serverConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
