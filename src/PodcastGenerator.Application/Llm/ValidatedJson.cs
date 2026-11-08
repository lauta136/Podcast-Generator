namespace PodcastGenerator.Application.Llm;

/// <summary>LLM JSON with one repair attempt: invalid JSON or failed validation gets one retry, then the step fails.</summary>
public static class ValidatedJson
{
    public static async Task<T> GenerateAsync<T>(
        ILlmProvider llm,
        LlmRequest request,
        Func<T, IReadOnlyList<string>> validate,
        CancellationToken ct)
    {
        string problem;
        string previous;
        try
        {
            var first = await llm.GenerateJsonAsync<T>(request, ct);
            var errors = validate(first);
            if (errors.Count == 0)
            {
                return first;
            }

            problem = string.Join("; ", errors);
            previous = "(valid JSON, but it broke the rules)";
        }
        catch (LlmInvalidResponseException e)
        {
            problem = e.Message;
            previous = e.RawContent;
        }

        var repair = request with
        {
            Purpose = request.Purpose + "-repair",
            Temperature = 0.2,
            UserPrompt = $"""
                {request.UserPrompt}

                Your previous reply was rejected: {problem}
                Previous reply:
                {Truncate(previous, 4000)}

                Reply again with only the corrected JSON object.
                """,
        };

        var second = await llm.GenerateJsonAsync<T>(repair, ct);
        var secondErrors = validate(second);
        if (secondErrors.Count > 0)
        {
            throw new LlmInvalidResponseException(
                $"Invalid {request.Purpose} response after repair: {string.Join("; ", secondErrors)}", "");
        }

        return second;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
