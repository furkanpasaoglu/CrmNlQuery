using CrmNlQuery.Application.Observability.Services;

namespace CrmNlQuery.Tests.Application.Observability.Services;

/// <summary>
/// Tests that cancellation records read the stage name from the request context.
/// </summary>
public class RequestContextStageTests
{
    /// <summary>
    /// Verifies that the stage is unknown until something sets it.
    /// </summary>
    [Fact]
    public void TheStage_IsUnknown_UntilSomethingSetsIt()
    {
        using (RequestContext.Begin(Guid.NewGuid()))
        {
            Assert.Equal("unknown", RequestContext.Stage);
        }
    }

    /// <summary>
    /// Verifies that the stage returns what was set.
    /// </summary>
    [Fact]
    public void TheStage_ReturnsWhatWasSet()
    {
        using (RequestContext.Begin(Guid.NewGuid()))
        {
            RequestContext.SetStage("plan");

            Assert.Equal("plan", RequestContext.Stage);
        }
    }

    /// <summary>
    /// Verifies that from the second chain step the stage carries a step prefix.
    /// </summary>
    [Fact]
    public void FromTheSecondChainStep_TheStageCarriesAStepPrefix()
    {
        using (RequestContext.Begin(Guid.NewGuid()))
        {
            RequestContext.SetChainStep(2);
            RequestContext.SetStage("crm");

            Assert.Equal("step2:crm", RequestContext.Stage);
        }
    }

    /// <summary>
    /// Verifies that the first chain step has no prefix.
    /// </summary>
    [Fact]
    public void TheFirstChainStep_HasNoPrefix()
    {
        using (RequestContext.Begin(Guid.NewGuid()))
        {
            RequestContext.SetChainStep(1);
            RequestContext.SetStage("crm");

            Assert.Equal("crm", RequestContext.Stage);
        }
    }

    /// <summary>
    /// Verifies that outside a request setting the stage does nothing.
    /// </summary>
    [Fact]
    public void OutsideARequest_SettingTheStage_DoesNothing()
    {
        RequestContext.SetStage("plan");
        RequestContext.SetChainStep(3);

        Assert.Equal("unknown", RequestContext.Stage);
    }

    /// <summary>
    /// Verifies that the deadline is expired only while watched and cancelled.
    /// </summary>
    [Fact]
    public void TheDeadline_IsExpiredOnlyWhileWatchedAndCancelled()
    {
        using (RequestContext.Begin(Guid.NewGuid()))
        {
            Assert.False(RequestContext.DeadlineExpired);
            var source = new CancellationTokenSource();
            using (RequestContext.WatchDeadline(source.Token))
            {
                Assert.False(RequestContext.DeadlineExpired);
                source.Cancel();
                Assert.True(RequestContext.DeadlineExpired);
            }
            Assert.False(RequestContext.DeadlineExpired);
        }
    }

    /// <summary>
    /// Verifies that when the caller aborted the deadline is not expired even though its linked token is cancelled.
    /// </summary>
    [Fact]
    public void WhenTheCallerAborted_TheDeadlineIsNotExpired_EvenThoughItsLinkedTokenIsCancelled()
    {
        using (RequestContext.Begin(Guid.NewGuid()))
        {
            var caller = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller.Token);
            using (RequestContext.WatchDeadline(linked.Token, caller.Token))
            {
                caller.Cancel();

                Assert.True(linked.IsCancellationRequested);
                Assert.False(RequestContext.DeadlineExpired);
            }
        }
    }

    /// <summary>
    /// Verifies that two concurrent requests do not see each others stage.
    /// </summary>
    [Fact]
    public async Task TwoConcurrentRequests_DoNotSeeEachOthersStage()
    {
        async Task<string> Run(string stage)
        {
            using (RequestContext.Begin(Guid.NewGuid()))
            {
                RequestContext.SetStage(stage);
                await Task.Delay(20);
                return RequestContext.Stage;
            }
        }

        var results = await Task.WhenAll(Run("plan"), Run("crm"));

        Assert.Equal(new[] { "plan", "crm" }, results);
    }
}
