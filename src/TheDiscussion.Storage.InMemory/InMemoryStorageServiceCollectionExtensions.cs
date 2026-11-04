using Microsoft.Extensions.DependencyInjection;
using TheDiscussion.Core.Storage;

namespace TheDiscussion.Storage.InMemory;

public static class InMemoryStorageServiceCollectionExtensions
{
    /// <summary>Registers a single process-wide <see cref="InMemoryDiscussionStore"/>.</summary>
    public static IServiceCollection AddInMemoryDiscussionStore(this IServiceCollection services)
    {
        services.AddSingleton<IDiscussionStore, InMemoryDiscussionStore>();
        return services;
    }
}
