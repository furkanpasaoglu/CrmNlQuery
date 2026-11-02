using System.Net;
using CrmNlQuery.Application.Setup.Models;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Llm;

namespace CrmNlQuery.Tests.Infrastructure.External.Llm;

/// <summary>
/// Tests the setup wizard's model check against a small local server, and that the CRM check's task read passes the service read policy.
/// </summary>
public class LlmSetupProbeTests
{
    private static async Task<IReadOnlyList<SetupCheck>> RunAgainstAsync(string provider, string path, int status, string body, string model)
    {
        var port = 0;
        using var listener = new HttpListener();
        for (port = 20000 + Random.Shared.Next(20000); ; port++)
        {
            try { listener.Prefixes.Clear(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start(); break; }
            catch (HttpListenerException) { }
        }
        var serve = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            Assert.Equal(path, context.Request.Url!.AbsolutePath);
            context.Response.StatusCode = status;
            var bytes = System.Text.Encoding.UTF8.GetBytes(body);
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        });
        var result = await new LlmSetupProbe().RunAsync(new ModelProbeInput(provider, $"http://127.0.0.1:{port}", model, null));
        await serve;
        return result;
    }

    [Fact]
    public async Task OllamaWithTheModelPassesAndAnUntaggedNameMatchesItsTag()
    {
        var checks = await RunAgainstAsync("Ollama", "/api/tags", 200, "{\"models\":[{\"name\":\"qwen3.6:27b\"}]}", "qwen3.6");

        Assert.All(checks, c => Assert.Equal(SetupCheckStatus.Ok, c.Status));
        Assert.Contains(checks, c => c.Id == "model.present");
    }

    [Fact]
    public async Task OllamaWithoutTheModelFailsWithNotListed()
    {
        var checks = await RunAgainstAsync("Ollama", "/api/tags", 200, "{\"models\":[{\"name\":\"other:1b\"}]}", "qwen3.6:27b");

        var present = Assert.Single(checks, c => c.Id == "model.present");
        Assert.Equal(SetupCheckStatus.Fail, present.Status);
        Assert.Equal("not_listed", present.Detail);
    }

    [Fact]
    public async Task VllmListsServedModels()
    {
        var checks = await RunAgainstAsync("Vllm", "/v1/models", 200, "{\"data\":[{\"id\":\"my-model\"}]}", "my-model");

        Assert.All(checks, c => Assert.Equal(SetupCheckStatus.Ok, c.Status));
    }

    [Fact]
    public async Task ARefusedKeyIsReportedWithoutTheBody()
    {
        var checks = await RunAgainstAsync("Vllm", "/v1/models", 401, "secret body", "m");

        var only = Assert.Single(checks);
        Assert.Equal("unauthorized", only.Detail);
    }

    [Fact]
    public async Task AnUnreachableServerFails()
    {
        var checks = await new LlmSetupProbe().RunAsync(new ModelProbeInput("Ollama", "http://127.0.0.1:1", "m", null));

        Assert.Equal("unreachable", Assert.Single(checks).Detail);
    }

    [Fact]
    public void TheCrmProbesTaskReadIsAllowedByTheServiceReadPolicy()
    {
        var filter = Uri.EscapeDataString("subject eq 'NLQ-login-" + new string('A', 43) + "'");

        ServiceReadPolicy.EnsureAllowed("tasks?$select=activityid&$filter=" + filter + "&$top=1");
        ServiceReadPolicy.EnsureAllowed("systemusers?$select=systemuserid,isdisabled&$top=20");
        ServiceReadPolicy.EnsureAllowed("systemusers(" + Guid.NewGuid().ToString("D") + ")/systemuserroles_association?$select=name&$top=200");
    }
}
