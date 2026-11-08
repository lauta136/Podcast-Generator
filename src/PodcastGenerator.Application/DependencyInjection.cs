using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PodcastGenerator.Application.Preferences;

namespace PodcastGenerator.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EpisodeOptions>(configuration.GetSection(EpisodeOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IPreferencesService, PreferencesService>();
        return services;
    }
}
