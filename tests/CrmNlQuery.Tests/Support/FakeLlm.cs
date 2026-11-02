using CrmNlQuery.Application.Ask.Abstractions;

namespace CrmNlQuery.Tests.Support;

/// <summary>
/// Fake model client returning canned replies for tests.
/// </summary>
public sealed class FakeLlm : ILlmClient
{
    private readonly Queue<string> _replies;
    public List<(string System, string User)> Calls { get; } = new List<(string System, string User)>();

    public FakeLlm(params string[] replies)
    {
        _replies = new Queue<string>(replies);
    }

    /// <summary>
    /// Returns a canned JSON reply for the test.
    /// </summary>
    public Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
    {
        Calls.Add((system, user));
        return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : "");
    }
}
