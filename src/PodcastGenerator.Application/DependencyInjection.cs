using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PodcastGenerator.Application.Audio;
using PodcastGenerator.Application.Auth;
using PodcastGenerator.Application.Episodes;
using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Preferences;
using PodcastGenerator.Application.Tts;

namespace PodcastGenerator.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EpisodeOptions>(configuration.GetSection(EpisodeOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPreferencesService, PreferencesService>();
        services.AddScoped<IEpisodeService, EpisodeService>();
        services.AddScoped<IEpisodeGenerator, EpisodeGenerator>();
        services.Configure<TtsOptions>(configuration.GetSection(TtsOptions.SectionName));
        services.AddScoped<TtsBudget>();
        services.AddScoped<AudioGenerator>();
        return services;
    }
}
