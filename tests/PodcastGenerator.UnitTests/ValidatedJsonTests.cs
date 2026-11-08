using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Llm;

namespace PodcastGenerator.UnitTests;

public class ValidatedJsonTests
{
    private static readonly LlmRequest Request = new("outline", "system", "user");

    [Fact]
    public async Task Valid_reply_is_returned_without_repair()
    {
        var llm = new ScriptedLlm(Good());

        var result = await ValidatedJson.GenerateAsync<OutlineResponse>(llm, Request, r => r.Validate(1), CancellationToken.None);

        Assert.Equal("Titel", result.Title);
        Assert.Single(llm.Requests);
    }

    [Fact]
    public async Task Invalid_json_gets_one_repair_with_the_error()
    {
        var llm = new ScriptedLlm(new LlmInvalidResponseException("not JSON", "{oops"), Good());

        var result = await ValidatedJson.GenerateAsync<OutlineResponse>(llm, Request, r => r.Validate(1), CancellationToken.None);

        Assert.Equal("Titel", result.Title);
        Assert.Equal(2, llm.Requests.Count);
        Assert.Equal("outline-repair", llm.Requests[1].Purpose);
        Assert.Contains("{oops", llm.Requests[1].UserPrompt);
    }

    [Fact]
    public async Task Rule_violation_gets_one_repair_then_fails()
    {
        var wrongCount = new OutlineResponse { Title = "T", Sections = [] };
        var llm = new ScriptedLlm(wrongCount, wrongCount);

        var ex = await Assert.ThrowsAsync<LlmInvalidResponseException>(() =>
            ValidatedJson.GenerateAsync<OutlineResponse>(llm, Request, r => r.Validate(1), CancellationToken.None));

        Assert.Contains("exactly 1", ex.Message);
        Assert.Equal(2, llm.Requests.Count);
    }

    private static OutlineResponse Good() => new()
    {
        Title = "Titel",
        Sections = [new OutlineSection { Title = "Teil", Summary = "Summary" }],
    };

    private sealed class ScriptedLlm(params object[] replies) : ILlmProvider
    {
        private readonly Queue<object> _replies = new(replies);

        public List<LlmRequest> Requests { get; } = [];

        public Task<T> GenerateJsonAsync<T>(LlmRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var reply = _replies.Dequeue();
            return reply is Exception e ? Task.FromException<T>(e) : Task.FromResult((T)reply);
        }

        public Task<string> GenerateTextAsync(LlmRequest request, CancellationToken ct) => throw new NotSupportedException();
    }
}
