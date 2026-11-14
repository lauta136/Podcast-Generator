using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Auth;
using PodcastGenerator.Application.Episodes;
using PodcastGenerator.Application.Llm;
using PodcastGenerator.Application.Preferences;
using PodcastGenerator.Application.Tts;
using PodcastGenerator.Infrastructure.Identity;
using PodcastGenerator.Infrastructure.Jobs;
using PodcastGenerator.Infrastructure.Llm;
using PodcastGenerator.Infrastructure.Persistence;
using PodcastGenerator.Infrastructure.Persistence.Repositories;
using PodcastGenerator.Infrastructure.Storage;
using PodcastGenerator.Infrastructure.Tts;

namespace PodcastGenerator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
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

        services.AddScoped<ITtsUsageRepository, TtsUsageRepository>();
        services.Configure<GoogleTtsOptions>(configuration.GetSection(GoogleTtsOptions.SectionName));
        services.AddHttpClient<ITtsProvider, GoogleCloudTtsProvider>((sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<GoogleTtsOptions>>().Value;
            http.BaseAddress = new Uri(o.BaseUrl.EndsWith('/') ? o.BaseUrl : o.BaseUrl + "/");
            http.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IAudioStorage, LocalFileAudioStorage>();

        services.Configure<WorkerOptions>(configuration.GetSection(WorkerOptions.SectionName));
        services.AddScoped<JobLeases>();
        services.AddHostedService<GenerationWorker>();
        return services;
    }
}
