using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PodcastGenerator.Application.Common;
using PodcastGenerator.Application.Tts;

namespace PodcastGenerator.Api.Middleware;

/// <summary>Maps application exceptions to ProblemDetails. Anything else falls through to a generic 500.</summary>
public class AppExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            RequestValidationException e => new ValidationProblemDetails(e.Errors.ToDictionary(kv => kv.Key, kv => kv.Value))
            {
                Status = StatusCodes.Status400BadRequest,
            },
            InvalidCredentialsException e => new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Sign-in failed",
                Detail = e.Message,
            },
            NotFoundException e => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not found",
                Detail = e.Message,
            },
            TtsException e => new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Text-to-Speech error",
                Detail = e.Message,
            },
            ConflictException e => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = e.Message,
            },
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
