using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Common;
using PodcastGenerator.Application.Episodes;
using PodcastGenerator.Application.Llm;
using PodcastGenerator.Application.Preferences;
using PodcastGenerator.Infrastructure.Identity;
using PodcastGenerator.Infrastructure.Jobs;
using PodcastGenerator.Infrastructure.Llm;
using PodcastGenerator.Infrastructure.Persistence;
using PodcastGenerator.Infrastructure.Persistence.Repositories;

namespace PodcastGenerator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ICurrentUser, LocalCurrentUser>();
        services.AddScoped<IPreferencesRepository, PreferencesRepository>();
        services.AddScoped<IEpisodeRepository, EpisodeRepository>();

        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.AddSingleton(sp => new RequestRateLimiter(
            sp.GetRequiredService<IOptions<LlmOptions>>().Value.RequestsPerMinute,
            sp.GetRequiredService<TimeProvider>()));
        services.AddHttpClient<ILlmProvider, OpenAiCompatibleLlmProvider>((sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            http.BaseAddress = new Uri(o.BaseUrl.EndsWith('/') ? o.BaseUrl : o.BaseUrl + "/");
            http.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.Configure<WorkerOptions>(configuration.GetSection(WorkerOptions.SectionName));
        services.AddScoped<JobLeases>();
        services.AddHostedService<GenerationWorker>();
        return services;
    }
}
