using CrmNlQuery.Application.Observability.Calls;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Observability.Calls;

/// <summary>
/// Tests the LLM call log that records the prompt size, real tokens and duration of each model call in memory for eval.
/// </summary>
public class LlmCallLogTests
{
    private sealed class Inner : ILlmClient
    {
        public Func<LlmResult>? Reply { get; set; }
        public Exception? Throw { get; set; }
        /// <summary>
        /// Fake inner model client returning JSON output.
        /// </summary>
        public Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
            => throw new InvalidOperationException("The recorder must call CompleteAsync.");
        /// <summary>
        /// Fake inner model client returning plain text.
        /// </summary>
        public Task<LlmResult> CompleteAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
        {
            if (Throw is not null) throw Throw;
            return Task.FromResult(Reply!());
        }
    }

    /// <summary>
    /// Verifies that a call is recorded with purpose, sizes and tokens and the text is unchanged.
    /// </summary>
    [Fact]
    public async Task ACall_IsRecorded_WithPurpose_Sizes_Tokens_AndTheTextIsUnchanged()
    {
        var log = new LlmCallLog();
        var inner = new Inner { Reply = () => new LlmResult("{\"ok\":true}", new LlmUsage(120, 34)) };
        var client = new RecordingLlmClient(inner, log, "plan");

        var text = await client.CompleteJsonAsync(new string('s', 1000), new string('u', 2000), new string('x', 500));

        Assert.Equal("{\"ok\":true}", text);
        var sample = Assert.Single(log.Since(0));
        Assert.Equal("plan", sample.Purpose);
        Assert.Equal(3500, sample.PromptChars);
        Assert.Equal(1000, sample.EstTokens);
        Assert.Equal(120, sample.InputTokens);
        Assert.Equal(34, sample.OutputTokens);
        Assert.True(sample.Milliseconds >= 0);
        Assert.Null(sample.Overflow);
    }

    /// <summary>
    /// Verifies that the texts are kept only when capture is on.
    /// </summary>
    [Fact]
    public async Task TheTexts_AreKeptOnlyWhenCaptureIsOn()
    {
        var inner = new Inner { Reply = () => new LlmResult("{}", null) };

        var off = new LlmCallLog();
        await new RecordingLlmClient(inner, off, "plan").CompleteJsonAsync("SYSTEM", "USER", "{}");
        Assert.Null(off.Since(0)[0].System);
        Assert.Null(off.Since(0)[0].User);

        var on = new LlmCallLog { CaptureText = true };
        await new RecordingLlmClient(inner, on, "plan").CompleteJsonAsync("SYSTEM", "USER", "{}");
        Assert.Equal("SYSTEM", on.Since(0)[0].System);
        Assert.Equal("USER", on.Since(0)[0].User);
        Assert.Equal("{}", on.Since(0)[0].Reply);
        Assert.Null(off.Since(0)[0].Reply);
    }

    /// <summary>
    /// Verifies that an overflow is recorded with its cause and still thrown.
    /// </summary>
    [Fact]
    public async Task AnOverflow_IsRecordedWithItsCause_AndStillThrown()
    {
        var log = new LlmCallLog();
        var inner = new Inner { Throw = new PromptTooLongException(PromptTooLongException.GenerationCause, 400, 595, 1000) };
        var client = new RecordingLlmClient(inner, log, "analyze");

        await Assert.ThrowsAsync<PromptTooLongException>(() => client.CompleteJsonAsync("s", "u", "{}"));

        var sample = Assert.Single(log.Since(0));
        Assert.Equal(PromptTooLongException.GenerationCause, sample.Overflow);
        Assert.Equal(400, sample.InputTokens);
        Assert.Equal(595, sample.OutputTokens);
    }

    /// <summary>
    /// Verifies that since returns only the calls after the mark.
    /// </summary>
    [Fact]
    public async Task Since_ReturnsOnlyTheCallsAfterTheMark()
    {
        var log = new LlmCallLog();
        var inner = new Inner { Reply = () => new LlmResult("{}", null) };
        var client = new RecordingLlmClient(inner, log, "plan");
        await client.CompleteJsonAsync("a", "b", "{}");
        var mark = log.Mark();

        await client.CompleteJsonAsync("c", "d", "{}");
        await client.CompleteJsonAsync("e", "f", "{}");

        Assert.Equal(2, log.Since(mark).Count);
        Assert.Equal(3, log.Since(0).Count);
        Assert.Null(log.Since(mark)[0].InputTokens);
    }

    /// <summary>
    /// Verifies that the summary groups by purpose and lists the cases that would not fit.
    /// </summary>
    [Fact]
    public void TheSummary_GroupsByPurpose_AndListsTheCasesThatWouldNotFit()
    {
        var samples = new List<LlmCallSample>
        {
            new LlmCallSample("plan", 7000, 2000, 1800, 100, 4000, null),
            new LlmCallSample("plan", 70000, 20000, 18000, 100, 9000, null),
            new LlmCallSample("analyze", 3500, 1000, 900, 50, 1000, null),
        };

        var rows = LlmCallSummary.ByPurpose(samples);

        var plan = rows.Single(r => r.Purpose == "plan");
        Assert.Equal(2, plan.Calls);
        Assert.Equal(11000, plan.AvgEstTokens, 1);
        Assert.Equal(20000, plan.MaxEstTokens);
        Assert.Equal(6500, plan.AvgMilliseconds, 1);
        Assert.True(LlmCallSummary.WouldNotFit(samples, 16384));
        Assert.False(LlmCallSummary.WouldNotFit(samples.Take(1).ToList(), 16384));
        Assert.False(LlmCallSummary.WouldNotFit(samples, 0));
    }
}
