using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PodcastGenerator.Api.Auth;
using PodcastGenerator.Api.Middleware;
using PodcastGenerator.Application;
using PodcastGenerator.Infrastructure;
using PodcastGenerator.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddHealthChecks();

builder.AddAppAuth();
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Dev and tests only: production applies migrations as a deploy step.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.WarnIfTemporarySigningKey();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

await app.RunAsync();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
