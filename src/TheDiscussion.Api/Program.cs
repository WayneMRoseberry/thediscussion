using TheDiscussion.Api;
using TheDiscussion.Api.Composition;

var builder = WebApplication.CreateBuilder(args);

builder.AddDiscussionApi();
builder.Services.AddDiscussionStorage(builder.Configuration);

var app = builder.Build();

app.UseDiscussionApi();
await app.InitializeDiscussionStorageAsync();

await app.RunAsync();

/// <summary>Entry point. Declared partial so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
