using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PodcastGenerator.Application.Llm;
using PodcastGenerator.Infrastructure.Persistence;

namespace PodcastGenerator.IntegrationTests;

/// <summary>
/// Runs the real API and worker against a throwaway PostgreSQL database (one per test class),
/// created on the server from ConnectionStrings__Default or the local docker-compose DB.
/// The LLM is always a fake: tests never call real providers.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string LocalServer = "Host=localhost;Port=5433;Username=podcast;Password=podcast";

    private readonly string _connectionString;

    public ApiFactory()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? LocalServer;
        _connectionString = new NpgsqlConnectionStringBuilder(server)
        {
            Database = $"podcast_test_{Guid.NewGuid():N}",
        }.ConnectionString;
    }

    public FakeLlmProvider Llm { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Worker:PollIntervalMs", "50");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILlmProvider>();
            services.AddSingleton<ILlmProvider>(Llm);
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }

        await DisposeAsync();
    }
}
