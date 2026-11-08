using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PodcastGenerator.Application.Common;
using PodcastGenerator.Application.Preferences;
using PodcastGenerator.Infrastructure.Identity;
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
        return services;
    }
}
