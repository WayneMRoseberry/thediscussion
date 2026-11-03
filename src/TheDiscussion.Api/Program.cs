using TheDiscussion.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddDiscussionApi();

var app = builder.Build();

app.UseDiscussionApi();

app.Run();

/// <summary>Entry point. Declared partial so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
