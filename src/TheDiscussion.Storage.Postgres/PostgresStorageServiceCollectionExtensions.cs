using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheDiscussion.Core.Storage;

namespace TheDiscussion.Storage.Postgres;

public static class PostgresStorageServiceCollectionExtensions
{
    /// <summary>Registers <see cref="PostgresDiscussionStore"/> using the given Npgsql connection string.</summary>
    public static IServiceCollection AddPostgresDiscussionStore(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextFactory<DiscussionDbContext>(options => PostgresOptions.Configure(options, connectionString));
        services.AddSingleton<IDiscussionStore, PostgresDiscussionStore>();
        return services;
    }

    /// <summary>Applies any pending EF Core migrations to the configured database.</summary>
    public static async Task MigratePostgresDiscussionStoreAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<DiscussionDbContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }
}

/// <summary>Shared Npgsql configuration for runtime, tests and design-time tooling.</summary>
public static class PostgresOptions
{
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(DiscussionDbContext).Assembly.GetName().Name));
}
