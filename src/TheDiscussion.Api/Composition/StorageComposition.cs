using TheDiscussion.Storage.InMemory;
using TheDiscussion.Storage.Postgres;

namespace TheDiscussion.Api.Composition;

/// <summary>
/// The API's composition root for storage: the only place in the application tier that knows which
/// storage providers exist. Everything else depends on <c>TheDiscussion.Core.Storage.IDiscussionStore</c>.
/// The provider is picked from configuration:
/// <list type="bullet">
/// <item><c>Storage:Provider</c> = <c>InMemory</c> (default) or <c>Postgres</c>.</item>
/// <item><c>ConnectionStrings:TheDiscussion</c> = Npgsql connection string (Postgres only).</item>
/// <item><c>Storage:Postgres:MigrateOnStartup</c> = apply EF Core migrations at startup (default <c>true</c>).</item>
/// </list>
/// </summary>
public static class StorageComposition
{
    public const string ProviderKey = "Storage:Provider";
    public const string ConnectionStringName = "TheDiscussion";
    public const string MigrateOnStartupKey = "Storage:Postgres:MigrateOnStartup";

    public static IServiceCollection AddDiscussionStorage(this IServiceCollection services, IConfiguration configuration)
    {
        switch (ProviderOf(configuration))
        {
            case StorageProvider.InMemory:
                services.AddInMemoryDiscussionStore();
                break;
            case StorageProvider.Postgres:
                var connectionString = configuration.GetConnectionString(ConnectionStringName);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException(
                        $"{ProviderKey} is 'Postgres' but no connection string is configured. " +
                        $"Set ConnectionStrings:{ConnectionStringName} (environment variable ConnectionStrings__{ConnectionStringName}).");
                }

                services.AddPostgresDiscussionStore(connectionString);
                break;
        }

        return services;
    }

    /// <summary>Prepares the configured storage before the API starts serving requests.</summary>
    public static async Task InitializeDiscussionStorageAsync(this WebApplication app)
    {
        if (ProviderOf(app.Configuration) == StorageProvider.Postgres
            && app.Configuration.GetValue(MigrateOnStartupKey, defaultValue: true))
        {
            await app.Services.MigratePostgresDiscussionStoreAsync(app.Lifetime.ApplicationStopping);
        }
    }

    private static StorageProvider ProviderOf(IConfiguration configuration)
    {
        var configured = configuration[ProviderKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return StorageProvider.InMemory;
        }

        return Enum.TryParse<StorageProvider>(configured, ignoreCase: true, out var provider) && Enum.IsDefined(provider)
            ? provider
            : throw new InvalidOperationException(
                $"Unknown {ProviderKey} '{configured}'. Use one of: {string.Join(", ", Enum.GetNames<StorageProvider>())}.");
    }

    private enum StorageProvider
    {
        InMemory,
        Postgres,
    }
}
