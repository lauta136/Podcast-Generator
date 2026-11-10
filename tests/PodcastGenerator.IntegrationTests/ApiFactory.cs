using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PodcastGenerator.Application.Llm;
using PodcastGenerator.Application.Tts;
using PodcastGenerator.Infrastructure.Persistence;

namespace PodcastGenerator.IntegrationTests;

/// <summary>
/// Runs the real API and worker against a throwaway PostgreSQL database (one per test class),
/// created on the server from ConnectionStrings__Default or the local docker-compose DB.
/// The LLM is always a fake: tests never call real providers.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
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

    public FakeTtsProvider Tts { get; } = new();

    /// <summary>Per-factory audio folder, deleted afterwards. Generated audio never lands in the repo.</summary>
    public string AudioRoot { get; } = Path.Combine(Path.GetTempPath(), $"podcast-audio-{Guid.NewGuid():N}");

    protected virtual IEnumerable<(string Key, string Value)> ExtraSettings => [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Worker:PollIntervalMs", "50");
        builder.UseSetting("Storage:LocalPath", AudioRoot);
        builder.UseSetting("Tts:ChunkRetryDelayMs", "10");
        foreach (var (key, value) in ExtraSettings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILlmProvider>();
            services.AddSingleton<ILlmProvider>(Llm);
            services.RemoveAll<ITtsProvider>();
            services.AddSingleton<ITtsProvider>(Tts);
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
        if (Directory.Exists(AudioRoot))
        {
            Directory.Delete(AudioRoot, recursive: true);
        }
    }
}

public sealed class NoProofreadApiFactory : ApiFactory
{
    protected override IEnumerable<(string Key, string Value)> ExtraSettings => [("Episodes:Proofread", "false")];
}

/// <summary>Tiny budget, and an estimate low enough that creation is allowed: synthesis must stop at the budget.</summary>
public sealed class TinyBudgetApiFactory : ApiFactory
{
    public const long Budget = 3000;

    protected override IEnumerable<(string Key, string Value)> ExtraSettings =>
    [
        ("Tts:Budgets:WaveNet:MonthlyChars", Budget.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ("Tts:CharsPerWord", "0.1"),
    ];
}

/// <summary>Real per-word estimate with the tiny budget: creating a 10-minute episode must be refused.</summary>
public sealed class NoBudgetApiFactory : ApiFactory
{
    protected override IEnumerable<(string Key, string Value)> ExtraSettings => [("Tts:Budgets:WaveNet:MonthlyChars", "3000")];
}
