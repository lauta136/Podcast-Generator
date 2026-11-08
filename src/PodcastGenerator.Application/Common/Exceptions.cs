namespace PodcastGenerator.Application.Common;

/// <summary>Input is well-formed but breaks a business rule. Mapped to 400.</summary>
public class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public RequestValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }
}

/// <summary>Mapped to 404.</summary>
public class NotFoundException(string message) : Exception(message);
