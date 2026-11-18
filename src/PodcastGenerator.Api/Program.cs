using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
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

// Behind Caddy (see deploy/), every request arrives from the proxy over plain HTTP. These headers carry the
// real client address (for the sign-in rate limit) and the original https (for Secure cookies). Only enable
// this when the API port is reachable by the proxy alone, or anyone could claim any address.
var behindProxy = builder.Configuration.GetValue<bool>("Proxy:TrustForwardedHeaders");
if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1; // the address Caddy saw; anything a client added before it is ignored
        o.KnownIPNetworks.Clear(); // the proxy's address on the Docker network isn't fixed
        o.KnownProxies.Clear();
    });
}

builder.AddAppAuth();
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// "--migrate-only" is the deploy step (deploy/docker-compose.yml runs it before the API starts): it brings the
// database schema up to date and exits. MigrateOnStartup is for dev and tests only.
var migrateOnly = args.Contains("--migrate-only");
if (migrateOnly || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    if (migrateOnly)
    {
        return;
    }
}

app.WarnIfTemporarySigningKey();

if (behindProxy)
{
    app.UseForwardedHeaders(); // first, so everything after sees the client's address and scheme
}

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
