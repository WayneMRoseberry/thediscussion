using TheDiscussion.Core.Storage;
using TheDiscussion.Storage.InMemory;

namespace TheDiscussion.Storage.Tests;

/// <summary>Runs the shared storage contract against the in-memory provider (always runs).</summary>
public sealed class InMemoryDiscussionStoreContractTests : DiscussionStoreContract
{
    private readonly InMemoryDiscussionStore store = new();

    protected override IDiscussionStore CreateStore() => store;
}
